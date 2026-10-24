using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McDm.App;
using McDm.App.Resources;
using McDm.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using AppSettings = McDm.App.AppSettings;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int result = 0;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FMDownloadManager;component/Themes/Theme.xaml", UriKind.Relative) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FMDownloadManager;component/Themes/Controls.xaml", UriKind.Relative) });
        application.Startup += (_, _) =>
        {
            try { Run(args); }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally { application.Shutdown(); }
        };
        application.Run();
        return result;
    }

    private static void Run(string[] args)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "McDownloadManager.sln"))) repository = repository.Parent;
        if (repository == null) throw new DirectoryNotFoundException("Run this acceptance test from the source checkout");
        Directory.SetCurrentDirectory(repository.FullName);
        string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "dist/acceptance");
        string screenshots = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "docs/screenshots");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(screenshots);
        string fixtures = Path.Combine(root, "fixtures");
        string downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(fixtures);
        Directory.CreateDirectory(downloads);
        string tools = Path.GetFullPath("Tools");
        string ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        string ytDlp = Path.Combine(tools, "yt-dlp.exe");
        string ffprobe = Path.Combine(tools, "ffprobe.exe");
        string movie = Path.Combine(fixtures, "sample-clip.mp4");
        Console.WriteLine("Preparing real media fixtures…");
        RunTool(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "1500k", "-c:a", "aac", movie]);
        RunTool(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", movie, "-c", "copy", "-hls_time", "2", "-hls_list_size", "0", Path.Combine(fixtures, "master.m3u8")]);
        byte[] transfer = new byte[24 * 1024 * 1024];
        new Random(42).NextBytes(transfer);
        File.WriteAllBytes(Path.Combine(fixtures, "transfer-test.bin"), transfer);
        File.WriteAllText(Path.Combine(fixtures, "release-notes.txt"), "FM Download Manager 1.0.0 acceptance test.\nThis is an actual downloaded test file.\n");
        File.WriteAllText(Path.Combine(fixtures, "captions.vtt"), "WEBVTT\n\n00:00:00.000 --> 00:00:03.000\nSample clip with embedded captions.\n\n00:00:03.000 --> 00:00:06.000\nFM Download Manager acceptance test.\n");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var server = builder.Build();
        server.MapGet("/samples/{name}", async context =>
        {
            string name = Path.GetFileName(context.Request.RouteValues["name"]!.ToString()!);
            string file = Path.Combine(fixtures, name);
            if (!File.Exists(file)) { context.Response.StatusCode = 404; return; }
            byte[] data = await File.ReadAllBytesAsync(file, context.RequestAborted);
            long start = 0, end = data.Length - 1;
            string range = context.Request.Headers.Range.ToString();
            if (range.StartsWith("bytes="))
            {
                var positions = range[6..].Split('-');
                start = long.Parse(positions[0]);
                end = positions[1].Length == 0 ? end : Math.Min(end, long.Parse(positions[1]));
                context.Response.StatusCode = 206;
                context.Response.Headers.ContentRange = $"bytes {start}-{end}/{data.Length}";
            }
            context.Response.Headers.ETag = "\"" + Convert.ToHexString(SHA256.HashData(data)) + "\"";
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.ContentLength = end - start + 1;
            context.Response.ContentType = name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : name.EndsWith(".ts") ? "video/mp2t" : "application/octet-stream";
            await context.Response.Body.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)), context.RequestAborted);
        });
        server.StartAsync().GetAwaiter().GetResult();
        string address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        // Render the production XAML controls in memory. No mock progress, fake task list,
        // desktop input, browser control or user-profile data is used for these captures.
        Strings.SetLanguage("en");
        ThemeManager.ApplyThemeMode("dark");
        ThemeManager.ApplyAccentColor("#E50914");
        var settings = new AppSettings { DownloadDirectory = downloads, Language = "en", AppTheme = "dark", SpeedLimitKbps = 1024, ClipboardWatcherEnabled = false, NotificationsEnabled = false };
        var video = new VideoDownloader(ytDlp: () => new ToolLauncher(ytDlp), ffmpeg: () => ffmpeg,
            options: () => new VideoOptions { Container = "mp4", MaxHeight = 720 });
        var manager = new DownloadManager(Path.Combine(root, "data"), downloads,
            uiContext: new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher), videoEngine: video)
        { DefaultConnections = 8, MaxConcurrentDownloads = 2 };
        manager.SpeedLimiter.BytesPerSecond = 1024 * 1024;
        string? scanner = Environment.GetEnvironmentVariable("MCDM_TEST_AV");
        manager.VirusScanEnabled = !string.IsNullOrWhiteSpace(scanner);
        manager.CustomAntivirusPath = scanner;
        manager.CustomAntivirusArgs = "SCAN \"{file}\" /i0 /fa";
        var tokens = new TokenStore(Path.Combine(root, "data", "bridge.token"));
        var bridge = new BridgeServer(manager, tokens);
        bridge.StartAsync(0).GetAwaiter().GetResult();
        var window = new MainWindow(manager, tokens, settings, bridge);
        var reports = new List<string>();
        try
        {
            Console.WriteLine("Testing HTTP downloads and the configured antivirus…");
            var note = manager.Add(new() { Url = address + "/samples/release-notes.txt", FileName = "release-notes.txt" });
            var clip = manager.Add(new() { Url = address + "/samples/sample-clip.mp4", FileName = "sample-clip.mp4" });
            Wait(() => Terminal(note) && Terminal(clip), 90);
            Require(note.Status == DownloadStatus.Completed && clip.Status == DownloadStatus.Completed, "sample downloads failed");
            Require(Hash(note.SavePath) == Hash(Path.Combine(fixtures, "release-notes.txt")), "text file hash mismatch");
            Require(Hash(clip.SavePath) == Hash(movie), "video file hash mismatch");
            reports.Add("PASS direct HTTP downloads: downloaded text and MP4 match their source SHA-256");
            if (manager.VirusScanEnabled)
            {
                Require(note.ScanResult == "passed" && clip.ScanResult == "passed", "real antivirus did not complete successfully");
                reports.Add("PASS real antivirus: configured scanner completed both downloaded files with exit code 0");
            }
            else reports.Add("SKIP real antivirus: MCDM_TEST_AV was not supplied");

            var large = manager.Add(new() { Url = address + "/samples/transfer-test.bin", FileName = "transfer-test.bin", Segments = 8 });
            Console.WriteLine("Testing pause/resume and capturing the production XAML views…");
            Wait(() => large.DownloadedBytes > 3 * 1024 * 1024, 30);
            Require(manager.Pause(large.Id), "pause was refused");
            Wait(() => large.ActiveConnections == 0 && File.Exists(large.SavePath + ".mcdm"), 15);
            Pump(300);
            long pausedAt = large.DownloadedBytes;
            Require(manager.Resume(large.Id), "resume was refused");
            Wait(() => large.DownloadedBytes > pausedAt + 2 * 1024 * 1024, 30);
            Pump(500);
            Capture(window, 1280, 760, Path.Combine(screenshots, "downloads-dark.png"));
            var details = new DownloadDetailWindow(large);
            Capture(details, 760, 680, Path.Combine(screenshots, "download-details.png"));
            ThemeManager.ApplyThemeMode("light");
            settings.AppTheme = "light";
            Pump(200);
            Capture(window, 1280, 760, Path.Combine(screenshots, "downloads-light.png"));
            ThemeManager.ApplyThemeMode("dark");
            settings.AppTheme = "dark";
            Wait(() => Terminal(large), 90);
            Require(large.Status == DownloadStatus.Completed && Hash(large.SavePath) == Hash(Path.Combine(fixtures, "transfer-test.bin")), "resumed transfer hash mismatch");
            reports.Add("PASS pause/resume: 24 MiB, 8 connections, byte-exact final file");

            manager.SpeedLimiter.BytesPerSecond = 0;
            Console.WriteLine("Testing actual HLS media extraction and subtitle muxing…");
            var hls = manager.Add(new() { Kind = DownloadKind.Video, Url = address + "/samples/master.m3u8", FileName = "HLS sample.mp4", WriteSubtitles = true, SubtitleLangs = "en", SubtitleUrl = address + "/samples/captions.vtt" });
            Wait(() => Terminal(hls), 120);
            Require(hls.Status == DownloadStatus.Completed, "HLS pipeline failed: " + hls.Error);
            string probe = RunTool(ffprobe, ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", hls.SavePath]);
            var streams = JsonDocument.Parse(probe).RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString()).ToList();
            Require(streams.Contains("video") && streams.Contains("audio"), "HLS output lost video or audio");
            Require(streams.Contains("subtitle"), "subtitle was not embedded");
            reports.Add("PASS actual yt-dlp + FFmpeg: HLS output contains video, audio and embedded subtitles (verified by ffprobe)");

            var publicFile = manager.Add(new() { Url = "https://raw.githubusercontent.com/dotnet/runtime/main/LICENSE.TXT", FileName = "dotnet-runtime-LICENSE.txt" });
            Wait(() => Terminal(publicFile), 45);
            if (publicFile.Status == DownloadStatus.Completed)
            {
                Require(File.ReadAllText(publicFile.SavePath).Contains("MIT License", StringComparison.OrdinalIgnoreCase), "public license content was unexpected");
                reports.Add("PASS public HTTPS download: dotnet/runtime LICENSE.TXT");
            }
            else throw new Exception("public HTTPS download failed: " + publicFile.Error);
            reports.Add("CAPTURE production WPF XAML, real isolated test downloads; progress and speeds were not fabricated");
            reports.Add("NOT RUN interactive browser test: desktop automation could not verify the Chrome/Brave URL");
            File.WriteAllLines(Path.Combine(root, "results.txt"), reports);
            foreach (string report in reports) Console.WriteLine(report);
        }
        finally
        {
            Task.Run(async () =>
            {
                await manager.DisposeAsync();
                await bridge.DisposeAsync();
                await server.StopAsync();
                await server.DisposeAsync();
            }).GetAwaiter().GetResult();
        }
    }

    private static bool Terminal(DownloadItem item) => item.Status is DownloadStatus.Completed or DownloadStatus.Failed;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static void Wait(Func<bool> condition, int seconds)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Acceptance step timed out");
            Pump(20);
        }
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        } while (clock.ElapsedMilliseconds < milliseconds);
    }
    private static void Capture(Window window, int width, int height, string file)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Pump(180);
        content.InvalidateMeasure();
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }
    private static string RunTool(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(Path.GetFileName(executable)); }
        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new Exception(Path.GetFileName(executable) + ": " + error);
        return output;
    }
}
