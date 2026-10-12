using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McDm.Core;

public sealed class DownloadManager : IDisposable
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
    private readonly SynchronizationContext? _ui;
    private readonly string _dbPath;

    /// <summary>UI-bound mirror of the download list; mutated on the supplied SynchronizationContext.</summary>
    public ObservableCollection<DownloadItem> Items { get; } = new();

    public string DefaultDownloadDirectory { get; set; }
    public int DefaultConnections { get; set; } = 8;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public SpeedLimiter SpeedLimiter { get; } = new();

    public event Action<DownloadItem>? ItemCompleted;

    public DownloadManager(string dataDirectory, string? downloadDirectory = null,
                           SegmentedDownloader? engine = null, SynchronizationContext? uiContext = null,
                           VideoDownloader? videoEngine = null)
    {
        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, "downloads.json");
        _engine = engine ?? new SegmentedDownloader(options: new EngineOptions { Limiter = SpeedLimiter });
        _videoEngine = videoEngine;
        _ui = uiContext;
        DefaultDownloadDirectory = downloadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Load();
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
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only http(s) URLs are supported");
        if (request.Kind == DownloadKind.Video && (_videoEngine == null || !_videoEngine.Available))
            throw new NotSupportedException("Video downloads are not available (yt-dlp missing)");

        var item = new DownloadItem
        {
            Request = request,
            Directory = DefaultDownloadDirectory,
            FileName = string.IsNullOrWhiteSpace(request.FileName) ? "" : FileNames.Sanitize(request.FileName),
            Status = DownloadStatus.Queued,
        };
        lock (_gate) _all.Add(item);
        Post(() => Items.Insert(0, item));
        Save();
        Pump();
        return item;
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
        item.Error = null;
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
        var wasRunning = item.Status == DownloadStatus.Downloading;
        item.Status = DownloadStatus.Cancelled;
        if (wasRunning) item.Cts?.Cancel();   // the run task cleans up the files
        else CleanupPartial(item);
        Save();
        return true;
    }

    public bool Remove(string id, bool deleteFile = false)
    {
        var item = Find(id);
        if (item == null) return false;
        if (item.Status == DownloadStatus.Downloading || item.Status == DownloadStatus.Queued)
            Cancel(id);
        if (deleteFile && item.Status == DownloadStatus.Completed) SegmentedDownloader.TryDelete(item.SavePath);
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
            int running = _all.Count(i => i.Status == DownloadStatus.Downloading);
            foreach (var i in _all.Where(i => i.Status == DownloadStatus.Queued))
            {
                if (running >= MaxConcurrentDownloads) break;
                i.Status = DownloadStatus.Downloading;
                i.Cts = new CancellationTokenSource();
                toStart.Add(i);
                running++;
            }
        }
        foreach (var item in toStart) _ = Task.Run(() => RunItemAsync(item));
    }

    private async Task RunItemAsync(DownloadItem item)
    {
        var cts = item.Cts!;
        int connections = Math.Clamp(item.Request.Segments ?? DefaultConnections, 1, 16);
        try
        {
            if (item.Request.Kind == DownloadKind.Video)
            {
                if (_videoEngine == null) throw new FatalDownloadException("Video engine is not configured");
                await _videoEngine.RunAsync(item, cts.Token);
            }
            else
            {
                await _engine.RunAsync(item, connections, cts.Token);
            }
            item.Status = DownloadStatus.Completed;
            item.Error = null;
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
                item.Status = DownloadStatus.Failed;
                item.Error = ex.Message;
            }
        }
        finally
        {
            item.Speed = 0;
            item.ActiveConnections = 0;
            item.Cts = null;
            cts.Dispose();
            if (item.Status == DownloadStatus.Cancelled) CleanupPartial(item);
            Save();
            Pump();
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

    // ───────────────────────── persistence ─────────────────────────

    private void Save()
    {
        try
        {
            lock (_gate)
            {
                var json = JsonSerializer.Serialize(_all, JsonOpts);
                var tmp = _dbPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _dbPath, overwrite: true);
            }
        }
        catch { /* best effort */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_dbPath)) return;
            var list = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(_dbPath), JsonOpts) ?? new();
            foreach (var i in list)
            {
                // nothing is running right after startup; let the user resume explicitly
                if (i.Status is DownloadStatus.Downloading or DownloadStatus.Queued) i.Status = DownloadStatus.Paused;
                _all.Add(i);
            }
            foreach (var i in _all.OrderBy(i => i.CreatedAt)) Items.Insert(0, i);
        }
        catch { /* corrupt db: start empty */ }
    }

    private void Post(Action a)
    {
        if (_ui != null) _ui.Post(_ => a(), null); else a();
    }

    public void Dispose()
    {
        List<DownloadItem> running;
        lock (_gate) running = _all.Where(i => i.Cts != null).ToList();
        foreach (var i in running) { i.Status = DownloadStatus.Paused; i.Cts?.Cancel(); }
        Save();
    }
}
