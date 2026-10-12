using System.Diagnostics;
using System.Globalization;
using System.Text;

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
    public bool YtDlpAvailable => YtDlpPath != null;
    /// <summary>Where "Update yt-dlp" writes the new binary (always writable, per user).</summary>
    public string UserYtDlpTarget => Path.Combine(UserToolsDirectory, "yt-dlp" + Exe);

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
}

/// <summary>One parsed --progress-template line.</summary>
public sealed record VideoProgress(string Status, long Downloaded, long? Total, double Speed, int? Fragment, int? FragmentCount);

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

    // ───────────────────────── argument construction ─────────────────────────

    public static string FormatSelector(VideoOptions o) =>
        !string.IsNullOrWhiteSpace(o.CustomFormat) ? o.CustomFormat : "bv*+ba/b/best";

    public static string FormatSort(VideoOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.CustomFormat)) return "";
        var parts = new List<string>();
        if (o.MaxHeight > 0) parts.Add("res:" + o.MaxHeight.ToString(CultureInfo.InvariantCulture));
        if (o.Container == "mp4") parts.Add("ext:mp4:m4a");   // prefer streams that can be merged into MP4 without re-encoding
        return string.Join(",", parts);
    }

    /// <summary>Everything after the executable. Public so tests can assert on it.</summary>
    public static List<string> BuildArguments(string url, VideoOptions o, string outputDir, string workDir,
                                              string? cookiesFile, string? ffmpegLocation,
                                              string? userAgent = null, string? referer = null)
    {
        var a = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "--newline",
            "--progress",
            "--no-colors",
            "--encoding", "utf-8",
            "--windows-filenames",
            "--trim-filenames", "150",
            "--no-mtime",
            "--concurrent-fragments", "8",
            "--buffer-size", "1024K",
            "--http-chunk-size", "10M",
            "--throttled-rate", "100K",
            "--no-check-certificates",
            "--socket-timeout", "10",
            "--impersonate", "chrome",
            "-c",                                  // continue partial downloads (resume)
            "--no-part-if-exists-placeholder-never-used".Length > 0 ? "--no-overwrites" : "",
            "-f", FormatSelector(o),
        };
        a.RemoveAll(string.IsNullOrEmpty);
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            a.Add("--user-agent");
            a.Add(userAgent);
        }
        if (!string.IsNullOrWhiteSpace(referer))
        {
            a.Add("--referer");
            a.Add(referer);
        }
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
            a.Add("--merge-output-format"); a.Add(o.Container is "mkv" or "webm" ? o.Container : "mp4");
        }
        if (ffmpegLocation != null) { a.Add("--ffmpeg-location"); a.Add(ffmpegLocation); }
        if (cookiesFile != null) { a.Add("--cookies"); a.Add(cookiesFile); }
        a.Add("-P"); a.Add(outputDir);
        a.Add("-P"); a.Add("temp:" + workDir);
        a.Add("-o"); a.Add("%(title).150B [%(id)s].%(ext)s");
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
        return new VideoProgress(p[1], L(p[2]) ?? 0, L(p[3]) ?? L(p[4]), speed, I(p[6]), I(p[7]));
    }

    /// <summary>Accumulates progress across yt-dlp's separate video/audio streams into one running total.</summary>
    public sealed class ProgressAccumulator
    {
        private long _finished;
        private long _lastStreamDownloaded;
        private long _lastStreamTotal;

        public (long Downloaded, long Total) Apply(VideoProgress p)
        {
            long total = p.Total ?? 0;
            if (total <= 0 && p.Fragment is > 0 && p.FragmentCount is > 0 && p.Downloaded > 0)
                total = p.Downloaded * p.FragmentCount.Value / p.Fragment.Value;   // HLS/DASH: estimate from fragments
            _lastStreamDownloaded = p.Downloaded;
            _lastStreamTotal = Math.Max(total, p.Downloaded);
            var result = (_finished + p.Downloaded, total > 0 ? _finished + _lastStreamTotal : -1);
            if (p.Status == "finished") { _finished += _lastStreamDownloaded; _lastStreamDownloaded = 0; _lastStreamTotal = 0; }
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

    public async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        var tool = _ytDlp() ?? throw new FatalDownloadException("yt-dlp is not installed");
        var baseOpt = _options();
        var opt = new VideoOptions
        {
            Container = !string.IsNullOrWhiteSpace(item.Request.Container) ? item.Request.Container : baseOpt.Container,
            MaxHeight = baseOpt.MaxHeight,
            CustomFormat = item.Request.CustomFormat,
            AudioOnly = item.Request.AudioOnly || item.Request.Container == "mp3"
        };
        string outDir = string.IsNullOrEmpty(item.Directory) ? Directory.GetCurrentDirectory() : item.Directory;
        Directory.CreateDirectory(outDir);
        item.Directory = outDir;
        if (string.IsNullOrEmpty(item.WorkDir)) item.WorkDir = Path.Combine(outDir, ".mcdm-video-" + item.Id);
        Directory.CreateDirectory(item.WorkDir);

        string? cookieFile = null;
        try
        {
            var cookieText = BuildNetscapeCookies(item.Request);
            if (cookieText.Contains('\t'))
            {
                cookieFile = Path.Combine(Path.GetTempPath(), "mcdm-" + Guid.NewGuid().ToString("N") + ".cookies.txt");
                await File.WriteAllTextAsync(cookieFile, cookieText, new UTF8Encoding(false), ct);
            }

            try
            {
                await ExecuteYtDlpOnceAsync(item, opt, outDir, cookieFile, tool, ct);
            }
            catch (FatalDownloadException ex) when (cookieFile != null && !ct.IsCancellationRequested &&
                   (ex.Message.Contains("reload", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase)))
            {
                // Browser cookies triggered YouTube's reload/bot check. Retry cleanly without cookies.
                await ExecuteYtDlpOnceAsync(item, opt, outDir, null, tool, ct);
            }
        }
        finally
        {
            if (cookieFile != null) SegmentedDownloader.TryDelete(cookieFile);
        }
    }

    private async Task ExecuteYtDlpOnceAsync(DownloadItem item, VideoOptions opt, string outDir, string? cookieFile,
                                             ToolLauncher tool, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(tool.FileName)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var l in tool.Lead) psi.ArgumentList.Add(l);
        foreach (var a in BuildArguments(item.Request.Url, opt, outDir, item.WorkDir!, cookieFile, _ffmpeg(), item.Request.UserAgent, item.Request.Referer))
            psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi };
        string? finalPath = null;
        string? lastError = null;
        var acc = new ProgressAccumulator();
        var clock = Stopwatch.StartNew();
        item.Stage = null;
        item.ActiveConnections = 1;

        void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (VideoDownloader.ParseProgress(line) is { } p)
            {
                var (done, total) = acc.Apply(p);
                item.TotalBytes = total;
                item.DownloadedBytes = done;
                item.Speed = p.Speed;
                item.Stage = null;
            }
            else if (line.StartsWith(TitlePrefix + "|", StringComparison.Ordinal))
            {
                var title = FileNames.Sanitize(line[(TitlePrefix.Length + 1)..].Trim());
                if (string.IsNullOrEmpty(item.SavePath)) item.FileName = title;
            }
            else if (line.StartsWith(FilePrefix + "|", StringComparison.Ordinal))
            {
                var fp = line[(FilePrefix.Length + 1)..].Trim();
                if (!string.IsNullOrEmpty(fp) && fp != "NA") finalPath = fp;
            }
            else if (line.StartsWith("[Merger] Merging formats into \"", StringComparison.Ordinal))
            {
                item.Stage = "Merging";
                item.Speed = 0;
                var fp = line.Substring("[Merger] Merging formats into \"".Length).TrimEnd('"').Trim();
                if (!string.IsNullOrEmpty(fp)) finalPath = fp;
            }
            else if (line.StartsWith("[download] Destination: ", StringComparison.Ordinal))
            {
                var fp = line.Substring("[download] Destination: ".Length).Trim();
                if (!string.IsNullOrEmpty(fp) && string.IsNullOrEmpty(finalPath)) finalPath = fp;
            }
            else if (line.StartsWith("[Merger]", StringComparison.Ordinal) || line.StartsWith("[VideoConvertor]", StringComparison.Ordinal)
                     || line.StartsWith("[ExtractAudio]", StringComparison.Ordinal) || line.StartsWith("[FixupM3u8]", StringComparison.Ordinal))
            {
                item.Stage = "Merging";
                item.Speed = 0;
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
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lastError = e.Data.Trim(); };

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

        if ((finalPath == null || !File.Exists(finalPath)) && Directory.Exists(outDir))
        {
            var candidates = Directory.GetFiles(outDir)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".mcdm-part", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();
            if (candidates.Count > 0) finalPath = candidates[0].FullName;
        }

        if (finalPath == null || !File.Exists(finalPath))
            throw new FatalDownloadException("yt-dlp finished but reported no output file");

        var full = Path.GetFullPath(finalPath);
        var root = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new FatalDownloadException("yt-dlp reported a file outside the download folder");

        item.SavePath = full;
        item.FileName = Path.GetFileName(full);
        long size = new FileInfo(full).Length;
        item.TotalBytes = size; item.DownloadedBytes = size;
        item.Stage = null; item.Speed = 0; item.ActiveConnections = 0;
        try { Directory.Delete(item.WorkDir!, recursive: true); } catch { }
        item.WorkDir = null;
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
