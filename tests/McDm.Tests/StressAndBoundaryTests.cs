using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McDm.Core;

namespace McDm.Tests;

public static class StressAndBoundaryTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check, Func<string> tmp)
    {
        // ── 1. FileNames Security & Windows Sanitization ───────────────────
        await test("stress: FileNames.Sanitize handles Windows reserved devices, null bytes, and traversal", () =>
        {
            check(FileNames.Sanitize("CON.txt") == "_CON.txt", "CON reserved name");
            check(FileNames.Sanitize("prn.pdf") == "_prn.pdf", "PRN reserved name");
            check(FileNames.Sanitize("aux.zip") == "_aux.zip", "AUX reserved name");
            check(FileNames.Sanitize("NUL") == "_NUL", "NUL reserved name");
            check(FileNames.Sanitize("com1.mp4") == "_com1.mp4", "COM1 reserved name");
            check(FileNames.Sanitize("LPT9.bin") == "_LPT9.bin", "LPT9 reserved name");

            // Traversal & invalid characters
            check(FileNames.Sanitize("../../etc/passwd") == "passwd", "traversal stripped");
            check(FileNames.Sanitize("..\\..\\Windows\\System32\\cmd.exe") == "cmd.exe", "windows traversal stripped");
            check(FileNames.Sanitize("report:final*v1?.pdf") == "report_final_v1_.pdf", "invalid chars replaced");
            check(FileNames.Sanitize("bad|name<test>here.mp4") == "bad_name_test_here.mp4", "pipes and brackets replaced");
            check(FileNames.Sanitize("") == "download", "empty becomes download");
            check(FileNames.Sanitize("   ") == "download", "whitespace becomes download");
            check(FileNames.Sanitize("...   ") == "download", "dots and spaces become download");

            // Extreme length truncation (>200 chars stem)
            var longStem = new string('A', 250);
            var longResult = FileNames.Sanitize(longStem + ".mkv");
            check(longResult.EndsWith(".mkv"), "extension preserved");
            check(longResult.Length <= 204, "total length capped: " + longResult.Length);

            return Task.CompletedTask;
        });

        // ── 2. FileNames.Unique Collision Resolution ───────────────────────
        await test("stress: FileNames.Unique collision resolution with existing, in-progress, and part files", () =>
        {
            var dir = tmp();
            Directory.CreateDirectory(dir);

            // 1. Initial file
            var p0 = FileNames.Unique(dir, "archive.zip");
            check(Path.GetFileName(p0) == "archive.zip", "first candidate free");
            File.WriteAllText(p0, "done");

            // 2. Collision with finished file -> archive (1).zip
            var p1 = FileNames.Unique(dir, "archive.zip");
            check(Path.GetFileName(p1) == "archive (1).zip", "first collision increment");
            File.WriteAllText(p1, "done");

            // 3. Collision with .mcdm-part file
            var p2 = FileNames.Unique(dir, "archive.zip");
            check(Path.GetFileName(p2) == "archive (2).zip", "second collision increment");
            // Simulate that archive (2).zip.mcdm-part is downloading
            File.WriteAllText(Path.Combine(dir, "archive (2).zip.mcdm-part"), "downloading");

            var p3 = FileNames.Unique(dir, "archive.zip");
            check(Path.GetFileName(p3) == "archive (3).zip", "collision avoided with .mcdm-part file");

            // 4. Collision with .mcdm metadata file
            File.WriteAllText(Path.Combine(dir, "archive (3).zip.mcdm"), "meta");
            var p4 = FileNames.Unique(dir, "archive.zip");
            check(Path.GetFileName(p4) == "archive (4).zip", "collision avoided with .mcdm metadata file");

            return Task.CompletedTask;
        });

        // ── 3. CategoryHelper Multi-type Mapping ──────────────────────────
        await test("stress: CategoryHelper correctly classifies 30+ extension types across all categories", () =>
        {
            // Video
            foreach (var ext in new[] { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".m4v", ".ts", ".m4s" })
                check(CategoryHelper.GetCategorySubfolder($"video{ext}", null, DownloadKind.File) == "Videos", $"video ext {ext}");

            // Music
            foreach (var ext in new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".opus" })
                check(CategoryHelper.GetCategorySubfolder($"audio{ext}", null, DownloadKind.File) == "Music", $"audio ext {ext}");

            // Images
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg" })
                check(CategoryHelper.GetCategorySubfolder($"pic{ext}", null, DownloadKind.File) == "Images", $"image ext {ext}");

            // Programs
            foreach (var ext in new[] { ".exe", ".msi", ".bat", ".cmd", ".iso", ".apk" })
                check(CategoryHelper.GetCategorySubfolder($"app{ext}", null, DownloadKind.File) == "Programs", $"app ext {ext}");

            // Documents
            foreach (var ext in new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv", ".epub" })
                check(CategoryHelper.GetCategorySubfolder($"doc{ext}", null, DownloadKind.File) == "Documents", $"doc ext {ext}");

            // Compressed
            foreach (var ext in new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".xz" })
                check(CategoryHelper.GetCategorySubfolder($"archive{ext}", null, DownloadKind.File) == "Compressed", $"archive ext {ext}");

            // Kind overrides
            check(CategoryHelper.GetCategorySubfolder("arbitrary_name", null, DownloadKind.Video) == "Videos", "kind video overrides filename");
            check(CategoryHelper.GetCategorySubfolder("file.iso", null, DownloadKind.Torrent) == "Torrents", "kind torrent overrides filename");

            // URL fallback when filename is empty or missing extension
            check(CategoryHelper.GetCategorySubfolder(null, "https://cdn.site.com/media/clip.mp4?token=123", DownloadKind.File) == "Videos", "URL fallback for video");
            check(CategoryHelper.GetCategorySubfolder("", "https://files.org/archive.7z", DownloadKind.File) == "Compressed", "URL fallback for archive");

            return Task.CompletedTask;
        });

        // ── 4. CategoryHelper ResolveDestinationDirectory ─────────────────
        await test("stress: CategoryHelper.ResolveDestinationDirectory handles toggles and auto-creation", () =>
        {
            var baseDir = tmp();
            Directory.CreateDirectory(baseDir);

            // autoCategorize = false -> returns base directory exactly
            var uncategorized = CategoryHelper.ResolveDestinationDirectory(baseDir, "song.mp3", null, DownloadKind.File, autoCategorize: false);
            check(uncategorized == baseDir, "uncategorized returns base directory");

            // autoCategorize = true -> returns subfolder and creates directory
            var musicDir = CategoryHelper.ResolveDestinationDirectory(baseDir, "song.mp3", null, DownloadKind.File, autoCategorize: true);
            check(musicDir == Path.Combine(baseDir, "Music"), "categorized returns Music subfolder");
            check(Directory.Exists(musicDir), "Music directory was automatically created on disk");

            var videoDir = CategoryHelper.ResolveDestinationDirectory(baseDir, null, null, DownloadKind.Video, autoCategorize: true);
            check(videoDir == Path.Combine(baseDir, "Videos"), "categorized returns Videos subfolder");
            check(Directory.Exists(videoDir), "Videos directory was automatically created on disk");

            return Task.CompletedTask;
        });

        // ── 5. UpdateChecker Version Comparison ───────────────────────────
        await test("stress: UpdateChecker.IsNewer semantic comparison logic", () =>
        {
            check(UpdateChecker.IsNewer("3.4.8", "3.4.7"), "patch increment is newer");
            check(UpdateChecker.IsNewer("3.5.0", "3.4.7"), "minor increment is newer");
            check(UpdateChecker.IsNewer("4.0.0", "3.4.7"), "major increment is newer");
            check(!UpdateChecker.IsNewer("3.4.7", "3.4.7"), "same version is not newer");
            check(!UpdateChecker.IsNewer("3.4.6", "3.4.7"), "older version is not newer");
            check(!UpdateChecker.IsNewer("2.9.9", "3.4.7"), "older major is not newer");
            check(!UpdateChecker.IsNewer("", "3.4.7"), "empty latest is not newer");
            check(!UpdateChecker.IsNewer("   ", "3.4.7"), "whitespace latest is not newer");
            check(UpdateChecker.IsNewer("3.4.7.1", "3.4.7.0"), "build increment is newer");

            return Task.CompletedTask;
        });

        // ── 6. TokenStore Cryptographic Generation and Constant-Time Match ─
        await test("stress: TokenStore security, file persistence, and timing attack resistance", () =>
        {
            var tokenFile = Path.Combine(tmp(), "bridge.token");
            var store = new TokenStore(tokenFile);

            check(store.Token.Length == 64, "token must be 64 characters (256 bits hex)");
            check(store.Token.All(Uri.IsHexDigit), "token must be all hex digits");
            check(File.Exists(tokenFile), "token was written to disk");
            check(File.ReadAllText(tokenFile).Trim() == store.Token, "file matches in-memory token");

            // Matching
            check(store.Matches(store.Token), "exact token matches");
            check(!store.Matches(null), "null candidate rejected");
            check(!store.Matches(""), "empty candidate rejected");
            char flippedLast = store.Token[63] == '0' ? '1' : '0';
            check(!store.Matches(store.Token[..63] + flippedLast), "single bit difference rejected");
            check(!store.Matches(store.Token[..32]), "truncated candidate rejected");
            check(!store.Matches(store.Token + "extra"), "longer candidate rejected");

            // Reload from existing file
            var reloaded = new TokenStore(tokenFile);
            check(reloaded.Token == store.Token, "reloaded store preserves existing token");

            // Invalidation on Regenerate
            var oldToken = store.Token;
            var newToken = store.Regenerate();
            check(newToken != oldToken, "regenerated token differs from previous");
            check(newToken.Length == 64, "new token is 64 hex characters");
            check(!store.Matches(oldToken), "old token is immediately invalid");
            check(store.Matches(newToken), "new token is valid");

            return Task.CompletedTask;
        });

        // ── 7. SpeedLimiter High Concurrency Stress Test ──────────────────
        await test("stress: SpeedLimiter concurrent throttling across 8 parallel tasks", async () =>
        {
            // 2 MB/s limiter
            var limiter = new SpeedLimiter(2 * 1024 * 1024);
            long totalConsumed = 0;
            var sw = Stopwatch.StartNew();

            // 8 concurrent workers each requesting 256 KB = 2 MB total
            var tasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                await limiter.ThrottleAsync(256 * 1024);
                Interlocked.Add(ref totalConsumed, 256 * 1024);
            }).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            check(totalConsumed == 2 * 1024 * 1024, "exact total bytes accounted for");
            check(sw.Elapsed.TotalSeconds < 3.0, "burst tokens served without deadlocks: " + sw.Elapsed);

            // Test dynamic change to unlimited (0)
            limiter.BytesPerSecond = 0;
            var unlimSw = Stopwatch.StartNew();
            await limiter.ThrottleAsync(10 * 1024 * 1024); // 10 MB instantaneous
            unlimSw.Stop();
            check(unlimSw.ElapsedMilliseconds < 50, "unlimited throttler does not block: " + unlimSw.ElapsedMilliseconds + "ms");

            return;
        });

        // ── 8. WebVTT to SRT Conversion Edge Cases ─────────────────────────
        await test("stress: VideoDownloader.ConvertVttToSrt complex cues, voice tags, and missing hours", () =>
        {
            var rawVtt = @"WEBVTT - Sample Presentation File
NOTE This is a commentary note that must be discarded
STYLE
::cue { color: #fff; }
REGION
id:top

00:00.500 --> 00:03.200
<v Narrator>Hello <b>world</b> and welcome!</v>

00:03.500 --> 00:07.800 position:10%,line:90% align:left
Here is a <c.yellow>colored subtitle</c> on multiple
lines of text!

01:23:45.678 --> 01:23:50.123
Late timestamp with full 3-part hour specification.";

            var srt = VideoDownloader.ConvertVttToSrt(rawVtt);

            // Headers stripped
            check(!srt.Contains("WEBVTT"), "WEBVTT header stripped");
            check(!srt.Contains("NOTE"), "NOTE block stripped");
            check(!srt.Contains("STYLE"), "STYLE block stripped");
            check(!srt.Contains("REGION"), "REGION block stripped");

            // Timing dots converted to commas
            check(srt.Contains("00:00:00,500 --> 00:00:03,200"), "cue 1 converted with hours and commas");
            check(srt.Contains("00:00:03,500 --> 00:00:07,800"), "cue 2 converted and cue settings stripped");
            check(srt.Contains("01:23:45,678 --> 01:23:50,123"), "cue 3 preserved full hours with comma");

            // Tags stripped
            check(!srt.Contains("<v Narrator>"), "voice tags stripped");
            check(!srt.Contains("<b>"), "bold tags stripped");
            check(!srt.Contains("<c.yellow>"), "class tags stripped");
            check(srt.Contains("Hello world and welcome!"), "clean text for cue 1");
            check(srt.Contains("lines of text!"), "multi-line text preserved");

            // Sequential numbering
            check(srt.StartsWith("1\r\n") || srt.StartsWith("1\n"), "first cue numbered 1");
            check(srt.Contains("\r\n2\r\n") || srt.Contains("\n2\n"), "second cue numbered 2");
            check(srt.Contains("\r\n3\r\n") || srt.Contains("\n3\n"), "third cue numbered 3");

            return Task.CompletedTask;
        });

        // ── 9. VideoDownloader Argument Builder Resilience ─────────────────
        await test("stress: VideoDownloader.BuildArguments complete flags validation", () =>
        {
            var opt = new VideoOptions
            {
                Container = "mp4",
                MaxHeight = 1080,
                SpeedLimitBytes = 5 * 1024 * 1024,
                WriteSubtitles = true,
                SubtitleLangs = "tr,en",
                AudioOnly = false,
            };

            var args = VideoDownloader.BuildArguments(
                "https://video.host.com/watch?v=abc",
                opt,
                @"C:\Downloads",
                @"C:\Downloads\.temp",
                @"C:\temp\cookies.txt",
                @"C:\Tools\ffmpeg.exe",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                "https://video.host.com/",
                "custom_video.mp4"
            );

            // Essential flags
            check(args.Contains("--limit-rate") && args[args.IndexOf("--limit-rate") + 1] == (5 * 1024 * 1024).ToString(), "--limit-rate flag present with exact value");
            check(args.Contains("--socket-timeout") && args[args.IndexOf("--socket-timeout") + 1] == "8", "--socket-timeout 8 flag present");
            check(args.Contains("--extractor-retries") && args[args.IndexOf("--extractor-retries") + 1] == "10", "--extractor-retries 10 flag present");
            check(args.Contains("--fragment-retries") && args[args.IndexOf("--fragment-retries") + 1] == "30", "--fragment-retries 30 flag present");
            check(args.Contains("--retry-sleep") && args[args.IndexOf("--retry-sleep") + 1] == "fragment:0.5", "--retry-sleep fragment:0.5 flag present");
            check(args.Contains("-c"), "-c continue flag present");

            // UserAgent & Referer
            check(args.Contains("--user-agent"), "--user-agent flag present");
            check(args[args.IndexOf("--user-agent") + 1].Contains("Mozilla/5.0"), "user-agent value preserved");
            check(args.Contains("--referer"), "--referer flag present");
            check(args[args.IndexOf("--referer") + 1] == "https://video.host.com/", "referer value preserved");

            // Subtitles
            check(args.Contains("--write-subs"), "--write-subs present");
            check(args.Contains("--sub-langs") && args[args.IndexOf("--sub-langs") + 1] == "tr,en", "--sub-langs present with tr,en");

            // Audio-only mode
            var audioOpt = new VideoOptions { AudioOnly = true, Container = "mp3" };
            var audioArgs = VideoDownloader.BuildArguments("https://music.com/track", audioOpt, @"C:\Music", @"C:\Music\.temp", null, @"C:\Tools\ffmpeg.exe");
            check(audioArgs.Contains("-x"), "audio-only has -x extract flag");
            check(audioArgs.Contains("--audio-format") && audioArgs[audioArgs.IndexOf("--audio-format") + 1] == "mp3", "audio-format is mp3");

            return Task.CompletedTask;
        });

        // ── 10. VideoDownloader Progress Parser Accuracy ───────────────────
        await test("stress: VideoDownloader.ParseProgress decimal, speeds, and ETA extraction", () =>
        {
            var line1 = "MCDM_PROGRESS|downloading|12345678|27000000|NA|3500000|1|10";
            var p1 = VideoDownloader.ParseProgress(line1);
            check(p1 != null, "p1 parsed");
            check(p1!.Status == "downloading", "status downloading");
            check(p1.Downloaded == 12345678, "downloaded bytes");
            check(p1.Total == 27000000, "total bytes");
            check(p1.Speed == 3500000, "speed: " + p1.Speed);
            check(p1.Fragment == 1 && p1.FragmentCount == 10, "fragment index and count");

            // Accumulator calculation
            var acc = new VideoDownloader.ProgressAccumulator();
            var (d1, t1) = acc.Apply(p1);
            check(d1 == 12345678 && t1 == 27000000, "accumulator tracks first stream");

            // Finished status resets for second stream (e.g. audio after video)
            var pFin = new VideoProgress("finished", 27000000, 27000000, 0, null, null);
            acc.Apply(pFin);

            var pAudio = new VideoProgress("downloading", 1000000, 3000000, 500000, null, null);
            var (dTotal, tTotal) = acc.Apply(pAudio);
            check(dTotal == 28000000, "accumulated total across streams: " + dTotal);
            check(tTotal == 30000000, "accumulated combined total size: " + tTotal);

            // Invalid / random lines return null safely
            check(VideoDownloader.ParseProgress("[download] Destination: clip.mp4") == null, "random console output is null");
            check(VideoDownloader.ParseProgress("") == null, "empty line is null");

            return Task.CompletedTask;
        });

        // ── 11. DownloadManager Queue Reordering Boundaries ────────────────
        await test("stress: DownloadManager queue reordering boundaries (top, bottom, out of range)", () =>
        {
            var dir = tmp();
            using var dm = new DownloadManager(dir);

            var it1 = dm.Add(new DownloadRequest { Url = "http://test.com/1", Directory = dir });
            var it2 = dm.Add(new DownloadRequest { Url = "http://test.com/2", Directory = dir });
            var it3 = dm.Add(new DownloadRequest { Url = "http://test.com/3", Directory = dir });

            check(dm.Items[0].Id == it3.Id && dm.Items[1].Id == it2.Id && dm.Items[2].Id == it1.Id, "initial UI queue order 3, 2, 1");

            // Move it1 up (already at top of queue) -> should safely return false
            bool movedUp = dm.MoveUp(it1.Id);
            check(!movedUp, "moving top item up returns false");

            // Move it3 down (already at bottom of queue) -> should safely return false
            bool movedDown = dm.MoveDown(it3.Id);
            check(!movedDown, "moving bottom item down returns false");

            // Move it2 up in queue -> returns true
            bool movedIt2Up = dm.MoveUp(it2.Id);
            check(movedIt2Up, "moving it2 up returns true");

            // Move it2 down in queue -> returns true
            bool movedIt2Down = dm.MoveDown(it2.Id);
            check(movedIt2Down, "moving it2 down returns true");

            return Task.CompletedTask;
        });

        // ── 12. DownloadItem State Persistence Integrity ──────────────────
        await test("stress: DownloadItem JSON serialization and property roundtrip", () =>
        {
            var item = new DownloadItem
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Status = DownloadStatus.Paused,
                FileName = "test_archive.tar.gz",
                SavePath = @"C:\Downloads\Compressed\test_archive.tar.gz",
                TotalBytes = 104857600,
                DownloadedBytes = 52428800,
                Directory = @"C:\Downloads\Compressed",
                Request = new DownloadRequest
                {
                    Url = "https://server.com/test_archive.tar.gz",
                    Referer = "https://server.com/",
                    UserAgent = "TestAgent/1.0",
                    FileName = "test_archive.tar.gz",
                    CookieHeader = "session=12345",
                    Kind = DownloadKind.File
                }
            };

            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<DownloadItem>(json);

            check(restored != null, "deserialization succeeded");
            check(restored!.Id == item.Id, "Id restored");
            check(restored.Status == item.Status, "Status restored");
            check(restored.TotalBytes == item.TotalBytes, "TotalBytes restored");
            check(restored.DownloadedBytes == item.DownloadedBytes, "DownloadedBytes restored");
            check(restored.Request.Url == item.Request.Url, "Request URL restored");
            check(restored.Request.CookieHeader == item.Request.CookieHeader, "CookieHeader restored");
            check(restored.Request.Kind == DownloadKind.File, "Request Kind restored");

            return Task.CompletedTask;
        });

        // ── 13. Dynamic Chunk Splitting + 429 Throttling Regression ───────
        await test("regression: dynamic chunk splitting survives 429 throttling without IndexOutOfRangeException", async () =>
        {
            var payload = new byte[10 * 1024 * 1024]; // 10 MB so chunks > 4MB can split dynamically
            new Random(99).NextBytes(payload);
            int reqCount = 0;

            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0));
            await using var app = builder.Build();

            app.MapGet("/split429", async ctx =>
            {
                var rh = ctx.Request.Headers.Range.ToString();
                if (rh != "bytes=0-0")
                {
                    int c = Interlocked.Increment(ref reqCount);
                    // Trigger 429 on later worker requests (including dynamically split chunks)
                    if (c is 3 or 5 or 7)
                    {
                        ctx.Response.StatusCode = 429;
                        ctx.Response.Headers.RetryAfter = "0";
                        return;
                    }
                }
                long start = 0, end = payload.Length - 1;
                if (rh.StartsWith("bytes="))
                {
                    var spec = rh[6..].Split('-');
                    start = long.Parse(spec[0]);
                    end = spec[1].Length > 0 ? Math.Min(long.Parse(spec[1]), payload.Length - 1) : payload.Length - 1;
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{payload.Length}";
                }
                ctx.Response.Headers.AcceptRanges = "bytes";
                ctx.Response.ContentLength = end - start + 1;
                await ctx.Response.Body.WriteAsync(payload.AsMemory((int)start, (int)(end - start + 1)));
            });

            await app.StartAsync();
            var port = new Uri(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First()).Port;

            var dir = tmp();
            // Force large initial chunk (MaxChunkBytes=10MB, MinChunkBytes=8MB) with 8 workers so 7 workers must dynamically split!
            var downloader = new SegmentedDownloader(null, new EngineOptions
            {
                MinChunkBytes = 8 * 1024 * 1024,
                MaxChunkBytes = 10 * 1024 * 1024,
                ThrottleBackoff = TimeSpan.FromMilliseconds(50)
            });
            var item = new DownloadItem
            {
                Directory = dir,
                FileName = "split429.bin",
                Request = new DownloadRequest { Url = $"http://127.0.0.1:{port}/split429", FileName = "split429.bin" }
            };

            await downloader.RunAsync(item, maxConnections: 8, CancellationToken.None);
            check(File.Exists(item.SavePath), "split429 output file exists");
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.SavePath)));
            var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
            check(actualHash == expectedHash, "split429 byte-exact hash match");
            await app.StopAsync();
        });

        // ── 14. DownloadManager.Remove(deleteFile: true) Cleanup Regression ─
        await test("regression: DownloadManager.Remove(deleteFile: true) cleans partial & target files in Paused/Failed states", () =>
        {
            var dir = tmp();
            using var dm = new DownloadManager(dir, dir);
            var item = dm.Add(new DownloadRequest { Url = "http://127.0.0.1:1/unreachable.bin", FileName = "partial_test.bin" });
            dm.Pause(item.Id);

            var savePath = Path.Combine(dir, "partial_test.bin");
            item.SavePath = savePath;
            File.WriteAllText(savePath, "final");
            File.WriteAllText(savePath + ".mcdm-part", "part");
            File.WriteAllText(savePath + ".mcdm", "state");

            bool removed = dm.Remove(item.Id, deleteFile: true);
            check(removed, "Remove returned true");
            check(!File.Exists(savePath), "SavePath deleted even when Paused");
            check(!File.Exists(savePath + ".mcdm-part"), ".mcdm-part deleted");
            check(!File.Exists(savePath + ".mcdm"), ".mcdm state deleted");

            return Task.CompletedTask;
        });

        // ── 15. PickBestSubtitle Null-Safety Regression ───────────────────
        await test("regression: VideoDownloader.PickBestSubtitle handles null Label, Code, and Url safely", () =>
        {
            var subs = new List<SubtitleTrackInfo>
            {
                new() { Lang = null!, Label = null, Url = null! },
                new() { Lang = "tr", Label = null, Url = "https://sub.example/tr.vtt" }
            };
            var picked = VideoDownloader.PickBestSubtitle(subs, "tr");
            check(picked != null && picked.Url == "https://sub.example/tr.vtt", "picked valid Turkish track without NullReferenceException");
            return Task.CompletedTask;
        });

        // ── 16. 4K (2160p) & 2K (1440p) Detection & FormatSort Regression ─
        await test("regression: 4K (2160p) and 2K (1440p) detection across 16:9, 21:9 cinema, YouTube HTML, and CustomFormat sort", () =>
        {
            // 1. Standard 16:9 and Ultrawide 21:9 cinema aspect ratio normalization
            check(StreamProbeService.NormalizeResolutionTier(3840, 2160) == 2160, "3840x2160 -> 2160p 4K");
            check(StreamProbeService.NormalizeResolutionTier(3840, 1600) == 2160, "3840x1600 (21:9 cinema 4K) -> 2160p 4K");
            check(StreamProbeService.NormalizeResolutionTier(2560, 1440) == 1440, "2560x1440 -> 1440p 2K");
            check(StreamProbeService.NormalizeResolutionTier(2560, 1080) == 1440, "2560x1080 (21:9 cinema 2K) -> 1440p 2K");
            check(StreamProbeService.NormalizeResolutionTier(1920, 800) == 1080, "1920x800 (21:9 cinema 1080p) -> 1080p");
            check(StreamProbeService.NormalizeResolutionTier(1080, 1920) == 1080, "1080x1920 (9:16 vertical) -> 1080p");

            // 2. HLS manifest with 4K and 2K cinema variants
            string hls4k = @"#EXTM3U
#EXT-X-STREAM-INF:BANDWIDTH=18000000,RESOLUTION=3840x1600
4k/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=9500000,RESOLUTION=2560x1080
2k/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=4500000,RESOLUTION=1920x800
1080p/index.m3u8";
            var hlsRes = StreamProbeService.ParseHlsManifestText(hls4k, "https://cdn.example/master.m3u8");
            check(hlsRes.Qualities.Count == 3, "HLS 4K/2K/1080p count == 3");
            check(hlsRes.Qualities[0].Height == 2160 && hlsRes.Qualities[0].Label.Contains("4K"), "First HLS quality is 2160p 4K");
            check(hlsRes.Qualities[1].Height == 1440 && hlsRes.Qualities[1].Label.Contains("2K"), "Second HLS quality is 1440p 2K");
            check(hlsRes.Qualities[2].Height == 1080 && hlsRes.Qualities[2].Label.Contains("Full HD"), "Third HLS quality is 1080p");

            // 3. YouTube fast HTML ytInitialPlayerResponse parser
            string ytHtml = @"<html><script>var ytInitialPlayerResponse = {""videoDetails"":{""title"":""4K Test""},""streamingData"":{""adaptiveFormats"":[{""mimeType"":""video/webm"",""width"":3840,""height"":1600,""qualityLabel"":""2160p60"",""bitrate"":20000000},{""mimeType"":""video/webm"",""width"":2560,""height"":1080,""qualityLabel"":""1440p"",""bitrate"":10000000},{""mimeType"":""video/mp4"",""width"":1920,""height"":1080,""qualityLabel"":""1080p"",""bitrate"":4500000}]}};</script></html>";
            var ytRes = StreamProbeService.ParseYouTubePlayerResponseFromHtml(ytHtml);
            check(ytRes != null && ytRes.Qualities.Count == 3, "Fast YouTube parser extracted 3 qualities");
            check(ytRes!.Qualities[0].Height == 2160 && ytRes.Qualities[1].Height == 1440 && ytRes.Qualities[2].Height == 1080, "Fast YouTube parser ordered 2160 -> 1440 -> 1080");

            // 4. VideoDownloader.FormatSort does not cap CustomFormat at 1080p
            var custom4kOpt = new VideoOptions { MaxHeight = 1080, CustomFormat = "bv*[height<=2160]+ba/b" };
            check(string.IsNullOrEmpty(VideoDownloader.FormatSort(custom4kOpt)), "FormatSort must be empty when CustomFormat is explicitly set");

            return Task.CompletedTask;
        });

        // ── 17. Subtitle XML/VTT Conversion, Lang Code Normalization & Dubbing Sort ─
        await test("regression: YouTube XML timedtext to SRT conversion, NormalizeSubtitleFileLangCode, and SortAudioTracks", () =>
        {
            // 1. YouTube srv1 XML (<text start="..." dur="...">) -> SRT
            string srv1Xml = @"<?xml version=""1.0"" encoding=""utf-8"" ?><transcript><text start=""1.5"" dur=""2.25"">Merhaba &amp; Hoşgeldiniz</text><text start=""4.0"" dur=""3.5"">İkinci satır</text></transcript>";
            string srt1 = VideoDownloader.ConvertXmlTimedTextToSrt(srv1Xml);
            check(srt1.Contains("00:00:01,500 --> 00:00:03,750"), "srv1 XML timestamps converted to SRT: " + srt1);
            check(srt1.Contains("Merhaba & Hoşgeldiniz"), "srv1 HTML entities decoded");

            // 2. YouTube srv3 XML (<p t="1500" d="2250">) -> SRT
            string srv3Xml = @"<?xml version=""1.0"" encoding=""utf-8"" ?><timedtext><body><p t=""1500"" d=""2250""><s>Test</s> <s>Altyazı</s></p></body></timedtext>";
            string srt3 = VideoDownloader.ConvertXmlTimedTextToSrt(srv3Xml);
            check(srt3.Contains("00:00:01,500 --> 00:00:03,750"), "srv3 XML ms timestamps converted to SRT");
            check(srt3.Contains("Test Altyazı"), "srv3 inner tags stripped cleanly");

            // 3. NormalizeSubtitleFileLangCode avoids .tr,tur,Turkish.srt or .all.srt
            check(VideoDownloader.NormalizeSubtitleFileLangCode("tr,tur,Turkish,turkish,tr-TR,tr-tr") == "tr", "comma-separated lang list normalized to 'tr'");
            check(VideoDownloader.NormalizeSubtitleFileLangCode("en-US,en.*,en") == "en", "hyphenated lang list normalized to 'en'");
            check(VideoDownloader.NormalizeSubtitleFileLangCode("all", new SubtitleTrackInfo { Lang = "de" }) == "de", "'all' falls back to bestSub lang");

            // 4. SortAudioTracks keeps Original first and Turkish Dub second consistently
            var unsortedAudios = new List<AudioTrackInfo>
            {
                new() { TrackId = "es", Lang = "es", Label = "İspanyolca Dublaj" },
                new() { TrackId = "tr", Lang = "tr", Label = "Türkçe Dublaj" },
                new() { TrackId = "original", Lang = "en", Label = "Orijinal Ses - İngilizce", IsDefault = true }
            };
            var sorted = StreamProbeService.SortAudioTracks(unsortedAudios);
            check(sorted[0].TrackId == "original", "Original audio sorted first");
            check(sorted[1].TrackId == "tr", "Turkish dubbing sorted second");
            check(sorted[2].TrackId == "es", "Other languages sorted after");

            // 5. Turkish movie site HLS manifest where "Türkçe Dublaj" has DEFAULT=YES and URL has "trdual"
            string trDualManifest = @"#EXTM3U
#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=""audio"",NAME=""Türkçe Dublaj"",DEFAULT=YES,AUTOSELECT=YES,LANGUAGE=""tr"",URI=""a1.m3u8""
#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=""audio"",NAME=""Altyazılı"",DEFAULT=NO,AUTOSELECT=NO,LANGUAGE=""en"",URI=""a2.m3u8""
#EXT-X-STREAM-INF:BANDWIDTH=4500000,RESOLUTION=1920x1080,AUDIO=""audio""
v1.m3u8";
            var trDualRes = StreamProbeService.ParseHlsManifestText(trDualManifest, "https://srv12.cdnimages3330.cyou/hls/godfather-1-1972-bluray-trdualmp4/master.m3u8");
            check(trDualRes.AudioTracks.Count == 2, "Must detect both audio tracks on trdual manifest, got " + trDualRes.AudioTracks.Count);
            check(trDualRes.AudioTracks.Any(a => a.TrackId == "tr" && a.Label == "Türkçe Dublaj"), "Turkish track with DEFAULT=YES must be labeled 'Türkçe Dublaj', NOT 'Orijinal Ses - Türkçe'");
            check(trDualRes.AudioTracks.Any(a => a.TrackId == "original" && a.Label.Contains("Orijinal Ses")), "Second track must be labeled 'Orijinal Ses'");

            return Task.CompletedTask;
        });

        // ── 18. VideoDownloader .mcdm-video-<id> WorkDir Cleanup Regression ─
        await test("regression: VideoDownloader cleans up .mcdm-video-<id> workDir on completion", async () =>
        {
            var testDir = tmp();
            var item = new DownloadItem
            {
                Id = "testclean123",
                Directory = testDir,
                FileName = "TestMovie.mp4",
                WorkDir = Path.Combine(testDir, ".mcdm-video-testclean123"),
                Status = DownloadStatus.Downloading,
                Request = new DownloadRequest
                {
                    Url = "https://cdn.example/video.mp4",
                    Kind = DownloadKind.Video,
                    SubtitlesOnly = true,
                    SubtitleLangs = "tr",
                    SubtitleContent = "WEBVTT\n\n00:01.000 --> 00:03.000\nTest Altyazı Satırı\n"
                }
            };

            var vd = new VideoDownloader(() => new ToolLauncher("cmd.exe", Array.Empty<string>()), () => null, () => new VideoOptions());
            await vd.RunAsync(item, CancellationToken.None);

            check(File.Exists(item.SavePath), "Final SRT file must exist at SavePath");
            check(!Directory.Exists(Path.Combine(testDir, ".mcdm-video-testclean123")), ".mcdm-video-<id> WorkDir must be completely deleted after RunAsync finishes");
        });
    }
}

