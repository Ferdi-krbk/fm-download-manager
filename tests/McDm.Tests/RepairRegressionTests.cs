using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McDm.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace McDm.Tests;

public static class RepairRegressionTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check, Func<string> tmp)
    {
        foreach (bool unknown in new[] { false, true })
        await test($"repair: interrupted non-range stream restarts byte-exact (unknown length={unknown})", async () =>
        {
            int calls = 0;
            using var http = new HttpClient(new Handler((_, _) =>
            {
                calls++;
                HttpContent content = calls == 2 ? new StreamContent(new BrokenStream())
                    : unknown ? new StreamContent(new NonSeekableStream()) : new ByteArrayContent("ABCDEFGH"u8.ToArray());
                if (!unknown) content.Headers.ContentLength = 8;
                else content.Headers.ContentLength = null;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }));
            var item = new DownloadItem { Directory = tmp(), Request = new() { Url = "https://example.test/file" } };
            await new SegmentedDownloader(http).RunAsync(item, 1, CancellationToken.None);
            check(File.ReadAllBytes(item.SavePath).SequenceEqual("ABCDEFGH"u8.ToArray()), "retry duplicated or left stale bytes");
        });

        await test("repair: resume validators are strong, present and case-sensitive", () =>
        {
            check(!SegmentedDownloader.ValidatorsMatch("\"ABC\"", "\"abc\""), "ETags are case sensitive");
            check(!SegmentedDownloader.ValidatorsMatch("\"ABC\"", null), "missing validator cannot resume");
            check(!SegmentedDownloader.ValidatorsMatch(null, null), "two missing validators cannot resume");
            check(!SegmentedDownloader.ValidatorsMatch("W/\"ABC\"", "W/\"ABC\""), "weak ETags cannot resume");
            check(SegmentedDownloader.ValidatorsMatch("\"ABC\"", "\"ABC\""), "unchanged strong ETag must resume");
            return Task.CompletedTask;
        });

        await test("repair: changed entity during transfer is rejected and If-Range is sent", async () =>
        {
            int calls = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                calls++;
                if (calls > 1) check(request.Headers.IfRange?.EntityTag?.Tag == "\"original\"", "missing If-Range guard");
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(calls == 1 ? [65] : "ABCDEFGH"u8.ToArray()) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, calls == 1 ? 0 : 7, 8);
                response.Headers.ETag = new EntityTagHeaderValue(calls == 1 ? "\"original\"" : "\"changed\"");
                return response;
            }));
            var item = new DownloadItem { Directory = tmp(), Request = new() { Url = "https://example.test/file" } };
            try { await new SegmentedDownloader(http).RunAsync(item, 1, CancellationToken.None); throw new Exception("changed entity was accepted"); }
            catch (FatalDownloadException) { check(!File.Exists(item.SavePath), "changed file was finalized"); }
        });

        await test("repair: invalid resume coverage restarts instead of finalizing missing bytes", async () =>
        {
            var dir = tmp();
            var path = Path.Combine(dir, "test.bin");
            File.WriteAllBytes(path + ".mcdm-part", new byte[8]);
            File.WriteAllText(path + ".mcdm", """{"Total":8,"Validator":"\"v1\"","Ranges":true,"Chunks":[]}""");
            using var http = new HttpClient(new Handler((request, _) =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                int from = (int)range.From!.Value, to = (int)range.To!.Value;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("ABCDEFGH"u8.ToArray()[from..(to + 1)]) };
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, 8);
                return response;
            }));
            var item = new DownloadItem { Directory = dir, SavePath = path, Request = new() { Url = "https://example.test/file" } };
            await new SegmentedDownloader(http).RunAsync(item, 1, CancellationToken.None);
            check(File.ReadAllBytes(path).SequenceEqual("ABCDEFGH"u8.ToArray()), "invalid checkpoint trusted");
        });

        await test("repair: queue respects retry deadline and shutdown cancels pending retries", async () =>
        {
            int calls = 0;
            using var http = new HttpClient(new Handler((_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException("network unavailable");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent("retry ok"u8.ToArray()) };
            }));
            var manager = new DownloadManager(tmp(), tmp(), new SegmentedDownloader(http, new() { MaxRetries = 0 }));
            var clock = Stopwatch.StartNew();
            var item = manager.Add(new() { Url = "https://example.test/file" });
            await Until(() => item.AutoRetryCount == 1, TimeSpan.FromSeconds(2));
            await Task.Delay(250);
            check(calls == 1 && item.Status == DownloadStatus.Queued, "retry bypassed deadline");
            await Until(() => item.Status == DownloadStatus.Completed, TimeSpan.FromSeconds(8));
            check(clock.Elapsed.TotalSeconds >= 4.8, "retry started too soon");
            await manager.DisposeAsync();

            using var failedHttp = new HttpClient(new Handler((_, _) => throw new HttpRequestException("network unavailable")));
            var pending = new DownloadManager(tmp(), tmp(), new SegmentedDownloader(failedHttp, new() { MaxRetries = 0 }));
            var queued = pending.Add(new() { Url = "https://example.test/pending" });
            await Until(() => queued.AutoRetryCount == 1, TimeSpan.FromSeconds(2));
            await pending.DisposeAsync();
            check(queued.Status == DownloadStatus.Paused, "shutdown left scheduled retry queued");
        });

        await test("repair: cookie domain, path, security and expiry are enforced", () =>
        {
            var request = new DownloadRequest { Url = "https://video.test/media", CookieHeader = "wrong=raw", Cookies =
            [
                new() { Name = "video", Value = "secret", Domain = "video.test", Path = "/", Secure = true },
                new() { Name = "subtitle", Value = "sub", Domain = "subs.test", Path = "/tracks" },
                new() { Name = "expired", Value = "old", Domain = "subs.test", ExpirationDate = 1 },
                new() { Name = "parent", Value = "ok", Domain = ".subs.test", Path = "/" },
            ] };
            string header = RequestCookies.HeaderFor(request, new("https://subs.test/tracks/en.vtt"))!;
            check(header.Contains("subtitle=sub") && !header.Contains("video=") && !header.Contains("expired=") && !header.Contains("wrong="), "cross-domain cookie leakage");
            check(!RequestCookies.HeaderFor(request, new("https://subs.test/tracks-other/a"))!.Contains("subtitle="), "cookie path boundary ignored");
            check(RequestCookies.HeaderFor(request, new("http://video.test/media")) == null, "secure cookie downgraded");
            check(RequestCookies.HeaderFor(request, new("https://evilvideo.test/media")) == null, "suffix domain confusion");
            check(RequestCookies.HeaderFor(request, new("https://cdn.subs.test/media")) == "parent=ok", "domain cookie missing");
            return Task.CompletedTask;
        });

        await test("repair: history encrypts and migrates cookies, terminal history omits them, backup recovers", async () =>
        {
            if (!OperatingSystem.IsWindows()) return; // Windows DPAPI intentionally has no plaintext fallback.
            var dir = tmp();
            string secret = "private-session-" + Guid.NewGuid().ToString("N");
            var legacy = new DownloadItem { Status = DownloadStatus.Paused, Request = new() { Url = "https://video.test/file", CookieHeader = "sid=" + secret } };
            File.WriteAllText(Path.Combine(dir, "downloads.json"), JsonSerializer.Serialize(new[] { legacy }));
            using (var manager = new DownloadManager(dir, tmp()))
            {
                check(manager.Find(legacy.Id)!.Request.CookieHeader!.Contains(secret), "legacy session lost");
                foreach (string name in new[] { "downloads.json", "downloads.json.bak" })
                {
                    string json = File.ReadAllText(Path.Combine(dir, name));
                    check(!json.Contains(secret) && json.Contains("ProtectedCredentials"), "plaintext cookie persisted");
                }
            }
            File.WriteAllText(Path.Combine(dir, "downloads.json"), "{broken");
            using (var manager = new DownloadManager(dir, tmp()))
            {
                var recovered = manager.Find(legacy.Id)!;
                check(recovered.Request.CookieHeader!.Contains(secret), "encrypted backup could not be recovered");
                recovered.Status = DownloadStatus.Completed;
            }
            check(!File.ReadAllText(Path.Combine(dir, "downloads.json")).Contains("ProtectedCredentials"), "terminal job retained credentials");
            await Task.CompletedTask;
        });

        await test("repair: HTTP redirects use destination-scoped cookies, including subtitle requests", async () =>
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            await using var server = builder.Build();
            int port = 0;
            string? originalCookie = null;
            server.MapGet("/redirect", context =>
            {
                originalCookie = context.Request.Headers.Cookie;
                context.Response.Redirect($"http://localhost:{port}/subtitle");
                return Task.CompletedTask;
            });
            server.MapGet("/subtitle", context => context.Response.WriteAsync(context.Request.Headers.Cookie.ToString()));
            await server.StartAsync();
            string address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            port = new Uri(address).Port;
            var request = new DownloadRequest { Url = address + "/redirect", Cookies =
            [
                new() { Name = "session", Value = "video-secret", Domain = "127.0.0.1", Path = "/" },
                new() { Name = "session", Value = "subtitle-secret", Domain = "localhost", Path = "/subtitle" },
            ] };
            using var client = RequestCookies.CreateClient(request, TimeSpan.FromSeconds(5));
            string received = await client.GetStringAsync(request.Url);
            check(originalCookie == "session=video-secret", "original host did not receive its own cookie");
            check(received == "session=subtitle-secret", "redirect leaked source credentials or lost target credentials");
            request.Cookies = null;
            request.CookieHeader = "raw=private";
            using var legacy = RequestCookies.CreateClient(request, TimeSpan.FromSeconds(5));
            check(await legacy.GetStringAsync(request.Url) == "", "legacy raw cookie crossed host boundary");
        });

        await test("repair: media probing rejects an untrusted HTTPS certificate", async () =>
        {
            using var rsa = RSA.Create(2048);
            var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            await using var server = builder.Build();
            server.MapGet("/master.m3u8", () => "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=1280x720\nvideo.m3u8");
            await server.StartAsync();
            string address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            try
            {
                await StreamProbeService.TryProbeHlsAsync(address + "/master.m3u8", null, null, null, null, CancellationToken.None);
                throw new Exception("untrusted certificate was accepted");
            }
            catch (HttpRequestException) { }
        });

        await test("repair: scanner output is drained and timed-out processes are terminated", async () =>
        {
            ProcessStartInfo Scanner(string mode)
            {
                var start = new ProcessStartInfo("dotnet");
                start.ArgumentList.Add(typeof(RepairRegressionTests).Assembly.Location);
                start.ArgumentList.Add("--scanner-fixture");
                start.ArgumentList.Add(mode);
                return start;
            }
            check(await AntivirusScanner.RunAsync(Scanner("noisy"), TimeSpan.FromSeconds(10), CancellationToken.None) == 0, "scanner output caused a deadlock");
            string pidFile = Path.Combine(tmp(), "scanner.pid");
            var slow = Scanner("wait"); slow.ArgumentList.Add(pidFile);
            try { await AntivirusScanner.RunAsync(slow, TimeSpan.FromSeconds(2), CancellationToken.None); throw new Exception("scanner timeout ignored"); }
            catch (TimeoutException) { }
            check(File.Exists(pidFile), "scanner fixture never started");
            int pid = int.Parse(File.ReadAllText(pidFile));
            try { using var process = Process.GetProcessById(pid); check(process.HasExited, "timed-out scanner still running"); }
            catch (ArgumentException) { }
        });

        await test("repair: stable release epoch and HLS upgrade behave correctly", () =>
        {
            check(UpdateChecker.IsNewer("1.0.0", "3.7.2"), "legacy to stable migration missing");
            check(!UpdateChecker.IsNewer("3.7.2", "1.0.0"), "stable offered obsolete legacy release");
            check(UpdateChecker.IsNewer("1.0.1", "1.0.0"), "stable updates broken");
            check(StreamProbeService.UpgradeToMasterHlsUrl("https://cdn.test/hls/sublist_vid1.m3u8?token=abc") == "https://cdn.test/hls/master.m3u8?token=abc", "HLS upgrade lost path/query");
            return Task.CompletedTask;
        });

        await test("repair: audio and subtitle labels respect Turkish and English UI cultures", () =>
        {
            var previous = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                check(StreamProbeService.FormatSubtitleLabel("tr", false) == "Turkish (Türkçe) Subtitles", "English subtitle label incorrect");
                check(StreamProbeService.FormatAudioLabel("tr", isOriginal: false) == "Turkish dub", "English dub label incorrect");
                check(StreamProbeService.FormatAudioLabel("en", isOriginal: true).StartsWith("Original audio"), "English original audio label incorrect");
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
                check(StreamProbeService.FormatSubtitleLabel("tr", false) == "Türkçe Altyazı", "Turkish subtitle label incorrect");
                check(StreamProbeService.FormatAudioLabel("tr", isOriginal: false) == "Türkçe Dublaj", "Turkish dub label incorrect");
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
            return Task.CompletedTask;
        });

        await test("repair: tool checksum failure preserves the installed binary", async () =>
        {
            string target = Path.Combine(tmp(), "tool.exe");
            File.WriteAllText(target, "installed binary");
            using var http = new HttpClient(new Handler((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent("downloaded binary"u8.ToArray()) }));
            try { await VerifiedToolDownload.SaveAsync(http, "https://example.test/tool.exe", new string('0', 64), target); throw new Exception("bad hash accepted"); }
            catch (CryptographicException) { }
            check(File.ReadAllText(target) == "installed binary", "installed tool was removed before verification");
            check(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp").Length == 0, "unverified tool left on disk");
            await VerifiedToolDownload.SaveAsync(http, "https://example.test/tool.exe", Convert.ToHexString(SHA256.HashData("downloaded binary"u8)), target);
            check(File.ReadAllText(target) == "downloaded binary", "verified tool was not installed");
        });

        await test("repair: torrent registration is released after cancellation and actual output path is reported", async () =>
        {
            var dir = tmp();
            string torrent = Path.Combine(dir, "source.torrent");
            byte[] metadata = Encoding.ASCII.GetBytes("d4:infod6:lengthi3e4:name11:payload.bin12:piece lengthi16384e6:pieces20:")
                .Concat(SHA1.HashData("abc"u8)).Concat("ee"u8.ToArray()).ToArray();
            File.WriteAllBytes(torrent, metadata);
            File.WriteAllBytes(Path.Combine(dir, "payload.bin"), "abc"u8.ToArray());
            using var engine = new TorrentDownloader(engineSettings: new MonoTorrent.Client.EngineSettingsBuilder
            {
                CacheDirectory = tmp(), DhtEndPoint = null, ListenEndPoints = new(),
                AllowLocalPeerDiscovery = false, AllowPortForwarding = false, AutoSaveLoadFastResume = true,
            }.ToSettings());
            var item = new DownloadItem { Directory = dir, FileName = "wrong.torrent", Request = new() { Url = torrent, Kind = DownloadKind.Torrent } };
            try { await engine.RunAsync(item, new CancellationToken(true)); }
            catch (OperationCanceledException) { }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await engine.RunAsync(item, deadline.Token);
            check(item.FileName == "payload.bin", "torrent kept display/metafile name");
            check(item.SavePath == Path.Combine(dir, "payload.bin") && File.Exists(item.SavePath), "torrent reported a nonexistent output");
            check(item.ContentFiles.SequenceEqual(new[] { item.SavePath }), "torrent content paths missing");
            check(File.ReadAllBytes(item.SavePath).SequenceEqual("abc"u8.ToArray()), "torrent data was changed");
        });
    }

    private static async Task Until(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) throw new TimeoutException("Regression condition not met");
            await Task.Delay(20);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request, cancellationToken));
    }
    private sealed class BrokenStream() : MemoryStream("ABCDEFGH"u8.ToArray())
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= 4) throw new IOException("simulated network reset");
            return base.ReadAsync(buffer[..Math.Min(4, buffer.Length)], cancellationToken);
        }
    }
    private sealed class NonSeekableStream() : MemoryStream("ABCDEFGH"u8.ToArray())
    {
        public override bool CanSeek => false;
    }
}
