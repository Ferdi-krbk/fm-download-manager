using System.IO;
using MonoTorrent;
using MonoTorrent.Client;

namespace McDm.Core;

public sealed class TorrentDownloader : IDisposable
{
    private readonly Lazy<ClientEngine> _engine;
    private readonly Dictionary<string, TorrentManager> _managers = new();
    private readonly object _lock = new();
    private readonly Func<long>? _speedLimitBytes;
    private long _lastAppliedRateLimit;

    public TorrentDownloader(Func<long>? speedLimitBytes = null, EngineSettings? engineSettings = null)
    {
        _speedLimitBytes = speedLimitBytes;
        _engine = new Lazy<ClientEngine>(() =>
        {
            long initialRate = _speedLimitBytes?.Invoke() ?? 0;
            int maxRate = initialRate > 0 ? (int)Math.Min(initialRate, int.MaxValue) : 0;
            _lastAppliedRateLimit = maxRate;
            var settings = engineSettings == null ? new EngineSettingsBuilder
            {
                AllowPortForwarding = true, AutoSaveLoadDhtCache = true, AutoSaveLoadFastResume = true,
            } : new EngineSettingsBuilder(engineSettings);
            settings.MaximumDownloadRate = maxRate;
            return new ClientEngine(settings.ToSettings());
        });
    }

    public bool IsTorrent(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("magnet:?xt=urn:btih:", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunAsync(DownloadItem item, CancellationToken cancel)
    {
        var engine = _engine.Value;
        TorrentManager manager;

        string targetDir = item.Directory;
        if (string.IsNullOrWhiteSpace(targetDir))
            targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Torrents");
        Directory.CreateDirectory(targetDir);

        if (item.Request.Url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var magnet = MagnetLink.Parse(item.Request.Url);
            item.FileName = string.IsNullOrWhiteSpace(item.FileName)
                ? (magnet.Name ?? "torrent-" + item.Id[..8])
                : item.FileName;
            manager = await engine.AddAsync(magnet, targetDir);
        }
        else
        {
            // Direct .torrent file path or HTTP(S) URL
            string torrentFilePath = item.Request.Url;
            string? tempTorrentPath = null;
            try
            {
                if (!File.Exists(torrentFilePath) &&
                    Uri.TryCreate(item.Request.Url, UriKind.Absolute, out var tUri) &&
                    tUri.Scheme is "http" or "https")
                {
                    tempTorrentPath = Path.Combine(Path.GetTempPath(), "mcdm-" + Guid.NewGuid().ToString("N")[..8] + ".torrent");
                    using var http = RequestCookies.CreateClient(item.Request, TimeSpan.FromSeconds(20));
                    var bytes = await http.GetByteArrayAsync(tUri, cancel);
                    await File.WriteAllBytesAsync(tempTorrentPath, bytes, cancel);
                    torrentFilePath = tempTorrentPath;
                }

                if (File.Exists(torrentFilePath))
                {
                    var torrent = await Torrent.LoadAsync(torrentFilePath);
                    item.FileName = string.IsNullOrWhiteSpace(item.FileName) ? torrent.Name : item.FileName;
                    manager = await engine.AddAsync(torrent, targetDir);
                }
                else
                {
                    throw new NotSupportedException("Torrent download requires magnet link or valid .torrent file/URL");
                }
            }
            finally
            {
                if (tempTorrentPath != null) SegmentedDownloader.TryDelete(tempTorrentPath);
            }
        }

        lock (_lock) _managers[item.Id] = manager;

        try
        {
            await manager.StartAsync();

            while (!cancel.IsCancellationRequested)
            {
                long desiredLimit = _speedLimitBytes?.Invoke() ?? 0;
                int maxRate = desiredLimit > 0 ? (int)Math.Min(desiredLimit, int.MaxValue) : 0;
                if (maxRate != _lastAppliedRateLimit)
                {
                    _lastAppliedRateLimit = maxRate;
                    try
                    {
                        var updatedSettings = new EngineSettingsBuilder(engine.Settings)
                        {
                            MaximumDownloadRate = maxRate
                        }.ToSettings();
                        await engine.UpdateSettingsAsync(updatedSettings);
                    }
                    catch { }
                }

                item.TotalBytes = manager.Torrent?.Size ?? -1;
                item.DownloadedBytes = item.TotalBytes > 0 ? (long)(item.TotalBytes * manager.Progress / 100.0) : 0;
                item.Speed = manager.Monitor.DownloadRate;
                item.ActiveConnections = manager.OpenConnections;

                if (manager.Torrent != null)
                    item.FileName = manager.Torrent.Name;

                if (manager.State == TorrentState.Error)
                    throw new FatalDownloadException("Torrent engine reported an error; check the destination and retry");

                item.Stage = $"{manager.State} ({manager.OpenConnections} peers)";

                if (manager.Complete)
                {
                    item.DownloadedBytes = item.TotalBytes > 0 ? item.TotalBytes : item.DownloadedBytes;
                    item.ContentFiles = manager.Files.Select(f => f.DownloadCompleteFullPath).ToList();
                    item.SavePath = manager.Files.Count == 1 ? manager.Files[0].DownloadCompleteFullPath : manager.ContainingDirectory;
                    DownloadOrigin.MarkCompleted(item, cancel);
                    break;
                }

                await Task.Delay(500, cancel);
            }
            cancel.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            await manager.PauseAsync();
            throw;
        }
        finally
        {
            lock (_lock) _managers.Remove(item.Id);
            // Stop saves fast-resume data. Removing only the registration permits the same
            // item to be resumed without a duplicate InfoHash and never deletes user data.
            try { await manager.StopAsync(); }
            finally { await engine.RemoveAsync(manager); }
        }
    }

    public async Task PauseAsync(string id)
    {
        TorrentManager? mgr;
        lock (_lock) _managers.TryGetValue(id, out mgr);
        if (mgr != null) await mgr.PauseAsync();
    }

    public async Task StopAsync(string id)
    {
        TorrentManager? mgr;
        lock (_lock) _managers.TryGetValue(id, out mgr);
        if (mgr != null) await mgr.StopAsync();
    }

    public void Dispose()
    {
        if (_engine.IsValueCreated)
        {
            try { _engine.Value.Dispose(); } catch { }
        }
    }
}
