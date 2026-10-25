using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace McDm.Core;

/// <summary>How to start an external tool: executable plus fixed leading arguments (lets tests run a fake via "dotnet fake.dll").</summary>
public sealed record ToolLauncher(string FileName, IReadOnlyList<string>? LeadingArgs = null)
{
    public IReadOnlyList<string> Lead => LeadingArgs ?? Array.Empty<string>();
}

/// <summary>Finds yt-dlp and ffmpeg: user-updated copy in the data folder first, then Tools\ next to the executable.</summary>
public sealed class ToolPaths
{
    private static readonly string Exe = OperatingSystem.IsWindows() ? ".exe" : "";

    public string ToolsDirectory { get; }
    public string UserToolsDirectory { get; }

    public ToolPaths(string? appDirectory = null, string? userToolsDirectory = null)
    {
        ToolsDirectory = Path.Combine(appDirectory ?? AppContext.BaseDirectory, "Tools");
        UserToolsDirectory = userToolsDirectory ?? Path.Combine(AppPaths.DataDirectory, "Tools");
    }

    public string? YtDlpPath => Find("yt-dlp", "MCDM_YTDLP");
    public string? FfmpegPath => Find("ffmpeg", "MCDM_FFMPEG");
    public string? QuickJsPath => Find("quickjs", "MCDM_QUICKJS") ?? Find("qjs", "MCDM_QUICKJS");
    public string? NodePath => Find("node", "MCDM_NODE") ?? FindNodeDefault();
    public string? DenoPath => Find("deno", "MCDM_DENO") ?? FindDenoDefault();

    public bool YtDlpAvailable => YtDlpPath != null;
    /// <summary>Where "Update yt-dlp" writes the new binary (always writable, per user).</summary>
    public string UserYtDlpTarget => Path.Combine(UserToolsDirectory, "yt-dlp" + Exe);
    public string UserQuickJsTarget => Path.Combine(UserToolsDirectory, "quickjs" + Exe);

    public (string Name, string Path)? GetPreferredJsRuntime()
    {
        if (QuickJsPath is { } qjs) return ("quickjs", qjs);
        if (NodePath is { } node) return ("node", node);
        if (DenoPath is { } deno) return ("deno", deno);
        return null;
    }

    private static string? FindNodeDefault()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var p86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] common = [
            Path.Combine(pf, "nodejs", "node.exe"),
            Path.Combine(p86, "nodejs", "node.exe")
        ];
        return common.FirstOrDefault(File.Exists);
    }

    private static string? FindDenoDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string deno = Path.Combine(userProfile, ".deno", "bin", "deno.exe");
        return File.Exists(deno) ? deno : null;
    }

    public async Task<string> DownloadYtDlpAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FMDownloadManager/1.0.0");
        string metadata = await client.GetStringAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", ct).ConfigureAwait(false);
        using var release = System.Text.Json.JsonDocument.Parse(metadata);
        string tag = release.RootElement.GetProperty("tag_name").GetString()!;
        string prefix = "https://github.com/yt-dlp/yt-dlp/releases/download/" + Uri.EscapeDataString(tag) + "/";
        string sums = await client.GetStringAsync(prefix + "SHA2-256SUMS", ct).ConfigureAwait(false);
        var match = Regex.Match(sums, @"(?m)^([a-fA-F0-9]{64})[ \t]+\*?yt-dlp\.exe[ \t]*\r?$");
        if (!match.Success) throw new System.Security.Cryptography.CryptographicException("yt-dlp checksum was not available");
        return await VerifiedToolDownload.SaveAsync(client, prefix + "yt-dlp.exe", match.Groups[1].Value, UserYtDlpTarget, ct).ConfigureAwait(false);
    }

    public async Task<string> DownloadQuickJsAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FMDownloadManager/1.0.0");
        return await VerifiedToolDownload.SaveAsync(client,
            "https://github.com/quickjs-ng/quickjs/releases/download/v0.17.0/qjs-windows-x86_64.exe",
            "2aeabf0092c3262d6b2609824418f7dd7ed1f1df939f73b2b15645230cac0d77", UserQuickJsTarget, ct).ConfigureAwait(false);
    }

    private string? Find(string name, string envVar)
    {
        if (Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } fromEnv && File.Exists(fromEnv)) return fromEnv;

        var candidates = new List<string>
        {
            Path.Combine(UserToolsDirectory, name + Exe),
            Path.Combine(ToolsDirectory, name + Exe),
            Path.Combine(AppContext.BaseDirectory, "Tools", name + Exe),
            Path.Combine(AppContext.BaseDirectory, name + Exe),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", name + Exe),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + Exe),
            Path.Combine(Environment.CurrentDirectory, "Tools", name + Exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FM Download Manager", "Tools", name + Exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FM Download Manager", "Tools", name + Exe),
        };

        if (Environment.ProcessPath is { Length: > 0 } procPath && Path.GetDirectoryName(procPath) is { } procDir)
        {
            candidates.Add(Path.Combine(procDir, "Tools", name + Exe));
            candidates.Add(Path.Combine(procDir, name + Exe));
        }

        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var p = Path.Combine(dir, name + Exe);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        }

        return null;
    }
}

public sealed class VideoOptions
{
    /// <summary>"mp4" | "mkv" | "webm" | "mp3".</summary>
    public string Container { get; set; } = "mp4";
    /// <summary>0 = best available.</summary>
    public int MaxHeight { get; set; } = 1080;
    public string? CustomFormat { get; set; }
    public bool AudioOnly { get; set; }
    public bool SubtitlesOnly { get; set; }
    public bool WriteSubtitles { get; set; }
    public string? SubtitleLangs { get; set; }
    public string? SubtitleUrl { get; set; }
    public List<SubtitleTrackInfo>? Subtitles { get; set; }
    public string? SubtitleContent { get; set; }
    /// <summary>Speed limit in bytes per second (0 = unlimited).</summary>
    public long SpeedLimitBytes { get; set; }
}

/// <summary>One parsed --progress-template line.</summary>
public sealed record VideoProgress(string Status, long Downloaded, long? Total, double Speed, int? Fragment, int? FragmentCount, bool IsTotalEstimated = false);

/// <summary>
/// Runs yt-dlp as a child process (ArgumentList only, never a shell string) and maps its output onto a DownloadItem.
/// Pause = kill the process and keep the work directory; resume = start again (yt-dlp -c continues the partial files).
/// </summary>
public sealed class VideoDownloader
{
    private readonly Func<ToolLauncher?> _ytDlp;
    private readonly Func<string?> _ffmpeg;
    private readonly Func<VideoOptions> _options;

    public const string ProgressPrefix = "MCDM_PROGRESS";
    public const string FilePrefix = "MCDM_FILE";
    public const string TitlePrefix = "MCDM_TITLE";

    public VideoDownloader(Func<ToolLauncher?> ytDlp, Func<string?> ffmpeg, Func<VideoOptions> options)
    {
        _ytDlp = ytDlp; _ffmpeg = ffmpeg; _options = options;
    }

    public bool Available => _ytDlp() != null;

