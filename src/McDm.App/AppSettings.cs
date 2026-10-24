using System.IO;
using System.Text.Json;

namespace McDm.App;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DownloadDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public int ConnectionsPerDownload { get; set; } = 16;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int SpeedLimitKbps { get; set; } = 0; // 0 = unlimited
    public int MinCaptureSizeBytes { get; set; } = 0; // 0 = capture all downloads (like IDM)

    public string CapturedExtensions { get; set; } =
        "zip,rar,7z,tar,gz,bz2,xz,iso,exe,msi,bin,dmg,pkg,apk,deb,rpm,appx,mp4,mkv,webm,mov,avi,flv,mp3,flac,wav,m4a,aac,ogg,pdf,epub,doc,docx,xls,xlsx,ppt,pptx,txt,csv,torrent";

    public string PreferredVideoFormat { get; set; } = "mp4";
    public int MaxVideoResolution { get; set; } = 0;

    public string Language { get; set; } = "auto"; // "auto", "en", "tr"
    public string AppTheme { get; set; } = "dark"; // "dark", "light"
    public string AccentColor { get; set; } = "#E50914"; // Default High-Octane Crimson
    public bool StartWithWindows { get; set; } = false;
    public bool ClipboardWatcherEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AutoCategorizeSubfolders { get; set; } = true;
    public bool VirusScanEnabled { get; set; } = false;
    public string CustomAntivirusPath { get; set; } = "";
    public string CustomAntivirusArgs { get; set; } = "\"{file}\"";

    // Scheduler (Zamanlayıcı)
    public bool SchedulerEnabled { get; set; } = false;
    public string SchedulerStartTime { get; set; } = "02:00";
    public string SchedulerStopTime { get; set; } = "07:00";
    public bool SchedulerAutoResume { get; set; } = true;
    public bool SchedulerAutoPause { get; set; } = true;

    public static string SettingsFilePath =>
        McDm.Core.AppPaths.SettingsFile();

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    loaded.ConnectionsPerDownload = Math.Clamp(loaded.ConnectionsPerDownload <= 0 ? 16 : loaded.ConnectionsPerDownload, 1, 32);
                    loaded.MaxConcurrentDownloads = Math.Clamp(loaded.MaxConcurrentDownloads <= 0 ? 3 : loaded.MaxConcurrentDownloads, 1, 10);
                    if (string.IsNullOrWhiteSpace(loaded.AccentColor)) loaded.AccentColor = "#E50914";
                    if (string.IsNullOrWhiteSpace(loaded.AppTheme)) loaded.AppTheme = "dark";
                    if (string.IsNullOrWhiteSpace(loaded.CustomAntivirusArgs)) loaded.CustomAntivirusArgs = "\"{file}\"";
                    return loaded;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch { }
    }
}
