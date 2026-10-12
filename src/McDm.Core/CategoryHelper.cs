namespace McDm.Core;

public static class CategoryHelper
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".m4v", ".ts", ".m4s", ".3gp"
    };

    private static readonly HashSet<string> MusicExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma", ".opus"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff"
    };

    private static readonly HashSet<string> AppExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".apk", ".appx", ".iso", ".img", ".dmg", ".pkg"
    };

    private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".epub", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".csv"
    };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz"
    };

    public static string GetCategorySubfolder(string? fileName, string? url, DownloadKind kind)
    {
        if (kind == DownloadKind.Video) return "Videos";
        if (kind == DownloadKind.Torrent) return "Torrents";

        string ext = "";
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            ext = Path.GetExtension(fileName);
        }
        if (string.IsNullOrEmpty(ext) && !string.IsNullOrWhiteSpace(url))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                ext = Path.GetExtension(uri.AbsolutePath);
            }
        }

        if (string.IsNullOrEmpty(ext)) return "";

        if (VideoExts.Contains(ext)) return "Videos";
        if (MusicExts.Contains(ext)) return "Music";
        if (ImageExts.Contains(ext)) return "Images";
        if (AppExts.Contains(ext)) return "Programs";
        if (DocExts.Contains(ext)) return "Documents";
        if (ArchiveExts.Contains(ext)) return "Compressed";
        if (string.Equals(ext, ".torrent", StringComparison.OrdinalIgnoreCase)) return "Torrents";

        return "";
    }

    public static string ResolveDestinationDirectory(string baseDirectory, string? fileName, string? url, DownloadKind kind, bool autoCategorize)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (!autoCategorize) return baseDirectory;

        var subfolder = GetCategorySubfolder(fileName, url, kind);
        if (string.IsNullOrEmpty(subfolder)) return baseDirectory;

        var target = Path.Combine(baseDirectory, subfolder);
        try { Directory.CreateDirectory(target); } catch { return baseDirectory; }
        return target;
    }
}
