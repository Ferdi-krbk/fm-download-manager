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
        await test("video: VideoDownloader argument construction, format sorting and cookies", () =>
        {
            var opt = new VideoOptions { Container = "mp4", MaxHeight = 720 };
            var args = VideoDownloader.BuildArguments("https://example.com/watch?v=123", opt, @"C:\Downloads", @"C:\Downloads\.temp", @"C:\temp\cookies.txt", @"C:\Tools\ffmpeg.exe");

            check(args.Contains("-c"), "should contain resume/continue -c flag");
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
                Request = new DownloadRequest { Url = "https://example.com/video", Kind = DownloadKind.Video },
                Directory = testDir
            };

            await downloader.RunAsync(item, CancellationToken.None);

            check(item.Status == DownloadStatus.Completed || item.SavePath == outMp4, "save path was not set");
            check(item.FileName == "video_out.mp4", "file name was not updated");
            check(item.TotalBytes == "FAKE_VIDEO_CONTENT".Length, "total bytes not updated from final file");
            check(item.DownloadedBytes == "FAKE_VIDEO_CONTENT".Length, "downloaded bytes not updated");

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
        });
    }
}
