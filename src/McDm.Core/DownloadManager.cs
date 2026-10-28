using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Net.NetworkInformation;

namespace McDm.Core;

public sealed class DownloadManager : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly List<DownloadItem> _all = new();
    private readonly SegmentedDownloader _engine;
    private readonly VideoDownloader? _videoEngine;
    private readonly TorrentDownloader? _torrentEngine;
    private readonly SynchronizationContext? _ui;
    private readonly string _dbPath;
    private readonly Dictionary<string, Task> _runningWorkers = new();
    private readonly Dictionary<string, Task> _manualScans = new();
    private readonly Dictionary<string, CancellationTokenSource> _manualScanCancellation = new();
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new();
    private readonly HashSet<string> _deleteOnExit = new();
    private readonly CancellationTokenSource _lifetime = new();
    private NetworkAvailabilityChangedEventHandler? _networkHandler;
    private bool _disposed;

    /// <summary>UI-bound mirror of the download list; mutated on the supplied SynchronizationContext.</summary>
    public ObservableCollection<DownloadItem> Items { get; } = new();

    public string DefaultDownloadDirectory { get; set; }
    public bool AutoCategorizeSubfolders { get; set; } = false;
    public bool VirusScanEnabled { get; set; } = false;
    public string? CustomAntivirusPath { get; set; }
    public string? CustomAntivirusArgs { get; set; }
    public int DefaultConnections { get; set; } = 8;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public SpeedLimiter SpeedLimiter { get; } = new();

    public event Action<DownloadItem>? ItemCompleted;

    public DownloadManager(string dataDirectory, string? downloadDirectory = null,
                           SegmentedDownloader? engine = null, SynchronizationContext? uiContext = null,
                           VideoDownloader? videoEngine = null, TorrentDownloader? torrentEngine = null)
    {
        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, "downloads.json");
        _engine = engine ?? new SegmentedDownloader(options: new EngineOptions { Limiter = SpeedLimiter });
        _videoEngine = videoEngine;
        _torrentEngine = torrentEngine;
        _ui = uiContext;
        DefaultDownloadDirectory = downloadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Load();

        try
        {
            _networkHandler = (_, e) =>
            {
                if (e.IsAvailable)
                {
                    lock (_gate)
                    {
                        if (_disposed) return;
                        foreach (var item in _all.Where(i => i.Status == DownloadStatus.Failed && IsTransientNetworkError(i.Error)))
                        {
                            item.Status = DownloadStatus.Queued;
                            item.Error = null;
                            item.AutoRetryCount = 0;
                        }
                    }
                    Pump();
                }
            };
            NetworkChange.NetworkAvailabilityChanged += _networkHandler;
        }
        catch { }
    }

    // ───────────────────────── queue reordering ─────────────────────────

    public bool MoveUp(string id)
    {
        lock (_gate)
        {
            int idx = _all.FindIndex(i => i.Id == id);
            if (idx <= 0) return false;
            var item = _all[idx];
            _all.RemoveAt(idx);
            _all.Insert(idx - 1, item);
            Post(() =>
            {
                int uiIdx = Items.IndexOf(item);
                if (uiIdx > 0) Items.Move(uiIdx, uiIdx - 1);
            });
            Save();
            Pump();
            return true;
        }
    }

    public bool MoveDown(string id)
    {
        lock (_gate)
        {
            int idx = _all.FindIndex(i => i.Id == id);
            if (idx < 0 || idx >= _all.Count - 1) return false;
            var item = _all[idx];
            _all.RemoveAt(idx);
            _all.Insert(idx + 1, item);
            Post(() =>
            {
                int uiIdx = Items.IndexOf(item);
                if (uiIdx >= 0 && uiIdx < Items.Count - 1) Items.Move(uiIdx, uiIdx + 1);
            });
            Save();
            Pump();
            return true;
        }
    }

    // ───────────────────────── commands ─────────────────────────

    public DownloadItem Add(DownloadRequest request)
    {
        bool isHttp = Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https");
        bool isMagnet = request.Url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
        bool isTorrent = request.Url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

        if (isMagnet || isTorrent)
        {
            request.Kind = DownloadKind.Torrent;
            if (_torrentEngine == null)
                throw new NotSupportedException("Torrent downloads are not available");
        }
        else if (!isHttp)
        {
            throw new ArgumentException("Only http(s), magnet and torrent URLs are supported");
        }

        // Fast-path: Direct static video files (.mp4/.mkv/.webm) from movie or web sites
        // that do NOT require yt-dlp extraction or audio conversion are routed directly
        // to the ultra-fast 16-channel SegmentedDownloader!
        if (request.Kind == DownloadKind.Video && !request.AudioOnly && IsDirectStaticMedia(request.Url))
        {
            request.Kind = DownloadKind.File;
        }

        if (request.Kind == DownloadKind.Video && (_videoEngine == null || !_videoEngine.Available))
            throw new NotSupportedException("Video downloads are not available (yt-dlp missing)");

        string targetDir = !string.IsNullOrWhiteSpace(request.Directory)
            ? request.Directory
            : (AutoCategorizeSubfolders
                ? CategoryHelper.ResolveDestinationDirectory(DefaultDownloadDirectory, request, true)
                : DefaultDownloadDirectory);

        string initialFileName = "";
        bool hasMeaningfulReqName = !string.IsNullOrWhiteSpace(request.FileName) &&
            (request.Kind == DownloadKind.Video ? !FileNames.IsGenericVideoName(request.FileName) : !FileNames.IsGenericFileName(request.FileName));

        if (hasMeaningfulReqName)
        {
            initialFileName = FileNames.Sanitize(request.FileName);
            // If request filename has no extension, but URL has one, preserve the URL's extension
            if (string.IsNullOrEmpty(Path.GetExtension(initialFileName)) &&
                Uri.TryCreate(request.Url, UriKind.Absolute, out var uriWithExt))
            {
                var urlExt = Path.GetExtension(uriWithExt.AbsolutePath);
                if (!string.IsNullOrEmpty(urlExt) && urlExt.Length <= 8)
                {
                    initialFileName += urlExt;
                }
            }
        }
        else if (request.Url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(request.Url, @"dn=([^&]+)");
            initialFileName = match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : "magnet-" + Guid.NewGuid().ToString("N")[..8];
        }
        else if (Uri.TryCreate(request.Url, UriKind.Absolute, out var parsedUri))
        {
            var seg = Path.GetFileName(parsedUri.AbsolutePath);
            if (request.Kind == DownloadKind.Video && (string.IsNullOrWhiteSpace(seg) || FileNames.IsGenericVideoName(seg)))
            {
                initialFileName = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
                    ? "Video Download"
                    : "Video İndirmesi";
            }
            else if (!string.IsNullOrWhiteSpace(seg))
            {
                initialFileName = FileNames.Sanitize(Uri.UnescapeDataString(seg));
            }
            else
            {
                initialFileName = parsedUri.Host;
            }
        }

        var item = new DownloadItem
        {
            Request = request,
            Directory = targetDir,
            FileName = initialFileName,
            Status = DownloadStatus.Queued,
        };
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _all.Add(item);
        }
        Post(() => Items.Insert(0, item));
        Save();
        Pump();
        return item;
    }

    public void ResumeAll()
    {
        lock (_gate)
        {
            foreach (var item in _all.Where(i => i.Status is DownloadStatus.Paused or DownloadStatus.Failed))
            {
                item.Status = DownloadStatus.Queued;
                item.Error = null;
                item.AutoRetryCount = 0;
                _retryAfter.Remove(item.Id);
            }
        }
        Save();
        Pump();
    }

    public void PauseAll()
    {
        lock (_gate)
        {
            var running = _all.Where(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Queued).ToList();
            foreach (var item in running) Pause(item.Id);
        }
    }

    public DownloadItem? Find(string id)
    {
        lock (_gate) return _all.FirstOrDefault(i => i.Id == id);
    }

    public IReadOnlyList<DownloadItem> Snapshot(int take = 50)
    {
        lock (_gate) return _all.OrderByDescending(i => i.CreatedAt).Take(take).ToList();
    }

    public int ActiveCount
    {
        get { lock (_gate) return _all.Count(i => i.Status == DownloadStatus.Downloading); }
    }

    public bool Pause(string id)
    {
        var item = Find(id);
        if (item == null) return false;
        if (item.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            item.Status = DownloadStatus.Paused;
            item.Cts?.Cancel();
            Save();
            return true;
        }
        return false;
    }

    public bool Resume(string id)
    {
        var item = Find(id);
        if (item == null || item.Status is not (DownloadStatus.Paused or DownloadStatus.Failed)) return false;
        lock (_gate) if (_manualScans.ContainsKey(id)) return false;
        item.Error = null;
        item.AutoRetryCount = 0;
        lock (_gate) _retryAfter.Remove(item.Id);
        item.Status = DownloadStatus.Queued;
        Save();
        Pump();
        return true;
    }

    public bool Cancel(string id)
    {
        var item = Find(id);
        if (item == null) return false;
        if (item.Status is DownloadStatus.Completed or DownloadStatus.Cancelled) return false;
        item.Status = DownloadStatus.Cancelled;
        if (item.Cts != null) item.Cts.Cancel();   // even a paused worker may still be unwinding
        else CleanupPartial(item);
        Save();
        return true;
    }

    public bool Remove(string id, bool deleteFile = false)
    {
        var item = Find(id);
        if (item == null) return false;
        if (item.Cts != null || item.Status == DownloadStatus.Downloading || item.Status == DownloadStatus.Queued)
            Cancel(id);
        lock (_gate) if (_manualScanCancellation.TryGetValue(id, out var pendingScan)) pendingScan.Cancel();
        if (deleteFile)
        {
            lock (_gate)
            {
                if (item.Cts != null) _deleteOnExit.Add(item.Id);
                else DeleteFiles(item);
            }
        }
        lock (_gate) _all.Remove(item);
        Post(() => Items.Remove(item));
        Save();
        return true;
    }

    // ───────────────────────── scheduling ─────────────────────────

    private void Pump()
    {
        List<DownloadItem> toStart = new();
        lock (_gate)
        {
            if (_disposed) return;
            var finished = _runningWorkers.Where(kv => kv.Value.IsCompleted).Select(kv => kv.Key).ToList();
            foreach (var id in finished) _runningWorkers.Remove(id);

            int running = _all.Count(i => i.Status == DownloadStatus.Downloading);
            foreach (var i in _all.Where(i => i.Status == DownloadStatus.Queued))
            {
                if (running >= MaxConcurrentDownloads) break;
                if (_retryAfter.TryGetValue(i.Id, out var due) && due > DateTimeOffset.UtcNow) continue;
                if (_runningWorkers.TryGetValue(i.Id, out var existing) && !existing.IsCompleted) continue;

                _retryAfter.Remove(i.Id);
                i.Status = DownloadStatus.Downloading;
                i.Cts = new CancellationTokenSource();
                toStart.Add(i);
                running++;
            }

            foreach (var item in toStart)
            {
                _runningWorkers[item.Id] = Task.Run(() => RunItemAsync(item));
            }
        }
    }

    private async Task RunItemAsync(DownloadItem item)
    {
        var cts = item.Cts!;
        int connections = Math.Clamp(item.Request.Segments ?? DefaultConnections, 1, 32);
        try
        {
            if (File.Exists(item.SavePath) || Directory.Exists(item.SavePath))
            {
                // A previous attempt may have finished downloading but been paused during scanning.
                cts.Token.ThrowIfCancellationRequested();
            }
            else if (item.Request.Kind == DownloadKind.Video)
            {
                if (_videoEngine == null) throw new FatalDownloadException("Video engine is not configured");
                await _videoEngine.RunAsync(item, cts.Token);
            }
            else if (item.Request.Kind == DownloadKind.Torrent)
            {
                if (_torrentEngine == null) throw new FatalDownloadException("Torrent engine is not configured");
                await _torrentEngine.RunAsync(item, cts.Token);
            }
            else
            {
                await _engine.RunAsync(item, connections, cts.Token);
            }
            if (!string.IsNullOrEmpty(item.WorkDir))
            {
                try { if (Directory.Exists(item.WorkDir)) Directory.Delete(item.WorkDir, recursive: true); } catch { }
                item.WorkDir = null;
            }
            DownloadOrigin.MarkCompleted(item, cts.Token);
            if (VirusScanEnabled && !string.IsNullOrEmpty(item.SavePath) && (File.Exists(item.SavePath) || Directory.Exists(item.SavePath)))
            {
                await RunVirusScanAsync(item, CustomAntivirusPath, CustomAntivirusArgs, cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();
            item.Status = DownloadStatus.Completed;
            item.Error = null;
            item.AutoRetryCount = 0;
            ItemCompleted?.Invoke(item);
        }
        catch (OperationCanceledException)
        {
            // Pause()/Cancel() already set the status
        }
        catch (Exception ex)
        {
            if (item.Status == DownloadStatus.Downloading)
            {
                if (IsTransientNetworkError(ex) && !cts.IsCancellationRequested && item.AutoRetryCount < 10)
                {
                    item.AutoRetryCount++;
                    int delaySec = Math.Min(30, item.AutoRetryCount * 3 + 2);
                    bool isTr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);
                    item.Stage = isTr
                        ? $"Ağ kesintisi! {delaySec} sn içinde tekrar denenecek ({item.AutoRetryCount}/10)..."
                        : $"Network drop! Reconnecting in {delaySec}s ({item.AutoRetryCount}/10)...";
                    item.Status = DownloadStatus.Queued;
                    lock (_gate) _retryAfter[item.Id] = DateTimeOffset.UtcNow.AddSeconds(delaySec);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delaySec), _lifetime.Token);
                            Pump();
                        }
                        catch (OperationCanceledException) { }
                    });
                }
                else
                {
                    item.Status = DownloadStatus.Failed;
                    item.Error = ex.Message;
                }
            }
        }
        finally
        {
            item.Speed = 0;
            item.ActiveConnections = 0;
            item.Cts = null;
            cts.Dispose();
            if (item.Status == DownloadStatus.Cancelled) CleanupPartial(item);
            lock (_gate)
            {
                if (_deleteOnExit.Remove(item.Id)) DeleteFiles(item);
                _runningWorkers.Remove(item.Id);
            }
            Save();
            Pump();
        }
    }

    public Task ScanExistingAsync(string id)
    {
        lock (_gate)
        {
            if (_disposed || _runningWorkers.ContainsKey(id)) return Task.CompletedTask;
            if (_manualScans.TryGetValue(id, out var running)) return running;
            var item = _all.FirstOrDefault(item => item.Id == id);
            if (item == null || (!File.Exists(item.SavePath) && !Directory.Exists(item.SavePath))) return Task.CompletedTask;
            bool scanOnlyFailure = item.Status == DownloadStatus.Failed && (item.ScanResult is "attention" or "unverified")
                && (item.Error?.Contains("scan", StringComparison.OrdinalIgnoreCase) == true || item.Error?.Contains("tarama", StringComparison.OrdinalIgnoreCase) == true);
            var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _manualScanCancellation[id] = scanCancellation;
            return _manualScans[id] = Task.Run(async () =>
            {
                try
                {
                    await RunVirusScanAsync(item, CustomAntivirusPath, CustomAntivirusArgs, scanCancellation.Token);
                    if (scanOnlyFailure && (item.ScanResult is "passed" or "unverified") && item.TotalBytes > 0
                        && item.DownloadedBytes >= item.TotalBytes && File.Exists(item.SavePath) && new FileInfo(item.SavePath).Length == item.TotalBytes)
                    { item.Status = DownloadStatus.Completed; item.Error = null; }
                }
                catch (OperationCanceledException) { item.ScanResult = "unverified"; item.Stage = "Scan cancelled"; }
                catch (FatalDownloadException error) { item.Status = DownloadStatus.Failed; item.Error = error.Message; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { item.ScanResult = "unverified"; item.Stage = "Scan incomplete: file unavailable"; }
                finally
                {
                    lock (_gate) { _manualScans.Remove(id); _manualScanCancellation.Remove(id); }
                    scanCancellation.Dispose();
                    Save();
                }
            });
        }
    }

    internal static async Task RunVirusScanAsync(DownloadItem item, string? customAvPath, string? customAvArgs, CancellationToken cancel,
        IReadOnlyList<AntivirusProfile>? candidatesOverride = null)
    {
        bool tr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        var candidates = candidatesOverride ?? AntivirusDetection.DetectCandidates(customAvPath, customAvArgs);
        item.ScanResult = "unverified";
        if (candidates.Count == 0)
        {
            item.Stage = tr ? "Tarama yap\u0131lamad\u0131: uyumlu taray\u0131c\u0131 bulunamad\u0131" : "Not scanned: no compatible scanner found";
            return;
        }
        foreach (var profile in candidates)
        {
            cancel.ThrowIfCancellationRequested();
            item.Stage = tr ? $"{profile.Name} ile taran\u0131yor\u2026" : $"Scanning with {profile.Name}\u2026";
            var start = profile.ForFile(item.SavePath);
            try
            {
                var result = await AntivirusScanner.RunDetailedAsync(start, TimeSpan.FromMinutes(3), cancel);
                if (result.PolicyBlocked)
                {
                    item.ScanResult = "attention";
                    throw new FatalDownloadException(tr ? "Dosya Windows g\u00fcvenlik ilkesi taraf\u0131ndan engellendi" : "File blocked by Windows security policy");
                }
                if (!File.Exists(item.SavePath) && !Directory.Exists(item.SavePath))
                {
                    item.ScanResult = "attention";
                    throw new FatalDownloadException(tr ? "Dosya tarama s\u0131ras\u0131nda kald\u0131r\u0131ld\u0131 veya karantinaya al\u0131nd\u0131" : "File removed or quarantined during scanning");
                }
                if (result.ThreatReported || profile.Kind == "kaspersky" && result.ExitCode is 101 or 102)
                {
                    item.ScanResult = "attention";
                    item.Stage = tr ? "Antivir\u00fcs tehdit bildirdi; dosyay\u0131 a\u00e7madan koruma uygulamas\u0131n\u0131 kontrol edin" : "Antivirus reported a threat; review it in your protection app";
                    throw new FatalDownloadException(item.Stage);
                }
                if (result.ExitCode != 0)
                {
                    item.Stage = tr ? $"{profile.Name} taramas\u0131 tamamlanamad\u0131 (kod {result.ExitCode}); indirme korundu" : $"{profile.Name} scan incomplete (code {result.ExitCode}); download preserved";
                    continue;
                }
                if (profile.Kind == "kaspersky" && !result.ScanCompleted)
                {
                    item.Stage = tr ? "Kaspersky dosyan\u0131n tarand\u0131\u011f\u0131n\u0131 do\u011frulamad\u0131; atlanan, hatal\u0131 veya \u015fifreli dosya olabilir"
                        : "Kaspersky did not confirm a completed file scan; the file may have been skipped, unreadable or encrypted";
                    continue;
                }
                if ((profile.Kind is "amsi" or "amsi-microsoft") && !result.ScanCompleted) continue;
                item.ScanResult = "passed";
                string providerName = profile.Name;
                item.Stage = tr ? $"{providerName} taramas\u0131 tamamland\u0131" : $"{providerName} scan completed";
                return;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (FatalDownloadException) { throw; }
            catch (Exception ex) when (ex is TimeoutException or IOException or System.ComponentModel.Win32Exception)
            {
                item.Stage = tr ? "⚠ Tarama tamamlanamadı; dosyanın güvenliği doğrulanmadı" : "⚠ Scan incomplete; file safety unverified";
            }
        }
    }

    private static void CleanupPartial(DownloadItem item)
    {
        if (!string.IsNullOrEmpty(item.WorkDir))
        {
            try { if (Directory.Exists(item.WorkDir)) Directory.Delete(item.WorkDir, recursive: true); } catch { }
        }
        if (string.IsNullOrEmpty(item.SavePath)) return;
        SegmentedDownloader.TryDelete(item.SavePath + ".mcdm-part");
        SegmentedDownloader.TryDelete(item.SavePath + ".mcdm");
        SegmentedDownloader.TryDelete(item.SavePath + ".mcdm.tmp");
    }

    private static void DeleteFiles(DownloadItem item)
    {
        CleanupPartial(item);
        string root = Path.GetFullPath(item.Directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in item.ContentFiles)
        {
            if (Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase)) SegmentedDownloader.TryDelete(file);
        }
        SegmentedDownloader.TryDelete(item.SavePath);
        // Never recursively delete a torrent directory: it may contain unrelated user files.
        try
        {
            if (Directory.Exists(item.SavePath) && !Path.GetFullPath(item.SavePath).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(item.SavePath, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ───────────────────────── persistence ─────────────────────────

    private void Save()
    {
        try
        {
            lock (_gate)
            {
                var rows = JsonSerializer.SerializeToNode(_all, JsonOpts)!.AsArray();
                for (int index = 0; index < rows.Count; index++)
                {
                    var request = rows[index]!["Request"]!.AsObject();
                    var secrets = new JsonObject
                    {
                        ["CookieHeader"] = request["CookieHeader"]?.DeepClone(),
                        ["Cookies"] = request["Cookies"]?.DeepClone(),
                    };
                    request.Remove("CookieHeader");
                    request.Remove("Cookies");
                    if (_all[index].Status is not (DownloadStatus.Completed or DownloadStatus.Cancelled)
                        && (_all[index].Request.Cookies is { Count: > 0 } || !string.IsNullOrEmpty(_all[index].Request.CookieHeader)))
                        request["ProtectedCredentials"] = CredentialProtection.Protect(secrets.ToJsonString());
                }
                var json = rows.ToJsonString(JsonOpts);
                var tmp = _dbPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.WriteAllText(_dbPath + ".bak.tmp", json);
                File.Move(_dbPath + ".bak.tmp", _dbPath + ".bak", overwrite: true);
                File.Move(tmp, _dbPath, overwrite: true);
            }
        }
        catch { /* best effort */ }
    }

    private void Load()
    {
        List<DownloadItem>? list = null;
        bool found = false;
        foreach (var path in new[] { _dbPath, _dbPath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            found = true;
            try
            {
                var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
                foreach (var row in rows)
                {
                    var request = row?["Request"]?.AsObject();
                    if (request?["ProtectedCredentials"]?.GetValue<string>() is { } encrypted
                        && CredentialProtection.Unprotect(encrypted) is { } clear)
                    {
                        var credentials = JsonNode.Parse(clear)!;
                        request["CookieHeader"] = credentials["CookieHeader"]?.DeepClone();
                        request["Cookies"] = credentials["Cookies"]?.DeepClone();
                    }
                    request?.Remove("ProtectedCredentials");
                }
                list = rows.Deserialize<List<DownloadItem>>(JsonOpts) ?? new();
                break;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { }
        }
        if (list == null)
        {
            if (found) throw new InvalidDataException("Download history and its backup could not be read. Your history has not been overwritten.");
            return;
        }
            foreach (var i in list)
            {
                // nothing is running right after startup; let the user resume explicitly
                if (i.Status is DownloadStatus.Downloading or DownloadStatus.Queued) i.Status = DownloadStatus.Paused;
                _all.Add(i);
            }
            foreach (var i in _all.OrderBy(i => i.CreatedAt)) Items.Insert(0, i);
        // Migrate legacy plaintext cookies immediately, including the recovery copy.
        Save();
    }

    private void Post(Action a)
    {
        if (_ui != null) _ui.Post(_ => a(), null); else a();
    }

    public static bool IsDirectStaticMedia(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("youtube.com") || host.Contains("youtu.be") ||
            host.Contains("twitter.com") || host.Contains("x.com") ||
            host.Contains("tiktok.com") || host.Contains("instagram.com") ||
            host.Contains("vimeo.com") || host.Contains("dailymotion.com") ||
            host.Contains("reddit.com") || host.Contains("twitch.tv"))
        {
            return false;
        }
        var path = uri.AbsolutePath.ToLowerInvariant();
        return (path.EndsWith(".mp4") || path.EndsWith(".mkv") || path.EndsWith(".webm") || path.EndsWith(".avi") || path.EndsWith(".mov"))
               && !path.EndsWith(".m3u8") && !path.EndsWith(".mpd");
    }

    public static bool IsTransientNetworkError(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is System.Net.Http.HttpRequestException ||
            ex is System.Net.Sockets.SocketException ||
            ex is TimeoutException)
            return true;
        if (ex.InnerException != null && IsTransientNetworkError(ex.InnerException))
            return true;
        return IsTransientNetworkError(ex.Message);
    }

    public static bool IsTransientNetworkError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var msg = message.ToLowerInvariant();
        return msg.Contains("network") || msg.Contains("socket") || msg.Contains("connection") ||
               msg.Contains("timed out") || msg.Contains("timeout") || msg.Contains("host") ||
               msg.Contains("reset by peer") || msg.Contains("eof") || msg.Contains("handshake") ||
               msg.Contains("ağ") || msg.Contains("bağlantı");
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task[] workers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            foreach (var i in _all.Where(i => i.Cts != null || i.Status == DownloadStatus.Queued))
            {
                i.Status = DownloadStatus.Paused;
                i.Cts?.Cancel();
            }
            workers = _runningWorkers.Values.Concat(_manualScans.Values).ToArray();
        }
        if (_networkHandler != null) NetworkChange.NetworkAvailabilityChanged -= _networkHandler;
        try { await Task.WhenAll(workers).ConfigureAwait(false); } catch { }
        _torrentEngine?.Dispose();
        Save();
    }
}