    public static bool IsVideoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be") ||
               host.Contains("tiktok.com") || host.Contains("instagram.com") ||
               host.Contains("twitter.com") || host.Contains("x.com") ||
               host.Contains("vimeo.com") || host.Contains("dailymotion.com") ||
               host.Contains("facebook.com") || host.Contains("fb.watch") ||
               host.Contains("twitch.tv") || host.Contains("bilibili.com") ||
               host.Contains("reddit.com");
    }

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be");
    }

    // ───────────────────────── argument construction ─────────────────────────

    public static string FormatSelector(VideoOptions o) =>
        !string.IsNullOrWhiteSpace(o.CustomFormat) ? o.CustomFormat : "bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/b[ext=mp4]/b/best";

    public static string FormatSort(VideoOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.CustomFormat)) return "";
        var parts = new List<string>();
        if (o.MaxHeight > 0) parts.Add("res:" + o.MaxHeight.ToString(CultureInfo.InvariantCulture));
        if (o.Container is null or "mp4" or "") parts.Add("ext:mp4:m4a");   // prefer streams that can be merged into MP4 without re-encoding
        return string.Join(",", parts);
    }

    /// <summary>Everything after the executable. Public so tests can assert on it.</summary>
    public static List<string> BuildArguments(string url, VideoOptions o, string outputDir, string workDir,
                                              string? cookiesFile, string? ffmpegLocation,
                                              string? userAgent = null, string? referer = null,
                                              string? fileName = null,
                                              (string Name, string Path)? jsRuntime = null)
    {
        var cacheDir = Path.Combine(AppPaths.DataDirectory, "yt-dlp-cache");
        try { Directory.CreateDirectory(cacheDir); } catch { }

        var a = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "--newline",
            "--progress",
            "--no-colors",
            "--encoding", "utf-8",
            "--windows-filenames",
            "--trim-filenames", "60",
            "--no-mtime",
            "--concurrent-fragments", IsYouTubeUrl(url) ? "5" : "1",
            "--buffer-size", "1024K",
            "--no-check-certificates",
            "--socket-timeout", "20",
            "--extractor-retries", "10",
            "--file-access-retries", "20",
            "--fragment-retries", "30",
            "--retry-sleep", "fragment:0.5",
            "--cache-dir", cacheDir,
            "-c",                                  // continue partial downloads (resume)
        };
        a.RemoveAll(string.IsNullOrEmpty);
        if (IsYouTubeUrl(url))
        {
            a.Add("--throttled-rate");
            a.Add("100K");
        }
        if (jsRuntime != null)
        {
            a.Add("--js-runtimes");
            a.Add($"{jsRuntime.Value.Name}:{jsRuntime.Value.Path}");
        }
        if (!IsYouTubeUrl(url))
        {
            a.Add("--extractor-args");
            a.Add("generic:impersonate");
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                a.Add("--user-agent");
                a.Add(userAgent);
            }
            if (!string.IsNullOrWhiteSpace(referer))
            {
                a.Add("--referer");
                a.Add(referer);
                if (Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
                {
                    a.Add("--add-header");
                    a.Add($"Origin:{refUri.Scheme}://{refUri.Authority}");
                }
            }
        }

        if (o.SubtitlesOnly)
        {
            a.Add("--skip-download");
            a.Add("--write-subs");
            a.Add("--write-auto-subs");
            a.Add("--sub-langs");
            a.Add(!string.IsNullOrWhiteSpace(o.SubtitleLangs) ? o.SubtitleLangs : "tr,en");
            a.Add("--convert-subs");
            a.Add("srt");
            a.Add("--no-abort-on-error");
        }
        else
        {
            a.Add("-f");
            a.Add(FormatSelector(o));
            var sort = FormatSort(o);
            if (sort.Length > 0) { a.Add("-S"); a.Add(sort); }
            if (o.AudioOnly || o.Container == "mp3")
            {
                a.Add("-x");
                a.Add("--audio-format");
                a.Add("mp3");
            }
            else
            {
                string targetContainer = o.Container is "mkv" or "webm" ? o.Container : "mp4";
                a.Add("--merge-output-format"); a.Add(targetContainer);
            }

            if (o.WriteSubtitles)
            {
                a.Add("--write-subs");
                a.Add("--write-auto-subs");
                a.Add("--sub-langs");
                a.Add(!string.IsNullOrWhiteSpace(o.SubtitleLangs) ? o.SubtitleLangs : "tr,en");
                a.Add("--convert-subs");
                a.Add("srt");
                a.Add("--embed-subs");
                a.Add("--no-abort-on-error");
            }
        }
        if (o.SpeedLimitBytes > 0)
        {
            a.Add("--limit-rate");
            a.Add($"{o.SpeedLimitBytes}");
        }
        if (ffmpegLocation != null) { a.Add("--ffmpeg-location"); a.Add(ffmpegLocation); }
        if (cookiesFile != null) { a.Add("--cookies"); a.Add(cookiesFile); }
        a.Add("-P"); a.Add(outputDir);
        a.Add("-P"); a.Add("temp:" + workDir);
        string safeName = !string.IsNullOrWhiteSpace(fileName)
            ? FileNames.Sanitize(Path.GetFileNameWithoutExtension(fileName))
            : "";
        if (!string.IsNullOrWhiteSpace(safeName) && !FileNames.IsGenericVideoName(safeName))
        {
            if (safeName.Length > 60) safeName = safeName[..60].Trim();
            string uniqueSafeName = safeName;
            int counter = 1;
            while (File.Exists(Path.Combine(outputDir, uniqueSafeName + ".mp4")) ||
                   File.Exists(Path.Combine(outputDir, uniqueSafeName + ".mkv")) ||
                   File.Exists(Path.Combine(outputDir, uniqueSafeName + ".webm")) ||
                   File.Exists(Path.Combine(outputDir, uniqueSafeName + ".mp3")) ||
                   File.Exists(Path.Combine(outputDir, uniqueSafeName + ".srt")))
            {
                uniqueSafeName = $"{safeName} ({counter++})";
            }
            a.Add("-o"); a.Add(uniqueSafeName + ".%(ext)s");
        }
        else
        {
            a.Add("-o"); a.Add("%(title).60B.%(ext)s");
        }
        a.Add("--progress-template");
        a.Add("download:" + ProgressPrefix + "|%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|" +
              "%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s");
        a.Add("--print"); a.Add("before_dl:" + TitlePrefix + "|%(title)s");
        a.Add("--print"); a.Add("after_move:" + FilePrefix + "|%(filepath)s");
        a.Add("--no-simulate");
        a.Add("--");                               // everything after this is a URL, never an option
        a.Add(url);
        return a;
    }

    // ───────────────────────── output parsing ─────────────────────────

    public static VideoProgress? ParseProgress(string line)
    {
        if (!line.StartsWith(ProgressPrefix + "|", StringComparison.Ordinal)) return null;
        var p = line.TrimEnd().Split('|');
        if (p.Length < 8) return null;
        static long? L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v
            : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) ? (long)d : null;
        static int? I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        double speed = double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) && !double.IsNaN(sp) ? sp : 0;
        long? exactTotal = L(p[3]);
        return new VideoProgress(p[1], L(p[2]) ?? 0, exactTotal ?? L(p[4]), speed, I(p[6]), I(p[7]), exactTotal is not > 0);
    }

    /// <summary>Accumulates progress across yt-dlp's separate video/audio streams into one running total.</summary>
    public sealed class ProgressAccumulator
    {
        private long _finished;
        private long _lastStreamDownloaded;
        private long _lastStreamTotal;
        private readonly HashSet<string> _accountedFiles = new(StringComparer.OrdinalIgnoreCase);

        public void AddFinished(long bytes, string? fileKey = null)
        {
            if (fileKey != null && !_accountedFiles.Add(fileKey)) return;
            _finished += bytes;
        }

        public (long Downloaded, long Total) Apply(VideoProgress p)
        {
            long total = p.Total ?? 0;
            if (total <= 0 && p.Fragment is > 0 && p.FragmentCount is > 0 && p.Downloaded > 0)
                total = p.Downloaded * p.FragmentCount.Value / p.Fragment.Value;   // HLS/DASH: estimate from fragments

            // If yt-dlp transitioned to the next stream (e.g. video finished and audio started),
            // p.Downloaded will drop below _lastStreamDownloaded. Latch the finished stream!
            if (p.Downloaded < _lastStreamDownloaded && _lastStreamDownloaded > 0)
            {
                _finished += _lastStreamDownloaded;
                _lastStreamDownloaded = 0;
                _lastStreamTotal = 0;
            }

            _lastStreamDownloaded = p.Downloaded;
            _lastStreamTotal = Math.Max(total, p.Downloaded);
            var result = (_finished + p.Downloaded, total > 0 ? _finished + _lastStreamTotal : -1);
            if (p.Status == "finished")
            {
                _finished += _lastStreamDownloaded;
                _lastStreamDownloaded = 0;
                _lastStreamTotal = 0;
            }
            return result;
        }
    }

    // ───────────────────────── cookies ─────────────────────────

    /// <summary>Netscape cookie jar text for yt-dlp (--cookies). Hosts come from the cookie list or, failing that, the page URL.</summary>
    public static string BuildNetscapeCookies(DownloadRequest r)
    {
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
        static string Clean(string? s) => (s ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", "");
        if (r.Cookies is { Count: > 0 })
        {
            foreach (var c in r.Cookies)
            {
                if (string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Domain)) continue;
                bool sub = c.Domain.StartsWith('.');
                long exp = c.ExpirationDate is > 0 ? (long)c.ExpirationDate.Value : 0;
                sb.Append(c.HttpOnly ? "#HttpOnly_" : "").Append(Clean(c.Domain)).Append('\t')
                  .Append(sub ? "TRUE" : "FALSE").Append('\t').Append(Clean(string.IsNullOrEmpty(c.Path) ? "/" : c.Path)).Append('\t')
                  .Append(c.Secure ? "TRUE" : "FALSE").Append('\t').Append(exp).Append('\t')
                  .Append(Clean(c.Name)).Append('\t').Append(Clean(c.Value)).Append('\n');
            }
        }
        else if (!string.IsNullOrWhiteSpace(r.CookieHeader) && Uri.TryCreate(r.Url, UriKind.Absolute, out var u))
        {
            foreach (var pair in r.CookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                sb.Append(u.Host).Append("\tFALSE\t/\t").Append(u.Scheme == "https" ? "TRUE" : "FALSE").Append("\t0\t")
                  .Append(Clean(pair[..eq])).Append('\t').Append(Clean(pair[(eq + 1)..])).Append('\n');
            }
        }
        return sb.ToString();
    }

    // ───────────────────────── run ─────────────────────────

    public static SubtitleTrackInfo? PickBestSubtitle(List<SubtitleTrackInfo>? list, string? preferredLang)
    {
        if (list == null || list.Count == 0) return null;
        string rawPref = !string.IsNullOrWhiteSpace(preferredLang) ? preferredLang : "tr";
        string lang = rawPref.Split(',')[0].Split('-')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lang) || lang == "all") lang = "tr";

        static bool MatchesLang(SubtitleTrackInfo s, string target)
        {
            string sLang = (s.Lang ?? "").Split('-')[0].Trim().ToLowerInvariant();
            string sCode = (s.Code ?? "").Split('-')[0].Trim().ToLowerInvariant();
            if (sLang == target || sCode == target) return true;
            if (target == "tr")
            {
                return (s.Label ?? "").Contains("Türkçe", StringComparison.OrdinalIgnoreCase) ||
                       (s.Url ?? "").Contains("-tr-", StringComparison.OrdinalIgnoreCase) ||
                       (s.Url ?? "").Contains("_tr", StringComparison.OrdinalIgnoreCase) ||
                       (s.Url ?? "").Contains("lang=tr", StringComparison.OrdinalIgnoreCase);
            }
            if (target == "en")
            {
                return (s.Label ?? "").Contains("İngilizce", StringComparison.OrdinalIgnoreCase) ||
                       (s.Label ?? "").Contains("English", StringComparison.OrdinalIgnoreCase) ||
                       (s.Url ?? "").Contains("lang=en", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // 1. Exact language, non-forced, non-auto
        var nonForcedManual = list.FirstOrDefault(s =>
            MatchesLang(s, lang) &&
            !(s.Url ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase) &&
            !(s.Label ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase) &&
            !(s.Label ?? "").Contains("Zorunlu", StringComparison.OrdinalIgnoreCase) &&
            !(s.Label ?? "").Contains("Otomatik", StringComparison.OrdinalIgnoreCase));
        if (nonForcedManual != null) return nonForcedManual;

        // 2. Exact language, non-forced (including auto)
        var nonForcedLang = list.FirstOrDefault(s =>
            MatchesLang(s, lang) &&
            !(s.Url ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase) &&
            !(s.Label ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase) &&
            !(s.Label ?? "").Contains("Zorunlu", StringComparison.OrdinalIgnoreCase));
        if (nonForcedLang != null) return nonForcedLang;

        // 3. Language match even if forced
        var anyLang = list.FirstOrDefault(s => MatchesLang(s, lang));
        if (anyLang != null) return anyLang;

        // 4. Turkish non-forced
        var trNonForced = list.FirstOrDefault(s =>
            MatchesLang(s, "tr") &&
            !(s.Url ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase));
        if (trNonForced != null) return trNonForced;

        // 5. Any non-forced
        var anyNonForced = list.FirstOrDefault(s => !(s.Url ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase) && !(s.Label ?? "").Contains("forced", StringComparison.OrdinalIgnoreCase));
        if (anyNonForced != null) return anyNonForced;

        return list[0];
    }

    public static string NormalizeSubtitleFileLangCode(string? rawLangs, SubtitleTrackInfo? bestSub = null)
    {
        if (!string.IsNullOrWhiteSpace(rawLangs))
        {
            string first = rawLangs.Split(',')[0].Split('.')[0].Split('-')[0].Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(first) && first != "all" && first != "none")
                return first;
        }
        if (bestSub != null)
        {
            string b = (!string.IsNullOrWhiteSpace(bestSub.Lang) ? bestSub.Lang : bestSub.Code ?? "").Split('-')[0].Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(b) && b != "all" && b != "und") return b;
        }
        return "tr";
    }

    private static bool IsTurkish => (CultureInfo.CurrentUICulture ?? CultureInfo.InstalledUICulture).TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    public async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        var tool = _ytDlp();
        if (tool == null)
        {
            item.Stage = IsTurkish ? "yt-dlp hazırlanıyor..." : "Preparing yt-dlp...";
            try
            {
                var tp = new ToolPaths();
                string path = await tp.DownloadYtDlpAsync(ct).ConfigureAwait(false);
                tool = new ToolLauncher(path);
            }
            catch (Exception ex)
            {
                throw new FatalDownloadException(IsTurkish 
                    ? "yt-dlp aracı bulunamadı ve otomatik indirilemedi: " + ex.Message 
                    : "yt-dlp tool not found and could not be downloaded automatically: " + ex.Message);
            }
        }
        var baseOpt = _options();
        var opt = new VideoOptions
        {
            Container = !string.IsNullOrWhiteSpace(item.Request.Container) ? item.Request.Container : baseOpt.Container,
            MaxHeight = baseOpt.MaxHeight,
            CustomFormat = item.Request.CustomFormat,
            AudioOnly = item.Request.AudioOnly || item.Request.Container == "mp3",
            SubtitlesOnly = item.Request.SubtitlesOnly,
            WriteSubtitles = item.Request.WriteSubtitles || item.Request.SubtitlesOnly,
            SubtitleLangs = !string.IsNullOrWhiteSpace(item.Request.SubtitleLangs) ? item.Request.SubtitleLangs : "tr,en",
            SubtitleUrl = item.Request.SubtitleUrl,
            Subtitles = item.Request.Subtitles,
            SubtitleContent = item.Request.SubtitleContent,
            SpeedLimitBytes = baseOpt.SpeedLimitBytes
        };
        string outDir = string.IsNullOrEmpty(item.Directory) ? Directory.GetCurrentDirectory() : item.Directory;
        Directory.CreateDirectory(outDir);
        item.Directory = outDir;
        if (string.IsNullOrEmpty(item.WorkDir)) item.WorkDir = Path.Combine(outDir, ".mcdm-video-" + item.Id);
        Directory.CreateDirectory(item.WorkDir);

        var bestSub = PickBestSubtitle(opt.Subtitles, opt.SubtitleLangs);
        string? targetSubUrl = !string.IsNullOrWhiteSpace(opt.SubtitleUrl)
            ? opt.SubtitleUrl
            : bestSub?.Url;

        if (bestSub != null && !string.IsNullOrWhiteSpace(bestSub.Url) &&
            targetSubUrl != null && targetSubUrl.Contains("forced", StringComparison.OrdinalIgnoreCase) &&
            !bestSub.Url.Contains("forced", StringComparison.OrdinalIgnoreCase))
        {
            targetSubUrl = bestSub.Url;
        }

        string? rawSubContent = !string.IsNullOrWhiteSpace(opt.SubtitleContent) ? opt.SubtitleContent : null;
        string? srtContent = null;
        if (!string.IsNullOrWhiteSpace(rawSubContent))
        {
            if (rawSubContent.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || (targetSubUrl?.Contains(".vtt", StringComparison.OrdinalIgnoreCase) ?? false))
                srtContent = ConvertVttToSrt(rawSubContent);
            else if (rawSubContent.TrimStart().StartsWith("<"))
                srtContent = ConvertXmlTimedTextToSrt(rawSubContent);
            else
                srtContent = rawSubContent;
        }
        else if (!string.IsNullOrWhiteSpace(targetSubUrl))
        {
            item.Speed = 0;
            item.Stage = IsTurkish ? "Altyazılar İndiriliyor..." : "Downloading Subtitles...";
            srtContent = await DownloadSubtitleFileAsync(targetSubUrl, item.Request, ct);
        }

        string cleanSubLang = NormalizeSubtitleFileLangCode(opt.SubtitleLangs, bestSub);

        // 1. Direct subtitle-only download (fast HTTP download + VTT/XML->SRT conversion)
        if (opt.SubtitlesOnly && !string.IsNullOrWhiteSpace(srtContent) && opt.SubtitleLangs != "all")
        {
            item.Stage = IsTurkish ? "Altyazılar Kaydediliyor..." : "Saving Subtitles...";
            string baseName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(item.FileName) ? "subtitle" : item.FileName);
            string srtFile = Path.Combine(outDir, $"{baseName}.{cleanSubLang}.srt");
            await File.WriteAllTextAsync(srtFile, srtContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
            CleanAndNormalizeSubtitleFile(srtFile);

            item.SavePath = srtFile;
            item.FileName = Path.GetFileName(srtFile);
            long sz = new FileInfo(srtFile).Length;
            item.TotalBytes = sz;
            item.DownloadedBytes = sz;
            item.Status = DownloadStatus.Completed;
            item.Stage = IsTurkish ? "Tamamlandı" : "Completed";
            try { Directory.Delete(item.WorkDir!, recursive: true); } catch { }
            item.WorkDir = null;
            return;
        }

        // 2. Pre-fetch external subtitle if video download requires subtitles
        string? manualSrtPath = null;
        if (opt.WriteSubtitles && !string.IsNullOrWhiteSpace(srtContent))
        {
            try
            {
                manualSrtPath = Path.Combine(item.WorkDir, "manual_sub.srt");
                await File.WriteAllTextAsync(manualSrtPath, srtContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
                CleanAndNormalizeSubtitleFile(manualSrtPath);
            }
            catch { }
        }

        string? cookieFile = null;
        try
        {
            var cookieText = BuildNetscapeCookies(item.Request);
            if (cookieText.Contains('\t'))
            {
                cookieFile = Path.Combine(Path.GetTempPath(), "mcdm-" + Guid.NewGuid().ToString("N") + ".cookies.txt");
                await File.WriteAllTextAsync(cookieFile, cookieText, new UTF8Encoding(false), ct);
            }

            bool isYt = IsYouTubeUrl(item.Request.Url);
            if (!isYt && !opt.SubtitlesOnly && StreamProbeService.UpgradeToMasterHlsUrl(item.Request.Url) != item.Request.Url)
            {
                item.Stage = IsTurkish ? "Ana Akış (Master HLS) Çözülüyor..." : "Resolving Master HLS Stream...";
                item.Request.Url = await StreamProbeService.ResolveMasterHlsUrlAsync(
                    item.Request.Url,
                    item.Request.Referer,
                    item.Request.UserAgent,
                    item.Request.Cookies,
                    item.Request.CookieHeader,
                    ct);
            }

            var tp = new ToolPaths();
            var jsRuntime = tp.GetPreferredJsRuntime();

            string? firstAttemptCookies = isYt ? null : cookieFile;
            string? fallbackCookies = isYt ? cookieFile : null;

            try
            {
                await ExecuteYtDlpOnceAsync(item, opt, outDir, firstAttemptCookies, tool, jsRuntime, ct);
            }
            catch (FatalDownloadException ex) when (fallbackCookies != firstAttemptCookies && !ct.IsCancellationRequested &&
                   (ex.Message.Contains("reload", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("age", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("private", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase)))
            {
                // Retry with alternate cookie mode (with cookies for age-restricted YouTube, or without cookies if cookies caused Video unavailable/reload)
                await ExecuteYtDlpOnceAsync(item, opt, outDir, fallbackCookies, tool, jsRuntime, ct);
            }

            // 2b. If the downloaded video is silent (video-only stream) and a separate audio stream URL was captured on the page, download & mux it
            if (!string.IsNullOrEmpty(item.SavePath) && File.Exists(item.SavePath) && !opt.SubtitlesOnly && !opt.AudioOnly
                && !string.IsNullOrWhiteSpace(item.Request.AudioStreamUrl)
                && !string.Equals(item.Request.AudioStreamUrl, item.Request.Url, StringComparison.OrdinalIgnoreCase)
                && _ffmpeg() is { } extMuxFfmpeg && File.Exists(extMuxFfmpeg))
            {
                try
                {
                    if (!HasAudioStream(extMuxFfmpeg, item.SavePath))
                    {
                        item.Stage = IsTurkish ? "Harici Ses Akışı İndiriliyor ve Birleştiriliyor..." : "Downloading & Merging External Audio...";
                        item.Speed = 0;
                        string? extAudioFile = await DownloadExternalAudioStreamAsync(item, tool, firstAttemptCookies, jsRuntime, ct);
                        if (!string.IsNullOrEmpty(extAudioFile) && File.Exists(extAudioFile))
                        {
                            item.Speed = 0;
                            item.Stage = IsTurkish ? "Ses ve Görüntü Birleştiriliyor (FFmpeg)..." : "Merging Video & Audio (FFmpeg)...";
                            if (MergeVideoAndAudioWithFfmpeg(extMuxFfmpeg, item.SavePath, extAudioFile, item.SavePath))
                            {
                                long mergedLen = new FileInfo(item.SavePath).Length;
                                item.TotalBytes = mergedLen;
                                item.DownloadedBytes = mergedLen;
                            }
                            SegmentedDownloader.TryDelete(extAudioFile);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                finally
                {
                    item.Stage = null;
                }
            }

            // 3. Post-process video with subtitle (save .srt alongside and embed with ffmpeg)
            if (!string.IsNullOrEmpty(item.SavePath) && File.Exists(item.SavePath) && !opt.SubtitlesOnly && !opt.AudioOnly)
            {
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(item.SavePath);
                    string finalSrt = Path.Combine(outDir, $"{baseName}.{cleanSubLang}.srt");
                    if (manualSrtPath != null && File.Exists(manualSrtPath))
                    {
                        File.Copy(manualSrtPath, finalSrt, overwrite: true);
                    }

                    foreach (var srt in Directory.GetFiles(outDir, $"{baseName}*.srt"))
                    {
                        CleanAndNormalizeSubtitleFile(srt);
                    }

                    string? srtToEmbed = File.Exists(finalSrt) ? finalSrt : Directory.GetFiles(outDir, $"{baseName}*.srt").FirstOrDefault();
                    if (srtToEmbed != null && File.Exists(srtToEmbed) && _ffmpeg() is { } ffmpegPath && File.Exists(ffmpegPath))
                    {
                        item.Stage = IsTurkish ? "Altyazı Gömülüyor (FFmpeg)..." : "Embedding Subtitles (FFmpeg)...";
                        item.Speed = 0;
                        try
                        {
                            EmbedSubtitleWithFfmpeg(ffmpegPath, item.SavePath, srtToEmbed, cleanSubLang);
                        }
                        catch { }
                        finally
                        {
                            item.Stage = null;
                        }
                    }
                }
                catch { }
                finally
                {
                    item.Stage = null;
                }
            }

            // Clean up temporary work directory now that yt-dlp and subtitle muxing succeeded
            if (!string.IsNullOrEmpty(item.WorkDir) && Directory.Exists(item.WorkDir))
            {
                try { Directory.Delete(item.WorkDir, recursive: true); } catch { }
                item.WorkDir = null;
            }
        }
        finally
        {
            if (cookieFile != null) SegmentedDownloader.TryDelete(cookieFile);
            if ((item.Status == DownloadStatus.Completed || item.Status == DownloadStatus.Cancelled || (!string.IsNullOrEmpty(item.SavePath) && File.Exists(item.SavePath)))
                && !string.IsNullOrEmpty(item.WorkDir) && Directory.Exists(item.WorkDir))
            {
                try { Directory.Delete(item.WorkDir, recursive: true); } catch { }
                item.WorkDir = null;
            }
        }
    }

    private async Task ExecuteYtDlpOnceAsync(DownloadItem item, VideoOptions opt, string outDir, string? cookieFile,
                                             ToolLauncher tool, (string Name, string Path)? jsRuntime, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(tool.FileName)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var l in tool.Lead) psi.ArgumentList.Add(l);
        var targetFileParam = !string.IsNullOrWhiteSpace(item.Request.FileName) ? item.Request.FileName : null;
        foreach (var a in BuildArguments(item.Request.Url, opt, outDir, item.WorkDir!, cookieFile, _ffmpeg(), item.Request.UserAgent, item.Request.Referer, targetFileParam, jsRuntime))
            psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi };
        string? finalPath = null;
        string? mergerTarget = null;
        var downloadedDestinations = new List<string>();
        string? lastError = null;
        var acc = new ProgressAccumulator();
        if (Directory.Exists(item.WorkDir))
        {
            try
            {
                var existingStreams = Directory.GetFiles(item.WorkDir)
                    .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.Length > 0)
                    .ToList();
                foreach (var es in existingStreams)
                {
                    acc.AddFinished(es.Length, es.FullName);
                }
            }
            catch { }
        }
        var clock = Stopwatch.StartNew();
        var startUtc = DateTime.UtcNow.AddSeconds(-5);
        item.Stage = null;
        item.ActiveConnections = 1;
        string currentStreamType = IsTurkish ? "Akış" : "Stream";

        void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (VideoDownloader.ParseProgress(line) is { } p)
            {
                var (done, total) = acc.Apply(p);
                item.IsTotalEstimated = p.IsTotalEstimated;
                item.TotalBytes = total;
                item.DownloadedBytes = done;
                item.Speed = p.Status == "finished" ? 0 : p.Speed;
                if (p.Fragment is > 0 && p.FragmentCount is > 0)
                {
                    item.Stage = IsTurkish 
                        ? $"{currentStreamType} İndiriliyor ({p.Fragment}/{p.FragmentCount})" 
                        : $"{currentStreamType} Downloading ({p.Fragment}/{p.FragmentCount})";
                }
                else
                {
                    item.Stage = null;
                }
            }
            else if (line.Contains("has already been downloaded", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(line, @"\[download\]\s+(.+?)\s+has already been downloaded");
                if (m.Success)
                {
                    var path = m.Groups[1].Value.Trim().Trim('"', '\'');
                    if (!string.IsNullOrEmpty(path) && !downloadedDestinations.Contains(path, StringComparer.OrdinalIgnoreCase))
                        downloadedDestinations.Add(path);
                    if (File.Exists(path))
                    {
                        try
                        {
                            var fi = new FileInfo(path);
                            acc.AddFinished(fi.Length, fi.FullName);
                            item.DownloadedBytes = Math.Max(item.DownloadedBytes, fi.Length);
                        }
                        catch { }
                    }
                }
            }
            else if (line.StartsWith(TitlePrefix + "|", StringComparison.Ordinal))
            {
                var title = FileNames.Sanitize(line[(TitlePrefix.Length + 1)..].Trim());
                if (string.IsNullOrEmpty(item.SavePath))
                {
                    bool isGenericNew = FileNames.IsGenericVideoName(title);
                    bool hasCleanCurrent = !FileNames.IsGenericVideoName(item.FileName);
                    if (!isGenericNew)
                    {
                        item.FileName = title;
                    }
                    else if (!hasCleanCurrent && !FileNames.IsGenericVideoName(item.Request.FileName))
                    {
                        item.FileName = FileNames.Sanitize(item.Request.FileName);
                    }
                }
            }
            else if (line.StartsWith(FilePrefix + "|", StringComparison.Ordinal))
            {
                var fp = line[(FilePrefix.Length + 1)..].Trim();
                if (!string.IsNullOrEmpty(fp) && fp != "NA") finalPath = fp;
            }
            else if (line.StartsWith("[Merger] Merging formats into \"", StringComparison.Ordinal))
            {
                item.Stage = IsTurkish ? "Dosyalar Birleştiriliyor (FFmpeg)..." : "Merging Streams (FFmpeg)...";
                item.Speed = 0;
                var fp = line.Substring("[Merger] Merging formats into \"".Length).TrimEnd('"').Trim();
                if (!string.IsNullOrEmpty(fp))
                {
                    mergerTarget = fp;
                    finalPath = fp;
                }
            }
            else if (line.StartsWith("[download] Destination: ", StringComparison.Ordinal))
            {
                item.Speed = 0;
                item.Stage = IsTurkish ? "İndirme Başlatılıyor..." : "Starting Download...";
                var fp = line.Substring("[download] Destination: ".Length).Trim();
                if (!string.IsNullOrEmpty(fp))
                {
                    if (!downloadedDestinations.Contains(fp, StringComparer.OrdinalIgnoreCase))
                        downloadedDestinations.Add(fp);
                    if (string.IsNullOrEmpty(finalPath)) finalPath = fp;
                }
            }
            else if (line.StartsWith("[Merger]", StringComparison.Ordinal) || line.StartsWith("[VideoConvertor]", StringComparison.Ordinal)
                     || line.StartsWith("[ExtractAudio]", StringComparison.Ordinal) || line.StartsWith("[FixupM3u8]", StringComparison.Ordinal))
            {
                item.Stage = IsTurkish ? "Dosyalar Birleştiriliyor (FFmpeg)..." : "Merging Streams (FFmpeg)...";
                item.Speed = 0;
            }
            else if (line.Contains("Writing video subtitles") || line.Contains("Downloading subtitles"))
            {
                item.Speed = 0;
                item.Stage = IsTurkish ? "Altyazılar İndiriliyor..." : "Downloading Subtitles...";
            }
            else if (line.Contains("Extracting URL:") || line.Contains("[youtube]") || line.Contains("[twitter]") || line.Contains("[tiktok]") || line.Contains("[generic]"))
            {
                if (item.DownloadedBytes == 0 && item.Stage == null) item.Stage = IsTurkish ? "Akışlar Çözülüyor..." : "Resolving Streams...";
            }
            else if (line.Contains("Downloading webpage") || line.Contains("Downloading player") || line.Contains("Downloading API") || line.Contains("Downloading JSON"))
            {
                if (item.DownloadedBytes == 0) item.Stage = IsTurkish ? "Formatlar Alınıyor..." : "Fetching Formats...";
            }
            else if (line.Contains("Downloading 1 format(s):", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("aud", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("turkish", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("original", StringComparison.OrdinalIgnoreCase))
                {
                    currentStreamType = IsTurkish ? "Ses Akışı" : "Audio Stream";
                }
                else
                {
                    currentStreamType = IsTurkish ? "Görüntü Akışı" : "Video Stream";
                }
                item.Stage = IsTurkish ? $"{currentStreamType} Başlatılıyor..." : $"{currentStreamType} Starting...";
            }
            else if (line.Contains("Retrying fragment", StringComparison.OrdinalIgnoreCase) || line.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
            {
                item.Speed = 0;
                item.Stage = IsTurkish ? "Sunucu yoğunluğu, parça yeniden deneniyor..." : "Server busy, retrying chunk...";
            }
            else if (line.Contains("Downloading 1 format") || line.Contains("Downloading m3u8"))
            {
                if (item.DownloadedBytes == 0) item.Stage = IsTurkish ? "Bağlantı Kuruluyor..." : "Connecting...";
            }
            else if (line.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                lastError = line[6..].Trim();
            }
            else
            {
                lastError ??= line.Trim();
            }
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lastError = e.Data.Trim();
            if (e.Data.Contains("Retrying fragment", StringComparison.OrdinalIgnoreCase) || e.Data.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
                OnLine(e.Data);
        };

        if (!proc.Start()) throw new FatalDownloadException("Could not start yt-dlp");
        try { proc.StandardInput.Close(); } catch { }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } });
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        proc.WaitForExit();     // flush the async readers
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        if (proc.ExitCode != 0)
            throw new FatalDownloadException("yt-dlp failed" + (lastError != null ? ": " + Trim(lastError) : $" (exit code {proc.ExitCode})"));

        if (!opt.SubtitlesOnly && !opt.AudioOnly && _ffmpeg() is { } muxFfmpeg && File.Exists(muxFfmpeg))
        {
            TryMergeLeftoverStreamsWithFfmpeg(muxFfmpeg, outDir, item.WorkDir, downloadedDestinations, mergerTarget, startUtc, ref finalPath, item);
        }

        if ((finalPath == null || !File.Exists(finalPath)) && Directory.Exists(outDir))
        {
            var candidates = Directory.GetFiles(outDir)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".mcdm-part", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(fi => fi.LastWriteTimeUtc >= startUtc)
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();
            if (candidates.Count > 0)
            {
                if (opt.SubtitlesOnly)
                {
                    var subFile = candidates.FirstOrDefault(f => f.Extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                                                                 f.Extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase));
                    if (subFile != null) finalPath = subFile.FullName;
                }
                else
                {
                    var mediaFile = candidates.FirstOrDefault(f => !f.Extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) &&
                                                                   !f.Extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase));
                    if (mediaFile != null) finalPath = mediaFile.FullName;
                }
            }
        }

        if (finalPath == null || !File.Exists(finalPath))
        {
            if (opt.SubtitlesOnly)
                throw new FatalDownloadException(IsTurkish ? "Altyazı akışı bulunamadı veya indirilemedi." : "Subtitle stream not found or could not be downloaded.");
            throw new FatalDownloadException(IsTurkish ? "yt-dlp tamamlandı ancak indirilen dosya bulunamadı." : "yt-dlp completed but downloaded file was not found.");
        }

        var full = Path.GetFullPath(finalPath);
        var root = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new FatalDownloadException("yt-dlp reported a file outside the download folder");

        var currentStem = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);

        if (FileNames.IsGenericVideoName(currentStem))
        {
            string desiredStem = !string.IsNullOrWhiteSpace(item.Request.FileName) && !FileNames.IsGenericVideoName(item.Request.FileName)
                ? Path.GetFileNameWithoutExtension(item.Request.FileName)
                : (!string.IsNullOrWhiteSpace(item.FileName) && !FileNames.IsGenericVideoName(item.FileName)
                    ? Path.GetFileNameWithoutExtension(item.FileName)
                    : (IsTurkish ? "Video" : "Video"));

            desiredStem = FileNames.Sanitize(desiredStem);
            var newPath = FileNames.Unique(outDir, desiredStem + ext);
            if (!string.Equals(full, newPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Move(full, newPath, overwrite: false);

                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        var oldStem = currentStem;
                        var newStem = Path.GetFileNameWithoutExtension(newPath);
                        foreach (var companion in Directory.EnumerateFiles(dir, $"{oldStem}.*"))
                        {
                            if (string.Equals(companion, full, StringComparison.OrdinalIgnoreCase)) continue;
                            var cExt = companion.Substring(Path.Combine(dir, oldStem).Length);
                            var newCompanion = Path.Combine(dir, newStem + cExt);
                            if (!File.Exists(newCompanion))
                            {
                                File.Move(companion, newCompanion);
                            }
                        }
                    }

                    full = newPath;
                }
                catch { }
            }
        }

        item.SavePath = full;
        item.FileName = Path.GetFileName(full);
        long size = new FileInfo(full).Length;
        item.TotalBytes = size; item.DownloadedBytes = size;
        item.Stage = null; item.Speed = 0; item.ActiveConnections = 0;
    }

    public static string ConvertVttToSrt(string vtt)
    {
        var lines = vtt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        int counter = 1;
        bool inCue = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                if (inCue)
                {
                    sb.AppendLine();
                    inCue = false;
                }
                continue;
            }

            if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(line, @"^(?:(\d{2}):)?(\d{2}:\d{2})\.(\d{3})\s*-->\s*(?:(\d{2}):)?(\d{2}:\d{2})\.(\d{3})");
            if (match.Success)
            {
                sb.AppendLine((counter++).ToString());
                string sH = match.Groups[1].Success ? match.Groups[1].Value : "00";
                string sMs = match.Groups[3].Value;
                string eH = match.Groups[4].Success ? match.Groups[4].Value : "00";
                string eMs = match.Groups[6].Value;

                sb.AppendLine($"{sH}:{match.Groups[2].Value},{sMs} --> {eH}:{match.Groups[5].Value},{eMs}");
                inCue = true;
                continue;
            }

            if (inCue)
            {
                var clean = Regex.Replace(line, @"[\u200B\u200C\u200D\u200E\u200F\uFEFF\u00AD]", "");
                clean = Regex.Replace(clean, @"<\/?(b|font|span|u)[^>]*>", "", RegexOptions.IgnoreCase);
                clean = Regex.Replace(clean, @"<[^>]+>", "");
                clean = Regex.Replace(clean, @"[ \t]+", " ").Trim();
                sb.AppendLine(clean);
            }
        }

        return sb.ToString().Trim() + "\r\n";
    }

    public static string ConvertXmlTimedTextToSrt(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return "";
        var sb = new StringBuilder();
        int counter = 1;

        static string FormatSrtTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        // Format 1: <text start="1.23" dur="4.56">...</text> (YouTube srv1/transcript XML)
        var textMatches = Regex.Matches(xml, @"<text\b([^>]*)>([\s\S]*?)<\/text>", RegexOptions.IgnoreCase);
        if (textMatches.Count > 0)
        {
            foreach (Match m in textMatches)
            {
                string attrs = m.Groups[1].Value;
                string body = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
                body = Regex.Replace(body, @"<[^>]+>", "").Trim();
                if (string.IsNullOrEmpty(body)) continue;

                var startM = Regex.Match(attrs, @"start=""([\d.]+)""");
                var durM = Regex.Match(attrs, @"dur=""([\d.]+)""");
                if (!startM.Success || !double.TryParse(startM.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double startSec))
                    continue;
                double durSec = durM.Success && double.TryParse(durM.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 3.0;

                sb.AppendLine((counter++).ToString());
                sb.AppendLine($"{FormatSrtTime(startSec)} --> {FormatSrtTime(startSec + durSec)}");
                sb.AppendLine(body);
                sb.AppendLine();
            }
            if (counter > 1) return sb.ToString().Trim() + "\r\n";
        }

        // Format 2: <p t="1230" d="4560">...</p> (YouTube srv3 XML in milliseconds)
        var pMatches = Regex.Matches(xml, @"<p\b([^>]*)>([\s\S]*?)<\/p>", RegexOptions.IgnoreCase);
        if (pMatches.Count > 0)
        {
            foreach (Match m in pMatches)
            {
                string attrs = m.Groups[1].Value;
                string body = System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "")).Trim();
                if (string.IsNullOrEmpty(body)) continue;

                var tM = Regex.Match(attrs, @"\bt=""(\d+)""");
                var dM = Regex.Match(attrs, @"\bd=""(\d+)""");
                if (!tM.Success || !long.TryParse(tM.Groups[1].Value, out long startMs))
                    continue;
                long durMs = dM.Success && long.TryParse(dM.Groups[1].Value, out long dm) ? dm : 3000;

                sb.AppendLine((counter++).ToString());
                sb.AppendLine($"{FormatSrtTime(startMs / 1000.0)} --> {FormatSrtTime((startMs + durMs) / 1000.0)}");
                sb.AppendLine(body);
                sb.AppendLine();
            }
            if (counter > 1) return sb.ToString().Trim() + "\r\n";
        }

        return "";
    }

    public static void CleanAndNormalizeSubtitleFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) return;

            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = Encoding.UTF8.GetString(hasBom ? bytes[3..] : bytes);

            text = Regex.Replace(text, @"[\u200B\u200C\u200D\u200E\u200F\uFEFF\u00AD]", "");
            text = Regex.Replace(text, @"<\/?(b|font|span|u)[^>]*>", "", RegexOptions.IgnoreCase);

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanLines = new List<string>();
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"^\d+$") || line.Contains("-->"))
                {
                    cleanLines.Add(line.Trim());
                }
                else
                {
                    cleanLines.Add(Regex.Replace(line, @"[ \t]+", " ").Trim());
                }
            }
            string finalText = string.Join("\r\n", cleanLines).Trim() + "\r\n";
            File.WriteAllText(filePath, finalText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch { }
    }

    public static bool EmbedSubtitleWithFfmpeg(string ffmpeg, string videoPath, string srtPath, string? langCode = "tr")
    {
        string? tempOut = null;
        try
        {
            string ext = Path.GetExtension(videoPath).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mkv" && ext != ".webm") return false;
            tempOut = Path.Combine(Path.GetDirectoryName(videoPath)!, "temp_embed_" + Guid.NewGuid().ToString("N")[..8] + ext);
            string subCodec = ext == ".mkv" ? "srt" : (ext == ".webm" ? "webvtt" : "mov_text");

            var (trTitle, _) = StreamProbeService.GetLanguageInfo(langCode);
            string iso3 = (langCode ?? "tr").ToLowerInvariant() switch
            {
                "tr" or "tur" => "tur",
                "en" or "eng" => "eng",
                "de" or "ger" or "deu" => "deu",
                "fr" or "fre" or "fra" => "fra",
                "es" or "spa" => "spa",
                "it" or "ita" => "ita",
                "ru" or "rus" => "rus",
                "ar" or "ara" => "ara",
                "pt" or "por" => "por",
                _ => "und"
            };

            var psi = new ProcessStartInfo(ffmpeg)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(videoPath);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(srtPath);
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:v:0");
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a?");
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("1:0");
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("copy");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("copy");
            psi.ArgumentList.Add("-c:s");
            psi.ArgumentList.Add(subCodec);
            psi.ArgumentList.Add("-metadata:s:s:0");
            psi.ArgumentList.Add($"language={iso3}");
            if (!string.IsNullOrEmpty(trTitle) && trTitle != "Bilinmeyen")
            {
                psi.ArgumentList.Add("-metadata:s:s:0");
                psi.ArgumentList.Add($"title={trTitle}");
            }
            psi.ArgumentList.Add("-disposition:s:0");
            psi.ArgumentList.Add("default");
            psi.ArgumentList.Add(tempOut);

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                bool exited = proc.WaitForExit(40000);
                if (!exited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                if (proc.ExitCode == 0 && File.Exists(tempOut) && new FileInfo(tempOut).Length > 1024)
                {
                    File.Move(tempOut, videoPath, true);
                    return true;
                }
            }
        }
        catch { }
        finally
        {
            if (tempOut != null) SegmentedDownloader.TryDelete(tempOut);
        }
        return false;
    }

    public static async Task<string?> DownloadSubtitleFileAsync(string subUrl, DownloadRequest req, CancellationToken ct)
    {
        try
        {
            string fetchUrl = subUrl;
            // Ensure YouTube timedtext URLs always return WebVTT instead of raw XML
            if ((fetchUrl.Contains("youtube.com/api/timedtext", StringComparison.OrdinalIgnoreCase) ||
                 fetchUrl.Contains("googlevideo.com/timedtext", StringComparison.OrdinalIgnoreCase)) &&
                !Regex.IsMatch(fetchUrl, @"[?&]fmt=", RegexOptions.IgnoreCase))
            {
                fetchUrl += (fetchUrl.Contains('?') ? "&" : "?") + "fmt=vtt";
            }

            using var http = RequestCookies.CreateClient(req, TimeSpan.FromSeconds(15));

            using var resp = await http.GetAsync(fetchUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var content = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var trimmed = content.TrimStart();

            // Handle HLS segmented WebVTT playlist (.m3u8 containing .vtt segment URLs)
            if (trimmed.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                var segUrls = new List<string>();
                foreach (var rawLine in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith('#')) continue;
                    try
                    {
                        var absUri = new Uri(new Uri(fetchUrl), line).AbsoluteUri;
                        if (!segUrls.Contains(absUri)) segUrls.Add(absUri);
                    }
                    catch { }
                }

                if (segUrls.Count > 0 && segUrls.Count <= 200)
                {
                    var combinedVtt = new StringBuilder("WEBVTT\n\n");
                    foreach (var segUrl in segUrls)
                    {
                        var segResp = await http.GetAsync(segUrl, ct);
                        if (segResp.IsSuccessStatusCode)
                        {
                            var segText = await segResp.Content.ReadAsStringAsync(ct);
                            if (!string.IsNullOrWhiteSpace(segText))
                            {
                                combinedVtt.AppendLine(segText);
                                combinedVtt.AppendLine();
                            }
                        }
                    }
                    return ConvertVttToSrt(combinedVtt.ToString());
                }
                return null;
            }

            if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || fetchUrl.Contains(".vtt", StringComparison.OrdinalIgnoreCase) || fetchUrl.Contains("fmt=vtt", StringComparison.OrdinalIgnoreCase))
                return ConvertVttToSrt(content);

            if (trimmed.StartsWith("<"))
            {
                var xmlSrt = ConvertXmlTimedTextToSrt(content);
                if (!string.IsNullOrWhiteSpace(xmlSrt)) return xmlSrt;
            }

            return content;
        }
        catch
        {
            return null;
        }
    }

    private void TryMergeLeftoverStreamsWithFfmpeg(
        string ffmpeg,
        string outDir,
        string? workDir,
        List<string> downloadedDestinations,
        string? mergerTarget,
        DateTime startUtc,
        ref string? finalPath,
        DownloadItem item)
    {
        try
        {
            // If yt-dlp already created the merged output and it contains both video & audio, just clean up any leftover .f<id> component files
            if (!string.IsNullOrEmpty(mergerTarget) && File.Exists(mergerTarget) && new FileInfo(mergerTarget).Length > 1024
                && HasVideoStream(ffmpeg, mergerTarget) && HasAudioStream(ffmpeg, mergerTarget))
            {
                foreach (var d in downloadedDestinations)
                {
                    string inOut = Path.Combine(outDir, Path.GetFileName(d));
                    if (!string.Equals(inOut, mergerTarget, StringComparison.OrdinalIgnoreCase) && File.Exists(inOut))
                        SegmentedDownloader.TryDelete(inOut);
                    if (!string.Equals(d, mergerTarget, StringComparison.OrdinalIgnoreCase) && File.Exists(d))
                        SegmentedDownloader.TryDelete(d);
                }
                finalPath = mergerTarget;
                return;
            }

            // Collect candidate unmerged stream files from downloadedDestinations, outDir, and workDir
            var candidatePaths = new List<string>();
            void AddCandidate(string? p)
            {
                if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext is ".srt" or ".vtt" or ".ass" or ".ttml" or ".part" or ".ytdl" or ".tmp" or ".mcdm-part" or ".json" or ".jpg" or ".png" or ".webp")
                    return;
                try
                {
                    if (new FileInfo(p).Length <= 512) return;
                }
                catch { return; }
                if (!candidatePaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    candidatePaths.Add(p);
            }

            foreach (var d in downloadedDestinations)
            {
                AddCandidate(Path.Combine(outDir, Path.GetFileName(d)));
                AddCandidate(d);
            }

            if (Directory.Exists(outDir))
            {
                foreach (var f in Directory.GetFiles(outDir))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTimeUtc >= startUtc)
                            AddCandidate(f);
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            {
                foreach (var f in Directory.GetFiles(workDir))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTimeUtc >= startUtc)
                            AddCandidate(f);
                    }
                    catch { }
                }
            }

            if (candidatePaths.Count < 2) return;

            string? videoStreamPath = null;
            string? audioStreamPath = null;

            foreach (var c in candidatePaths)
            {
                bool hasV = HasVideoStream(ffmpeg, c);
                bool hasA = HasAudioStream(ffmpeg, c);
                if (hasV && !hasA && videoStreamPath == null)
                    videoStreamPath = c;
                else if (hasA && !hasV && audioStreamPath == null)
                    audioStreamPath = c;
            }

            // Fallback heuristic if stream probing was inconclusive on one of the streams
            if (videoStreamPath == null || audioStreamPath == null)
            {
                foreach (var c in candidatePaths)
                {
                    string ext = Path.GetExtension(c).ToLowerInvariant();
                    string name = Path.GetFileNameWithoutExtension(c);
                    bool looksLikeAudio = ext is ".m4a" or ".aac" or ".mp3" or ".opus" or ".ogg" or ".wav" or ".flac"
                                          || Regex.IsMatch(name, @"(\.f\d*a\d*|-a\d+|audio|dublaj)", RegexOptions.IgnoreCase);
                    if (looksLikeAudio && audioStreamPath == null && !string.Equals(c, videoStreamPath, StringComparison.OrdinalIgnoreCase))
                        audioStreamPath = c;
                    else if (!looksLikeAudio && videoStreamPath == null && !string.Equals(c, audioStreamPath, StringComparison.OrdinalIgnoreCase))
                        videoStreamPath = c;
                }
            }

            if (videoStreamPath == null || audioStreamPath == null || string.Equals(videoStreamPath, audioStreamPath, StringComparison.OrdinalIgnoreCase))
                return;

            string targetMergedPath;
            if (!string.IsNullOrWhiteSpace(mergerTarget))
            {
                targetMergedPath = Path.IsPathRooted(mergerTarget) ? mergerTarget : Path.Combine(outDir, mergerTarget);
            }
            else
            {
                string rawStem = Path.GetFileNameWithoutExtension(videoStreamPath);
                string cleanStem = Regex.Replace(rawStem, @"\.f[0-9a-zA-Z_\-]+$", "");
                if (string.IsNullOrWhiteSpace(cleanStem)) cleanStem = rawStem;
                targetMergedPath = Path.Combine(outDir, cleanStem + ".mp4");
            }

            item.Stage = IsTurkish ? "Ses ve Görüntü Birleştiriliyor (FFmpeg)..." : "Merging Video & Audio (FFmpeg)...";
            item.Speed = 0;

            if (MergeVideoAndAudioWithFfmpeg(ffmpeg, videoStreamPath, audioStreamPath, targetMergedPath))
            {
                if (!string.Equals(videoStreamPath, targetMergedPath, StringComparison.OrdinalIgnoreCase))
                    SegmentedDownloader.TryDelete(videoStreamPath);
                if (!string.Equals(audioStreamPath, targetMergedPath, StringComparison.OrdinalIgnoreCase))
                    SegmentedDownloader.TryDelete(audioStreamPath);

                finalPath = targetMergedPath;
            }
        }
        catch { }
    }

    public static bool MergeVideoAndAudioWithFfmpeg(string ffmpeg, string videoPath, string audioPath, string outputPath)
    {
        if (!File.Exists(ffmpeg) || !File.Exists(videoPath) || !File.Exists(audioPath))
            return false;

        string? tempOut = null;
        try
        {
            string outDir = Path.GetDirectoryName(outputPath) ?? Path.GetDirectoryName(videoPath)!;
            string ext = Path.GetExtension(outputPath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
            tempOut = Path.Combine(outDir, "temp_mux_" + Guid.NewGuid().ToString("N")[..8] + ext);

            // Tier 1: Fast lossless stream copy + aac_adtstoasc bitstream filter (fixes HLS MPEG-TS raw ADTS AAC -> MP4 container mux errors)
            // Tier 2: Standard lossless stream copy (for non-AAC codecs like Opus/MP3/AC3)
            // Tier 3: Video stream copy + AAC audio transcode fallback (guarantees MP4 compatibility when audio codec is incompatible with MP4)
            string[][] attempts =
            [
                ["-c:v", "copy", "-c:a", "copy", "-bsf:a", "aac_adtstoasc", "-movflags", "+faststart"],
                ["-c:v", "copy", "-c:a", "copy", "-movflags", "+faststart"],
                ["-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]
            ];

            foreach (var codecArgs in attempts)
            {
                SegmentedDownloader.TryDelete(tempOut);
                var psi = new ProcessStartInfo(ffmpeg)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-nostdin");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-loglevel");
                psi.ArgumentList.Add("error");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(videoPath);
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(audioPath);
                psi.ArgumentList.Add("-map");
                psi.ArgumentList.Add("0:v:0");
                psi.ArgumentList.Add("-map");
                psi.ArgumentList.Add("1:a:0");
                foreach (var a in codecArgs)
                    psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(tempOut);

                using var proc = Process.Start(psi);
                if (proc == null) continue;
                if (!proc.WaitForExit(180000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    continue;
                }

                if (proc.ExitCode == 0 && File.Exists(tempOut) && new FileInfo(tempOut).Length > 1024)
                {
                    File.Move(tempOut, outputPath, overwrite: true);
                    return true;
                }
            }
        }
        catch { }
        finally
        {
            if (tempOut != null) SegmentedDownloader.TryDelete(tempOut);
        }
        return false;
    }

    public static bool HasAudioStream(string ffmpeg, string mediaPath)
        => ProbeMediaStreamType(ffmpeg, mediaPath, "Audio:");

    public static bool HasVideoStream(string ffmpeg, string mediaPath)
        => ProbeMediaStreamType(ffmpeg, mediaPath, "Video:");

    private static bool ProbeMediaStreamType(string ffmpeg, string mediaPath, string marker)
    {
        if (!File.Exists(ffmpeg) || !File.Exists(mediaPath)) return false;
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(mediaPath);

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);
            return stderr.Contains(marker, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> DownloadExternalAudioStreamAsync(
        DownloadItem item,
        ToolLauncher tool,
        string? cookieFile,
        (string Name, string Path)? jsRuntime,
        CancellationToken ct)
    {
        string? audioUrl = item.Request.AudioStreamUrl;
        if (string.IsNullOrWhiteSpace(audioUrl)) return null;

        try
        {
            string tempDir = !string.IsNullOrEmpty(item.WorkDir) && Directory.Exists(item.WorkDir)
                ? item.WorkDir
                : Path.GetTempPath();
            string outTemplate = Path.Combine(tempDir, "ext_audio_" + Guid.NewGuid().ToString("N")[..8] + ".%(ext)s");

            var psi = new ProcessStartInfo(tool.FileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var l in tool.Lead) psi.ArgumentList.Add(l);
            var audioOptions = new VideoOptions { CustomFormat = "ba/b", SpeedLimitBytes = _options().SpeedLimitBytes };
            var audioArguments = BuildArguments(audioUrl, audioOptions, tempDir, tempDir,
                         cookieFile, _ffmpeg(), item.Request.UserAgent, item.Request.Referer,
                         Path.GetFileName(outTemplate), jsRuntime);
            // Preserve certificate validation in the separate-audio path.
            audioArguments.Remove("--no-check-certificates");
            foreach (var arg in audioArguments)
                psi.ArgumentList.Add(arg);

            long videoBytes = item.DownloadedBytes;
            item.Speed = 0;
            item.TotalBytes = -1;
            var acc = new ProgressAccumulator();
            void OnAudioLine(string? line)
            {
                if (line == null) return;
                if (ParseProgress(line) is { } p)
                {
                    var (done, total) = acc.Apply(p);
                    item.IsTotalEstimated = p.IsTotalEstimated;
                    item.TotalBytes = total > 0 ? videoBytes + total : -1;
                    item.DownloadedBytes = videoBytes + done;
                    item.Speed = p.Status == "finished" ? 0 : p.Speed;
                    item.Stage = p.Fragment is > 0 && p.FragmentCount is > 0
                        ? (IsTurkish ? $"Ses Akışı İndiriliyor ({p.Fragment}/{p.FragmentCount})" : $"Audio Stream Downloading ({p.Fragment}/{p.FragmentCount})")
                        : (IsTurkish ? "Ses Akışı İndiriliyor..." : "Downloading Audio Stream...");
                }
                else if (line.Contains("Retrying fragment", StringComparison.OrdinalIgnoreCase) || line.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
                {
                    item.Speed = 0;
                    item.Stage = IsTurkish ? "Ses parçası yeniden deneniyor..." : "Retrying audio fragment...";
                }
            }
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => OnAudioLine(e.Data);
            proc.ErrorDataReceived += (_, e) => OnAudioLine(e.Data);
            if (!proc.Start()) return null;
            try { proc.StandardInput.Close(); } catch { }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var registration = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } });
            await proc.WaitForExitAsync(ct);
            proc.WaitForExit();
            ct.ThrowIfCancellationRequested();
            item.Speed = 0;
            if (proc.ExitCode != 0) return null;

            var prefix = Path.GetFileNameWithoutExtension(outTemplate).Replace(".%(ext)", "");
            return Directory.GetFiles(tempDir, prefix + "*").FirstOrDefault(f =>
                !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
