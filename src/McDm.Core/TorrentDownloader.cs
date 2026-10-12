using System.IO;
using MonoTorrent;
using MonoTorrent.Client;

namespace McDm.Core;

public sealed class TorrentDownloader : IDisposable
{
    private readonly Lazy<ClientEngine> _engine;
    private readonly Dictionary<string, TorrentManager> _managers = new();
    private readonly object _lock = new();

    public TorrentDownloader()
    {
        _engine = new Lazy<ClientEngine>(() =>
        {
            var settings = new EngineSettingsBuilder
            {
                AllowPortForwarding = true,
                AutoSaveLoadDhtCache = true,
                AutoSaveLoadFastResume = true,
            }.ToSettings();
            return new ClientEngine(settings);
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
            // Direct .torrent file path or URL
            if (File.Exists(item.Request.Url))
            {
                var torrent = await Torrent.LoadAsync(item.Request.Url);
                item.FileName = string.IsNullOrWhiteSpace(item.FileName) ? torrent.Name : item.FileName;
                manager = await engine.AddAsync(torrent, targetDir);
            }
            else
            {
                throw new NotSupportedException("Torrent download requires magnet link or local .torrent file");
            }
        }

        lock (_lock) _managers[item.Id] = manager;

        try
        {
            await manager.StartAsync();

            while (!cancel.IsCancellationRequested)
            {
                item.DownloadedBytes = manager.Monitor.DataBytesReceived;
                item.TotalBytes = manager.Torrent?.Size ?? -1;
                item.Speed = manager.Monitor.DownloadRate;
                item.ActiveConnections = manager.OpenConnections;

                if (manager.Torrent != null && string.IsNullOrEmpty(item.FileName))
                    item.FileName = manager.Torrent.Name;

                item.Stage = $"{manager.State} ({manager.OpenConnections} peers)";

                if (manager.Complete)
                {
                    item.DownloadedBytes = item.TotalBytes > 0 ? item.TotalBytes : item.DownloadedBytes;
                    item.SavePath = Path.Combine(targetDir, item.FileName);
                    break;
                }

                await Task.Delay(500, cancel);
            }
        }
        catch (OperationCanceledException)
        {
            await manager.PauseAsync();
            throw;
        }
        finally
        {
            lock (_lock) _managers.Remove(item.Id);
            if (cancel.IsCancellationRequested)
            {
                try { await manager.StopAsync(); } catch { }
            }
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
