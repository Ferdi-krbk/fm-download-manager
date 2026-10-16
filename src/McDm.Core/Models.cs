using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace McDm.Core;

public enum DownloadStatus { Queued, Downloading, Paused, Completed, Failed, Cancelled }
public enum DownloadKind { File, Video, Torrent }

public sealed class BrowserCookie
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public double? ExpirationDate { get; set; }
}

/// <summary>Body of POST /download (same shape as the macOS app's bridge).</summary>
public sealed class DownloadRequest
{
    public string Url { get; set; } = "";
    public string? FileName { get; set; }
    public string? Referer { get; set; }
    public string? UserAgent { get; set; }
    public string? CookieHeader { get; set; }
    public List<BrowserCookie>? Cookies { get; set; }
    public DownloadKind Kind { get; set; } = DownloadKind.File;
    public int? Segments { get; set; }
    public string? Directory { get; set; }
    public string? CustomFormat { get; set; }
    public string? Container { get; set; }
    public bool AudioOnly { get; set; }
    public bool SubtitlesOnly { get; set; }
    public bool WriteSubtitles { get; set; }
    public string? SubtitleLangs { get; set; }
    public string? SubtitleUrl { get; set; }
    public List<SubtitleTrackInfo>? Subtitles { get; set; }
    public List<VideoQualityInfo>? VideoQualities { get; set; }
    public List<AudioTrackInfo>? AudioTracks { get; set; }
    public string? SubtitleContent { get; set; }
}

public sealed class VideoQualityInfo
{
    public int Height { get; set; }
    public string Label { get; set; } = "";
    public string? FormatId { get; set; }
    public long? Bitrate { get; set; }
}

public sealed class AudioTrackInfo
{
    public string TrackId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Lang { get; set; } = "";
    public string? FormatId { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class SubtitleTrackInfo
{
    public string Url { get; set; } = "";
    public string Lang { get; set; } = "tr";
    public string? Code { get => Lang; set => Lang = value ?? "tr"; }
    public string? Label { get; set; }
}

public sealed class Chunk
{
    public long Start { get; set; }
    /// <summary>Inclusive end offset; -1 = unknown length (streamed without Range).</summary>
    public long End { get; set; }
    public long Done { get; set; }
    [JsonIgnore] public long Length => End < 0 ? -1 : End - Start + 1;
    [JsonIgnore] public bool IsComplete => End >= 0 && Done >= Length;
}

public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class DownloadItem : ObservableBase
{
    private string _fileName = "";
    private string _savePath = "";
    private long _total = -1;
    private long _downloaded;
    private DownloadStatus _status = DownloadStatus.Queued;
    private string? _error;
    private double _speed;
    private int _connections;
    private string? _stage;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DownloadRequest Request { get; set; } = new();
    public string Directory { get; set; } = "";
    public string? WorkDir { get; set; }
    public bool SupportsRanges { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string FileName
    {
        get => _fileName;
        set
        {
            if (Set(ref _fileName, value)) Raise(nameof(DisplayName));
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_fileName)) return _fileName;
            if (!string.IsNullOrWhiteSpace(Request?.FileName)) return Request.FileName;
            if (!string.IsNullOrWhiteSpace(Request?.Url))
            {
                if (Request.Url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Request.Url, @"dn=([^&]+)");
                    if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
                    return "Magnet (" + Id[..6] + ")";
                }
                if (Uri.TryCreate(Request.Url, UriKind.Absolute, out var u))
                {
                    var seg = Path.GetFileName(u.AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(seg)) return Uri.UnescapeDataString(seg);
                    return u.Host;
                }
                return Request.Url;
            }
            return "İsimsiz İndirme";
        }
    }
    public string SavePath { get => _savePath; set => Set(ref _savePath, value); }
    public long TotalBytes
    {
        get => _total;
        set
        {
            if (Set(ref _total, value))
            {
                Raise(nameof(Progress));
                Raise(nameof(EtaFormatted));
            }
        }
    }
    public long DownloadedBytes
    {
        get => _downloaded;
        set
        {
            if (Set(ref _downloaded, value))
            {
                Raise(nameof(Progress));
                Raise(nameof(EtaFormatted));
            }
        }
    }
    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(EtaFormatted));
            }
        }
    }
    public string? Error { get => _error; set => Set(ref _error, value); }

    /// <summary>Free-form phase text for video downloads ("Merging…"); null for plain files.</summary>
    [JsonIgnore] public string? Stage { get => _stage; set => Set(ref _stage, value); }
    [JsonIgnore]
    public double Speed
    {
        get => _speed;
        set
        {
            if (Set(ref _speed, value))
            {
                Raise(nameof(EtaFormatted));
            }
        }
    }
    [JsonIgnore] public int ActiveConnections { get => _connections; set => Set(ref _connections, value); }
    [JsonIgnore] public double Progress => _total > 0 ? Math.Clamp((double)_downloaded / _total, 0, 1) : 0;

    [JsonIgnore]
    public TimeSpan? Eta
    {
        get
        {
            if (_status != DownloadStatus.Downloading || _speed <= 1024 || _total <= 0 || _downloaded >= _total)
                return null;
            long remaining = _total - _downloaded;
            double secs = remaining / _speed;
            if (secs > 86400 * 30 || double.IsInfinity(secs) || double.IsNaN(secs))
                return null;
            return TimeSpan.FromSeconds(secs);
        }
    }

    [JsonIgnore]
    public string EtaFormatted
    {
        get
        {
            if (_status == DownloadStatus.Completed) return "Tamamlandı";
            if (_status != DownloadStatus.Downloading) return "—";
            var eta = Eta;
            if (!eta.HasValue) return "—";
            var ts = eta.Value;
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}g {ts.Hours}sa";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}sa {ts.Minutes}dk";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}dk {ts.Seconds}sn";
            return $"{Math.Max(1, ts.Seconds)}sn";
        }
    }
    [JsonIgnore] internal CancellationTokenSource? Cts { get; set; }
}
