using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace McDm.Core;

public enum DownloadStatus { Queued, Downloading, Paused, Completed, Failed, Cancelled }
public enum DownloadKind { File, Video }

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

    public string FileName { get => _fileName; set => Set(ref _fileName, value); }
    public string SavePath { get => _savePath; set => Set(ref _savePath, value); }
    public long TotalBytes
    {
        get => _total;
        set { if (Set(ref _total, value)) Raise(nameof(Progress)); }
    }
    public long DownloadedBytes
    {
        get => _downloaded;
        set { if (Set(ref _downloaded, value)) Raise(nameof(Progress)); }
    }
    public DownloadStatus Status { get => _status; set => Set(ref _status, value); }
    public string? Error { get => _error; set => Set(ref _error, value); }

    /// <summary>Free-form phase text for video downloads ("Merging…"); null for plain files.</summary>
    [JsonIgnore] public string? Stage { get => _stage; set => Set(ref _stage, value); }
    [JsonIgnore] public double Speed { get => _speed; set => Set(ref _speed, value); }
    [JsonIgnore] public int ActiveConnections { get => _connections; set => Set(ref _connections, value); }
    [JsonIgnore] public double Progress => _total > 0 ? Math.Clamp((double)_downloaded / _total, 0, 1) : 0;
    [JsonIgnore] internal CancellationTokenSource? Cts { get; set; }
}
