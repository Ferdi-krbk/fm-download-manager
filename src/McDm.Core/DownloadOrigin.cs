using System.Text;

namespace McDm.Core;

/// <summary>Preserves Windows attachment-origin protections without storing private URLs.</summary>
public static class DownloadOrigin
{
    public static bool MarkFile(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            string stream = Path.GetFullPath(path) + ":Zone.Identifier";
            // Never lower an existing restricted-zone classification.
            if (File.Exists(stream) && File.ReadAllText(stream).Contains("ZoneId=4", StringComparison.Ordinal)) return true;
            File.WriteAllText(stream, "[ZoneTransfer]\r\nZoneId=3\r\n", new UTF8Encoding(false));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; } // ADS are unavailable on some destination filesystems.
    }

    public static void MarkCompleted(DownloadItem item, CancellationToken cancel = default)
    {
        // Use the engine's exact output inventory. Never sweep a user's destination folder.
        if (!OperatingSystem.IsWindows()) return;
        if (item.ContentFiles is { Count: > 0 })
        {
            foreach (string file in item.ContentFiles)
            { cancel.ThrowIfCancellationRequested(); MarkFile(file); }
        }
        else MarkFile(item.SavePath);
    }
}
