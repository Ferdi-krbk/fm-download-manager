using System.Text.Json;
using System.Text.Json.Serialization;

namespace McDm.Core;

/// <summary>Well-known per-user locations (all under %LOCALAPPDATA%\MC Download Manager on Windows).</summary>
public static class AppPaths
{
    public const string AppFolderName = "MC Download Manager";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("MCDM_DATA_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string SettingsFile(string? dataDir = null) => Path.Combine(dataDir ?? DataDirectory, "settings.json");
    public static string TokenFile(string? dataDir = null) => Path.Combine(dataDir ?? DataDirectory, "bridge.token");
}

/// <summary>User settings persisted to settings.json. Thread-safe enough for UI + engine reads (plain values).</summary>
public sealed class AppSettings
{
    public static readonly string[] DefaultCaptureExtensions =
    {
        "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "iso", "img", "dmg", "pkg",
        "exe", "msi", "msix", "apk", "deb", "rpm", "jar", "bin",
        "pdf", "epub", "mobi", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv",
        "mp4", "mkv", "avi", "mov", "webm", "flv", "wmv", "mp3", "flac", "wav", "m4a", "aac", "ogg",
        "psd", "ai", "ttf", "otf",
    };

    /// <summary>null/empty = the user's Downloads folder.</summary>
    public string? DownloadFolder { get; set; }
    public int Connections { get; set; } = 8;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public List<string> CaptureExtensions { get; set; } = new(DefaultCaptureExtensions);
    /// <summary>Browser downloads smaller than this (MB) stay in the browser. 0 = take everything.</summary>
    public int MinCaptureSizeMB { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ClipboardWatcher { get; set; } = true;
    public bool Notifications { get; set; } = true;
    /// <summary>Global speed limit in KB/s, 0 = unlimited.</summary>
    public int SpeedLimitKBps { get; set; }
    /// <summary>"mp4" | "mkv" | "webm".</summary>
    public string VideoContainer { get; set; } = "mp4";
    /// <summary>Maximum video height (0 = best available).</summary>
    public int VideoMaxHeight { get; set; } = 1080;
    /// <summary>"auto" (follow Windows) | "en" | "tr".</summary>
    public string Language { get; set; } = "auto";
    public bool FirstRunDone { get; set; }

    public string ResolvedDownloadFolder =>
        string.IsNullOrWhiteSpace(DownloadFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : DownloadFolder!;

    /// <summary>Clamps every field into its valid range (settings.json can be hand-edited).</summary>
    public void Normalize()
    {
        Connections = Math.Clamp(Connections, 1, 16);
        MaxConcurrentDownloads = Math.Clamp(MaxConcurrentDownloads, 1, 32);
        MinCaptureSizeMB = Math.Max(0, MinCaptureSizeMB);
        SpeedLimitKBps = Math.Max(0, SpeedLimitKBps);
        VideoMaxHeight = VideoMaxHeight < 0 ? 0 : VideoMaxHeight;
        if (VideoContainer is not ("mp4" or "mkv" or "webm")) VideoContainer = "mp4";
        if (Language is not ("auto" or "en" or "tr")) Language = "auto";
        CaptureExtensions = (CaptureExtensions ?? new())
            .Select(e => (e ?? "").Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length is > 0 and <= 8 && e.All(char.IsLetterOrDigit))
            .Distinct().ToList();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile();
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
                s.Normalize();
                return s;
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SettingsFile();
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
