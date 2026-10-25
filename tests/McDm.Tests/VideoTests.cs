using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McDm.Core;

namespace McDm.Tests;

public static class VideoTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check, Func<string> tmp)
    {
        await test("video: Python runtime failure retains its actual cause", () =>
        {
            var message = VideoDownloader.DescribeToolFailure(new[] {
                "Traceback (most recent call last):", "PermissionError: access denied",
                "[PYI-26004:ERROR] Failed to execute script '__main__' due to unhandled exception!"
            }, 1);
            check(message.Contains("PermissionError: access denied"), "generic PyInstaller banner hid actual cause");
            check(!message.Contains("unhandled exception"), "raw runtime banner leaked instead of useful diagnostic");
            var ordinary = VideoDownloader.DescribeToolFailure(new[] { "ERROR: HTTP Error 403: Forbidden" }, 1);
            check(ordinary.Contains("403"), "provider error was lost");
            return Task.CompletedTask;
        });

        await test("video: VideoDownloader argument construction, format sorting and cookies", () =>
        {
            var opt = new VideoOptions { Container = "mp4", MaxHeight = 720 };
            var args = VideoDownloader.BuildArguments("https://example.com/watch?v=123", opt, @"C:\Downloads", @"C:\Downloads\.temp", @"C:\temp\cookies.txt", @"C:\Tools\ffmpeg.exe");

            check(args.Contains("-c"), "should contain resume/continue -c flag");
            check(!args.Contains("--no-check-certificates"), "HTTPS verification must remain enabled even when cookies are supplied");
            check(args.Contains("--newline"), "should contain --newline");
            check(args.Contains("--progress-template"), "should contain --progress-template");
            check(args.Contains("--merge-output-format"), "should contain merge output format");
            check(args[args.IndexOf("--merge-output-format") + 1] == "mp4", "merge output format must be mp4");
            check(args.Contains("--ffmpeg-location"), "should contain ffmpeg location");
            check(args[args.IndexOf("--ffmpeg-location") + 1] == @"C:\Tools\ffmpeg.exe", "ffmpeg location mismatch");
            check(args.Contains("--cookies"), "should contain cookies arg");
            check(args[args.IndexOf("--cookies") + 1] == @"C:\temp\cookies.txt", "cookies file mismatch");
            check(args.Contains("-S"), "should contain sort arg");
            check(args[args.IndexOf("-S") + 1].Contains("res:720"), "sort should contain res:720");
            check(args.Contains("--"), "should contain end-of-options marker");
            check(args[^1] == "https://example.com/watch?v=123", "last arg must be url");
            check(!args.Contains("--limit-rate"), "unlimited download should not have --limit-rate");

            var optThrottled = new VideoOptions { Container = "mp4", MaxHeight = 720, SpeedLimitBytes = 2097152 };
            var argsThrottled = VideoDownloader.BuildArguments("https://example.com/watch?v=123", optThrottled, @"C:\Downloads", @"C:\Downloads\.temp", null, null);
            check(argsThrottled.Contains("--limit-rate"), "throttled download should have --limit-rate");
            check(argsThrottled[argsThrottled.IndexOf("--limit-rate") + 1] == "2097152", "--limit-rate value mismatch");

            var req = new DownloadRequest
            {
                Url = "https://sub.domain.com/video",
                Cookies = new List<BrowserCookie>
                {
                    new() { Name = "AUTH", Value = "xyz", Domain = ".domain.com", Path = "/", Secure = true, HttpOnly = true, ExpirationDate = 2000000000 }
                }
            };
            var netscape = VideoDownloader.BuildNetscapeCookies(req);
            check(netscape.StartsWith("# Netscape HTTP Cookie File"), "header missing");
            check(netscape.Contains("#HttpOnly_.domain.com\tTRUE\t/\tTRUE\t2000000000\tAUTH\txyz"), "cookie line incorrect: " + netscape);

            return Task.CompletedTask;
        });

        await test("video: VideoDownloader subtitle argument generation (standalone & embedded)", () =>
        {
            // Standalone subtitles only
            var subsOnlyOpt = new VideoOptions
            {
                SubtitlesOnly = true,
                SubtitleLangs = "tr,en"
            };
            var subsArgs = VideoDownloader.BuildArguments("https://example.com/watch?v=123", subsOnlyOpt, @"C:\Downloads", @"C:\Downloads\.temp", null, null);
            check(subsArgs.Contains("--skip-download"), "SubtitlesOnly should include --skip-download");
            check(subsArgs.Contains("--write-subs"), "SubtitlesOnly should include --write-subs");
            check(subsArgs.Contains("--write-auto-subs"), "SubtitlesOnly should include --write-auto-subs");
            check(subsArgs.Contains("--sub-langs"), "SubtitlesOnly should include --sub-langs");
            check(subsArgs[subsArgs.IndexOf("--sub-langs") + 1] == "tr,en", "sub-langs must be tr,en");
            check(subsArgs.Contains("--convert-subs"), "SubtitlesOnly should include --convert-subs");
            check(subsArgs[subsArgs.IndexOf("--convert-subs") + 1] == "srt", "convert-subs must be srt");

            // Video with embedded subtitles
            var embedOpt = new VideoOptions
            {
                Container = "mp4",
                WriteSubtitles = true,
                SubtitleLangs = "tr"
            };
            var embedArgs = VideoDownloader.BuildArguments("https://example.com/watch?v=123", embedOpt, @"C:\Downloads", @"C:\Downloads\.temp", null, null);
            check(!embedArgs.Contains("--skip-download"), "Video with embedded subtitles must NOT have --skip-download");
            check(embedArgs.Contains("--embed-subs"), "WriteSubtitles should include --embed-subs");
            check(embedArgs.Contains("--write-subs"), "WriteSubtitles should include --write-subs");
            check(embedArgs[embedArgs.IndexOf("--sub-langs") + 1] == "tr", "sub-langs must be tr");

            // Verify fallback defaults
            var defaultSubs = new VideoOptions { SubtitlesOnly = true };
            var defaultArgs = VideoDownloader.BuildArguments("https://example.com/watch?v=123", defaultSubs, @"C:\Downloads", @"C:\Downloads\.temp", null, null, "CustomUA", "https://example.com/movie");
            check(defaultArgs[defaultArgs.IndexOf("--sub-langs") + 1] == "tr,en", "default sub-langs should be tr,en");
            check(defaultArgs.Contains("--extractor-args"), "should contain --extractor-args");
            check(defaultArgs[defaultArgs.IndexOf("--extractor-args") + 1] == "generic:impersonate", "should impersonate for anti-bot bypass");
            check(defaultArgs.Contains("--user-agent"), "should contain --user-agent");
            check(defaultArgs[defaultArgs.IndexOf("--user-agent") + 1] == "CustomUA", "user-agent mismatch");
            check(defaultArgs.Contains("--referer"), "should contain --referer");
            check(defaultArgs[defaultArgs.IndexOf("--referer") + 1] == "https://example.com/movie", "referer mismatch");
            check(defaultArgs.Contains("--trim-filenames"), "should contain --trim-filenames");
            check(defaultArgs[defaultArgs.IndexOf("--trim-filenames") + 1] == "60", "trim-filenames should be 60 to prevent MAX_PATH errors");

            var safeNameArgs = VideoDownloader.BuildArguments("https://example.com/stream.m3u8", defaultSubs, @"C:\Downloads", @"C:\Downloads\.temp", null, null, fileName: "The Mentalist S02E09");
            check(safeNameArgs.Contains("-o"), "should contain -o");
            string tmpl = safeNameArgs[safeNameArgs.IndexOf("-o") + 1];
            check(!tmpl.Contains("[%(id)s]"), "template must not contain raw [%(id)s] URL query strings");
            check(tmpl.Contains("The Mentalist S02E09"), "template should use clean fileName: " + tmpl);

            return Task.CompletedTask;
        });

        await test("video: WebVTT to SRT conversion with timestamps, numbering and tag stripping", () =>
        {
            var vtt = @"WEBVTT
Kind: captions
Language: tr

NOTE This is a test subtitle note

1
00:00:01.200 --> 00:00:04.500 line:0% position:50% align:center
<c.yellow>Merhaba dünya!</c>

2
01:15.300 --> 01:18.900
<v Narrator>İkinci <b>satır</b> açıklaması</v>

3
01:20:00.000 --> 01:20:05.123
Son sahne metni.
";
            var srt = VideoDownloader.ConvertVttToSrt(vtt);

            check(srt.Contains("1\r\n00:00:01,200 --> 00:00:04,500\r\nMerhaba dünya!") ||
                  srt.Contains("1\n00:00:01,200 --> 00:00:04,500\nMerhaba dünya!"), "first cue mismatch: " + srt);
            check(srt.Contains("2\r\n00:01:15,300 --> 00:01:18,900\r\nİkinci satır açıklaması") ||
                  srt.Contains("2\n00:01:15,300 --> 00:01:18,900\nİkinci satır açıklaması"), "second cue short timestamp mismatch: " + srt);
            check(srt.Contains("3\r\n01:20:00,000 --> 01:20:05,123\r\nSon sahne metni.") ||
                  srt.Contains("3\n01:20:00,000 --> 01:20:05,123\nSon sahne metni."), "third cue mismatch: " + srt);
            check(!srt.Contains("WEBVTT"), "header should be stripped");
            check(!srt.Contains("NOTE"), "notes should be stripped");
            check(!srt.Contains("<c.yellow>"), "styling tags should be stripped");
            check(!srt.Contains("<v Narrator>"), "voice tags should be stripped");
            check(!srt.Contains("<b>"), "bold tags should be stripped");

            return Task.CompletedTask;
        });

        await test("video: VideoDownloader progress parsing & accumulator", () =>
        {
            var line1 = "MCDM_PROGRESS|downloading|1048576|10485760|10485760|524288|1|10";
            var p1 = VideoDownloader.ParseProgress(line1);
            check(p1 != null, "failed to parse progress line");
            check(p1!.Status == "downloading", "status mismatch");
            check(p1.Downloaded == 1048576, "downloaded bytes mismatch");
            check(p1.Total == 10485760, "total bytes mismatch");
            check(p1.Speed == 524288, "speed mismatch");
            check(p1.Fragment == 1 && p1.FragmentCount == 10, "fragment mismatch");
            check(!p1.IsTotalEstimated, "an exact content length must allow ETA");
            var estimated = VideoDownloader.ParseProgress("MCDM_PROGRESS|downloading|1000000|NA|3000000|1000000|1|30")!;
            check(estimated.IsTotalEstimated, "HLS estimate must be distinguished from exact size");
            var etaItem = new DownloadItem { Status = DownloadStatus.Downloading, DownloadedBytes = 1000000, TotalBytes = 3000000, Speed = 1000000 };
            check(etaItem.Eta != null, "known size should provide ETA");
            etaItem.IsTotalEstimated = estimated.IsTotalEstimated;
            check(etaItem.Eta != null && etaItem.EtaFormatted.StartsWith("~"), "estimated HLS ETA must be visible and marked approximate");
            var reported = VideoDownloader.ParseProgress("MCDM_PROGRESS|downloading|1000|NA|NA|500|1|30|58.5")!;
            check(reported.EtaSeconds == 58.5, "tool ETA was discarded");
            etaItem.ReportedEtaSeconds = reported.EtaSeconds;
            etaItem.TotalBytes = -1;
            check(etaItem.Eta?.TotalSeconds == 58.5, "tool ETA must work without a known total size");
            etaItem.Speed = 0;
            check(etaItem.Eta == null, "stalled transfer should not display a stale countdown");
            etaItem.Speed = 500;
            etaItem.ReportedEtaSeconds = null;
            etaItem.TotalBytes = 2000000;
            check(etaItem.Eta != null, "slow transfer ETA was hidden by the old speed threshold");

            var acc = new VideoDownloader.ProgressAccumulator();
            var r1 = acc.Apply(p1);
            check(r1.Downloaded == 1048576 && r1.Total == 10485760, "acc 1st step mismatch");

            var pFin = VideoDownloader.ParseProgress("MCDM_PROGRESS|finished|10485760|10485760|10485760|0|10|10")!;
            var rFin = acc.Apply(pFin);
            check(rFin.Downloaded == 10485760, "acc finished stream mismatch");

            // audio stream begins
            var pAudio = VideoDownloader.ParseProgress("MCDM_PROGRESS|downloading|500000|2000000|2000000|100000|1|1")!;
            var rAudio = acc.Apply(pAudio);
            check(rAudio.Downloaded == 10485760 + 500000, "accumulated downloaded across streams mismatch");
            check(rAudio.Total == 10485760 + 2000000, "accumulated total across streams mismatch");

            // Test transition where "finished" status is NOT emitted by yt-dlp (e.g. HLS stream transition)
            var accNoFin = new VideoDownloader.ProgressAccumulator();
            accNoFin.Apply(VideoDownloader.ParseProgress("MCDM_PROGRESS|downloading|700000000|700000000|700000000|20000000||")!);
            var rSeamlessAudio = accNoFin.Apply(VideoDownloader.ParseProgress("MCDM_PROGRESS|downloading|100000|30000000|30000000|1000000|1|300")!);
            check(rSeamlessAudio.Downloaded == 700000000 + 100000, "progress should not reset to 0 when transitioning streams: " + rSeamlessAudio.Downloaded);
            check(rSeamlessAudio.Total == 700000000 + 30000000, "total should encompass both streams: " + rSeamlessAudio.Total);

            // Test concurrent fragments configuration
            var ytArgs = VideoDownloader.BuildArguments("https://www.youtube.com/watch?v=abc", new VideoOptions(), @"C:\Downloads", @"C:\Downloads\.temp", null, null);
            check(ytArgs.Contains("--concurrent-fragments"), "should have concurrent-fragments");
            check(ytArgs[ytArgs.IndexOf("--concurrent-fragments") + 1] == "5", "YouTube should have 5 concurrent fragments");

            var hlsArgs = VideoDownloader.BuildArguments("https://srv9.cdnimages3140.cyou/master.txt", new VideoOptions(), @"C:\Downloads", @"C:\Downloads\.temp", null, null);
            check(hlsArgs[hlsArgs.IndexOf("--concurrent-fragments") + 1] == "1", "Generic HLS should have 1 fragment worker to prevent 429 rate limits");
            check(hlsArgs.Contains("fragment:0.5"), "should use fragment retry sleep");

            return Task.CompletedTask;
        });

        await test("video: fake yt-dlp execution lifecycle (progress, output file, cancel/pause)", async () =>
        {
            var testDir = tmp();
            // Create a fake yt-dlp executable script via a small C# console app or dotnet runner
            string? node = null;
            foreach (var cand in new[] { "node", "node.exe", @"C:\Program Files\nodejs\node.exe", "/usr/bin/node", "/usr/local/bin/node" })
            {
                try
                {
                    using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cand, "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
                    probe.WaitForExit(5000); if (probe.ExitCode == 0) { node = cand; break; }
                }
                catch { }
            }
            if (node == null) { Console.WriteLine("      (node not found: fake yt-dlp test skipped)"); return; }

            var fakeScript = Path.Combine(testDir, "fake_ytdlp.js");
            var outMp4 = Path.Combine(testDir, "video_out.mp4");
            File.WriteAllBytes(outMp4, Encoding.UTF8.GetBytes("FAKE_VIDEO_CONTENT"));

            var jsLines = new[]
            {
                "console.log('MCDM_TITLE|Sample Video Title');",
                "console.log('MCDM_PROGRESS|downloading|5000|10000|10000|2500||');",
                "console.log('[Merger] Merging formats into video_out.mp4');",
                $"console.log('MCDM_FILE|' + {JsonSerializer.Serialize(outMp4)});",
                "process.exit(0);"
            };
            File.WriteAllLines(fakeScript, jsLines);

            var launcher = new ToolLauncher(node, new[] { fakeScript });
            var downloader = new VideoDownloader(() => launcher, () => null, () => new VideoOptions());

            var item = new DownloadItem
            {
                Request = new DownloadRequest
                {
                    Url = "https://example.com/video",
                    Kind = DownloadKind.Video,
                    WriteSubtitles = true,
                    SubtitleLangs = "tr",
                    SubtitleContent = "WEBVTT\n\n00:01.000 --> 00:03.000\nMerhaba Dünya\n"
                },
                Directory = testDir
            };

            string expectedWorkDir = Path.Combine(testDir, ".mcdm-video-" + item.Id);
            await downloader.RunAsync(item, CancellationToken.None);

            check(item.Status == DownloadStatus.Completed || item.SavePath == outMp4, "save path was not set");
            check(item.FileName == "video_out.mp4", "file name was not updated");
            check(item.TotalBytes == "FAKE_VIDEO_CONTENT".Length, "total bytes not updated from final file");
            check(item.DownloadedBytes == "FAKE_VIDEO_CONTENT".Length, "downloaded bytes not updated");
            check(!Directory.Exists(expectedWorkDir), ".mcdm-video-* temporary work directory must be deleted after video+subtitle download");
            check(File.Exists(Path.Combine(testDir, "video_out.tr.srt")), "sidecar .tr.srt file must be copied to output directory");

            // Test cancellation / pause kills process
            var hangScript = Path.Combine(testDir, "hang_ytdlp.js");
            File.WriteAllLines(hangScript, new[]
            {
                "console.log('MCDM_PROGRESS|downloading|100|10000|10000|100||');",
                "setInterval(() => {}, 1000);"
            });

            var hangLauncher = new ToolLauncher(node, new[] { hangScript });
            var hangDownloader = new VideoDownloader(() => hangLauncher, () => null, () => new VideoOptions());
            var hangItem = new DownloadItem
            {
                Request = new DownloadRequest { Url = "https://example.com/hang", Kind = DownloadKind.Video },
                Directory = testDir
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            bool caught = false;
            try
            {
                await hangDownloader.RunAsync(hangItem, cts.Token);
            }
            catch (OperationCanceledException)
            {
                caught = true;
            }

            check(caught, "Cancellation token did not stop fake yt-dlp");

            // Exercise the separate-audio subprocess path: its output used to be discarded.
            using var culture = new UiCultureScope("en-US");
            var audioMethod = typeof(VideoDownloader).GetMethod("DownloadExternalAudioStreamAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Task<string?> RunAudio(VideoDownloader engine, ToolLauncher tool, DownloadItem audioItem, CancellationToken token) =>
                (Task<string?>)audioMethod.Invoke(engine, new object?[] { audioItem, tool, null, null, token })!;
            string audioScript = Path.Combine(testDir, "audio_ytdlp.js");
            string argsFile = Path.Combine(testDir, "audio_args.json");
            File.WriteAllText(audioScript, $$"""
                const fs = require('fs'), path = require('path');
                const a = process.argv.slice(2);
                fs.writeFileSync({{JsonSerializer.Serialize(argsFile)}}, JSON.stringify(a));
                console.log('MCDM_PROGRESS|downloading|500000|NA|2000000|1000000|1|3');
                setTimeout(() => console.error('WARNING: Retrying fragment 2 (1/30)'), 100);
                setTimeout(() => console.log('MCDM_PROGRESS|downloading|1000000|NA|2200000|800000|2|3'), 250);
                setTimeout(() => {
                    fs.writeFileSync(path.join(a[a.indexOf('-P')+1], a[a.indexOf('-o')+1].replace('%(ext)s', 'm4a')), 'audio');
                    process.exit(0);
                }, 450);
                """);
            var audioTool = new ToolLauncher(node, new[] { audioScript });
            var audioEngine = new VideoDownloader(() => audioTool, () => null, () => new VideoOptions());
            var audioItem = new DownloadItem
            {
                Request = new DownloadRequest { Url = "https://example.com/video", AudioStreamUrl = "https://example.com/audio.m3u8" },
                WorkDir = testDir, DownloadedBytes = 10000000, TotalBytes = 10000000,
                Speed = 9999999, Status = DownloadStatus.Downloading
            };
            bool sawAudioProgress = false, sawRetry = false;
            audioItem.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadItem.Speed) && audioItem.Speed == 800000)
                    sawAudioProgress = audioItem.DownloadedBytes == 11000000 && audioItem.TotalBytes == 12200000 && audioItem.Eta != null && audioItem.EtaFormatted.StartsWith("~");
                if (e.PropertyName == nameof(DownloadItem.Stage) && audioItem.Stage?.Contains("Retrying audio") == true)
                    sawRetry = audioItem.Speed == 0;
            };
            string? audioPath = await RunAudio(audioEngine, audioTool, audioItem, CancellationToken.None);
            check(audioPath != null && File.Exists(audioPath), "external audio output not found");
            check(sawAudioProgress, "external audio must update bytes/speed while preserving the completed video bytes");
            check(sawRetry, "fragment retries on stderr must clear stale speed and identify the waiting stage");
            check(audioItem.Speed == 0, "audio exit must clear transfer speed before muxing");
            var audioArgs = JsonSerializer.Deserialize<string[]>(File.ReadAllText(argsFile))!;
            check(audioArgs.Contains("--progress-template") && audioArgs.Contains("--socket-timeout"), "audio must use shared progress and bounded network options");
            check(!audioArgs.Contains("--no-check-certificates"), "separate audio must preserve HTTPS certificate validation");

            string audioPid = Path.Combine(testDir, "audio.pid");
            string audioHangScript = Path.Combine(testDir, "audio_hang.js");
            File.WriteAllText(audioHangScript, $"require('fs').writeFileSync({JsonSerializer.Serialize(audioPid)}, String(process.pid)); setInterval(() => {{}}, 1000);");
            var audioHangTool = new ToolLauncher(node, new[] { audioHangScript });
            using var audioCts = new CancellationTokenSource();
            var pendingAudio = RunAudio(audioEngine, audioHangTool, audioItem, audioCts.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(audioPid) && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(25);
            audioCts.Cancel();
            caught = false;
            try { await pendingAudio; } catch (OperationCanceledException) { caught = true; }
            check(caught, "external audio must propagate pause/cancellation");
            check(File.Exists(audioPid), "external audio cancellation fixture did not start");
            if (File.Exists(audioPid))
            {
                bool exited;
                try { using var child = Process.GetProcessById(int.Parse(File.ReadAllText(audioPid))); exited = child.WaitForExit(5000); }
                catch (ArgumentException) { exited = true; }
                check(exited, "cancelled external audio subprocess remained running");
            }
        });

        await test("video: StreamProbeService dynamic HLS manifest extraction (qualities, audio tracks, subtitles)", () =>
        {
            string sampleManifest = @"#EXTM3U
#EXT-X-VERSION:3
#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=""audio"",NAME=""Original Audio"",DEFAULT=YES,AUTOSELECT=YES,LANGUAGE=""en"",URI=""audio/en/index.m3u8""
#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=""audio"",NAME=""Turkish"",DEFAULT=NO,AUTOSELECT=NO,LANGUAGE=""tr"",URI=""audio/tr/index.m3u8""
#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=""subs"",NAME=""Türkçe"",DEFAULT=YES,AUTOSELECT=YES,LANGUAGE=""tr"",URI=""sub/tr/sub.vtt""
#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=""subs"",NAME=""English"",DEFAULT=NO,AUTOSELECT=NO,LANGUAGE=""en"",URI=""sub/en/sub.vtt""
#EXT-X-STREAM-INF:BANDWIDTH=4500000,RESOLUTION=1920x1080,AUDIO=""audio"",SUBTITLES=""subs""
1080p/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,AUDIO=""audio"",SUBTITLES=""subs""
720p/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=1200000,RESOLUTION=854x480,AUDIO=""audio"",SUBTITLES=""subs""
480p/index.m3u8
";
            var result = StreamProbeService.ParseHlsManifestText(sampleManifest, "https://example.com/hls/master.m3u8");

            check(result.Qualities.Count == 3, "Expected 3 qualities, got " + result.Qualities.Count);
            check(result.Qualities[0].Height == 1080, "First quality should be 1080p");
            check(result.Qualities[1].Height == 720, "Second quality should be 720p");
            check(result.Qualities[2].Height == 480, "Third quality should be 480p");

            check(result.AudioTracks.Count == 2, "Expected 2 audio tracks, got " + result.AudioTracks.Count);
            check(result.AudioTracks[0].TrackId == "original", "First audio track should be original");
            check(result.AudioTracks[1].TrackId == "tr", "Second audio track should be tr");

            check(result.Subtitles.Count == 2, "Expected 2 subtitles, got " + result.Subtitles.Count);
            check(result.Subtitles[0].Lang == "tr", "First subtitle should be tr");
            check(result.Subtitles[0].Url == "https://example.com/hls/sub/tr/sub.vtt", "Subtitle URL should be resolved against base");

            return Task.CompletedTask;
        });

        await test("video: multi-language audio dubs and subtitle localization across global languages", () =>
        {
            using var culture = new UiCultureScope("tr-TR");
            // 1. Language Info detection
            var esInfo = StreamProbeService.GetLanguageInfo("es");
            check(esInfo.Turkish == "İspanyolca" && esInfo.Native == "Español", "es language info mismatch");

            var jaInfo = StreamProbeService.GetLanguageInfo("ja");
            check(jaInfo.Turkish == "Japonca" && jaInfo.Native == "日本語", "ja language info mismatch");

            var deInfo = StreamProbeService.GetLanguageInfo("de");
            check(deInfo.Turkish == "Almanca" && deInfo.Native == "Deutsch", "de language info mismatch");

            var ruInfo = StreamProbeService.GetLanguageInfo("ru");
            check(ruInfo.Turkish == "Rusça" && ruInfo.Native == "Русский", "ru language info mismatch");

            var arInfo = StreamProbeService.GetLanguageInfo("ar");
            check(arInfo.Turkish == "Arapça" && arInfo.Native == "العربية", "ar language info mismatch");

            // 2. Audio Dubbing label formatting
            string origLabel = StreamProbeService.FormatAudioLabel("en", "Original", isOriginal: true);
            check(origLabel.Contains("Orijinal Ses") && origLabel.Contains("İngilizce"), "Original audio label mismatch: " + origLabel);

            string trDubLabel = StreamProbeService.FormatAudioLabel("tr", "Türkçe - dubbed", isOriginal: false);
            check(trDubLabel == "Türkçe Dublaj", "Turkish dub label mismatch: " + trDubLabel);

            string esDubLabel = StreamProbeService.FormatAudioLabel("es", "Español - dubbed", isOriginal: false);
            check(esDubLabel == "İspanyolca Dublaj (Español)", "Spanish dub label mismatch: " + esDubLabel);

            string jaDubLabel = StreamProbeService.FormatAudioLabel("ja", "日本語 - dubbed", isOriginal: false);
            check(jaDubLabel == "Japonca Dublaj (日本語)", "Japanese dub label mismatch: " + jaDubLabel);

            // 3. Subtitle label formatting
            string trSub = StreamProbeService.FormatSubtitleLabel("tr", isAuto: false);
            check(trSub == "Türkçe Altyazı", "TR subtitle label mismatch: " + trSub);

            string esSub = StreamProbeService.FormatSubtitleLabel("es", isAuto: false);
            check(esSub == "İspanyolca (Español) Altyazı", "ES subtitle label mismatch: " + esSub);

            string autoSub = StreamProbeService.FormatSubtitleLabel("de", isAuto: true);
            check(autoSub == "Almanca (Deutsch) Altyazı (Otomatik)", "DE auto subtitle mismatch: " + autoSub);

            // 4. Subtitle file normalization & UTF-8 BOM encoding
            string tempSubFile = Path.Combine(Path.GetTempPath(), "test_sub_" + Guid.NewGuid().ToString("N") + ".srt");
            try
            {
                string rawDirty = "1\r\n00:00:00,100 --> 00:00:02,500\r\n\u200B\u200B<b>\u200B Bütün marketi satın aldım. \u200B</b>\r\n\r\n" +
                                  "2\r\n00:00:03,000 --> 00:00:05,000\r\n<font color=\"red\">Çılgın teklif şu:</font>\r\n";
                File.WriteAllText(tempSubFile, rawDirty, new UTF8Encoding(false));

                VideoDownloader.CleanAndNormalizeSubtitleFile(tempSubFile);

                var bytes = File.ReadAllBytes(tempSubFile);
                check(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "File must have UTF-8 BOM header");

                string cleaned = Encoding.UTF8.GetString(bytes[3..]);
                check(!cleaned.Contains("\u200B"), "Must not contain zero-width space");
                check(!cleaned.Contains("<b>") && !cleaned.Contains("</b>"), "Must not contain <b> tags");
                check(!cleaned.Contains("<font"), "Must not contain font tags");
                check(cleaned.Contains("Bütün marketi satın aldım."), "Must preserve Turkish characters: ü, ı");
                check(cleaned.Contains("Çılgın teklif şu:"), "Must preserve Turkish characters: Ç, ı, ş");
            }
            finally
            {
                if (File.Exists(tempSubFile)) File.Delete(tempSubFile);
            }

            return Task.CompletedTask;
        });
    }

}
