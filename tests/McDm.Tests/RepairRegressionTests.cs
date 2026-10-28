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
        await test("repair: automatic antivirus detection and real result semantics", () =>
        {
            string root = tmp(), product = Path.Combine(root, "Kaspersky Lab", "Kaspersky Test");
            Directory.CreateDirectory(product);
            string gui = Path.Combine(product, "avpui.exe"), console = Path.Combine(product, "avp.com");
            File.WriteAllText(gui, "fixture GUI");
            check(AntivirusDetection.Detect(gui, installRoots: new[] { root }) == null, "GUI launcher was treated as a file scanner");
            File.WriteAllText(console, "fixture console");
            string vpn = Path.Combine(root, "Kaspersky Lab", "Kaspersky VPN 99.0");
            Directory.CreateDirectory(vpn);
            string vpnConsole = Path.Combine(vpn, "avp.com"); File.WriteAllText(vpnConsole, "fixture VPN console");
            var detected = AntivirusDetection.Detect(installRoots: new[] { root });
            check(detected?.Kind == "kaspersky" && detected.Executable == console, "installed console was not discovered automatically");
            check(AntivirusDetection.Detect(vpnConsole, installRoots: new[] { root })?.Executable == console, "VPN console was mistaken for an antivirus scanner");
            check(AntivirusDetection.Detect(gui, installRoots: new[] { root })?.Executable == console, "legacy GUI setting was not repaired");
            var command = detected!.ForFile(@"C:\Downloads\a file.ppt");
            check(command.ArgumentList.SequenceEqual(new[] { "SCAN", @"C:\Downloads\a file.ppt", "/i0", "/fa" }), "scanner command or quoting is incorrect");
            check(!AntivirusScanner.ReportsThreat("Threats detected: 0\nNot infected: 1"), "clean scan statistics produced a false threat");
            check(AntivirusScanner.ReportsThreat("Threats detected: 1"), "positive threat count was ignored");
            check(WindowsAntivirus.IsThreat(32768) && !WindowsAntivirus.IsThreat(1), "Windows scan verdict was misinterpreted");
            check(WindowsAntivirus.IsPolicyBlock(0x4000) && !WindowsAntivirus.IsPolicyBlock(0), "Windows policy blocks were misinterpreted");
            using var content = new WindowsAntivirus.FileContent(new MemoryStream("ABCDEF"u8.ToArray()), "fixture.txt");
            IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(128);
            try
            {
                check(content.GetAttribute(2, 8, buffer, out var size) == 0 && size == 8 && System.Runtime.InteropServices.Marshal.ReadInt64(buffer) == 6, "Windows stream length is incorrect");
                check(content.Read(3, 3, buffer, out var read) == 0 && read == 3 && System.Runtime.InteropServices.Marshal.ReadByte(buffer) == (byte)'D', "Windows scanner stream offset was ignored");
                check(content.Read(100, 3, buffer, out read) == 0 && read == 0, "Windows stream EOF was misreported");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
            return Task.CompletedTask;
        });

        await test("repair: multipart names group only explicit sibling volumes", () =>
        {
            var first = MultipartFiles.Describe("Game.part01.rar", @"C:\Downloads") ?? throw new Exception("multipart name was not recognized");
            var second = MultipartFiles.Describe("Game.part02.rar", @"c:\downloads");
            check(first.Key == second?.Key && first.Label == "Game.rar", "RAR sibling parts did not group");
            check(first.Key != MultipartFiles.Describe("Game.part02.zip", @"C:\Downloads")?.Key, "different archive formats grouped");
            check(first.Key != MultipartFiles.Describe("Game.part02.rar", @"C:\Other")?.Key, "different target folders grouped");
            check(MultipartFiles.Describe("Game.7z.001", @"C:\Downloads")?.Key == MultipartFiles.Describe("Game.7z.002", @"C:\Downloads")?.Key, "split 7z volumes did not group");
            check(MultipartFiles.Describe("Game.r00", @"C:\Downloads")?.Key == MultipartFiles.BaseArchive("Game.rar", @"C:\Downloads")?.Key, "legacy first archive did not match volumes");
            check(CategoryHelper.GetCategorySubfolder("Game.r00", null, DownloadKind.File) == "Compressed"
                && CategoryHelper.GetCategorySubfolder("Game.7z.001", null, DownloadKind.File) == "Compressed",
                "automatic folders would separate archive volumes from their base archive");
            check(MultipartFiles.Describe("Game.rar", @"C:\Downloads") == null && MultipartFiles.Describe("fg-01.bin", @"C:\Downloads") == null, "unrelated single files were guessed into a group");
            return Task.CompletedTask;
        });

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

        foreach (string mode in new[] { "changed-size", "unknown-size", "interrupted", "empty", "html", "unsolicited-partial", "not-found", "cancelled" })
        await test($"repair: ignored Range safely restarts a full GET ({mode})", async () =>
        {
            byte[] old = "old-file-version"u8.ToArray();
            byte[] expected = mode == "empty" ? Array.Empty<byte>() : "ABCDEFGH"u8.ToArray();
            int calls = 0, fullGets = 0, partialWrites = 0;
            using var cancellation = new CancellationTokenSource();
            using var http = new HttpClient(new Handler((request, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    var probe = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(old[..1]) };
                    probe.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, old.Length);
                    probe.Headers.ETag = new EntityTagHeaderValue("\"old\"");
                    return probe;
                }
                if (request.Headers.Range is { } range)
                {
                    var part = range.Ranges.Single();
                    if (Interlocked.Increment(ref partialWrites) == 1)
                    {
                        int from = (int)part.From!.Value, to = (int)part.To!.Value;
                        var valid = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(old[from..(to + 1)]) };
                        valid.Headers.ETag = new EntityTagHeaderValue("\"old\"");
                        valid.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, old.Length);
                        return valid;
                    }
                    if (mode == "cancelled") cancellation.Cancel();
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(old) };
                }
                check(request.Headers.IfRange == null, "full restart retained an If-Range validator");
                int attempt = Interlocked.Increment(ref fullGets);
                if (mode == "not-found") return new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
                HttpContent content = mode == "interrupted" && attempt == 1 ? new StreamContent(new BrokenStream())
                    : mode == "unknown-size" ? new StreamContent(new NonSeekableStream()) : new ByteArrayContent(expected);
                var response = new HttpResponseMessage(mode == "unsolicited-partial" ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content };
                response.Headers.ETag = new EntityTagHeaderValue("\"new\"");
                if (mode == "html") response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return response;
            }));
            var item = new DownloadItem { Directory = tmp(), Request = new() { Url = "https://example.test/document", FileName = mode == "html" ? "document.pdf" : "document.bin" } };
            bool accepted = mode is "changed-size" or "unknown-size" or "interrupted" or "empty";
            try
            {
                await new SegmentedDownloader(http, new EngineOptions { MinSplitBytes = 1, MinChunkBytes = 4, MaxChunkBytes = 4 }).RunAsync(item, 2, cancellation.Token);
                check(accepted, "invalid full-download response was finalized");
                check(File.ReadAllBytes(item.SavePath).SequenceEqual(expected), "range fallback mixed entities or appended retry bytes");
                check(!item.SupportsRanges && item.TotalBytes == expected.Length, "full-download size or range capability is stale");
                check(!File.Exists(item.SavePath + ".mcdm"), "range checkpoint survived successful fallback");
            }
            catch (FatalDownloadException)
            { check(!accepted, "valid full GET fallback was rejected"); check(!File.Exists(item.SavePath), "invalid response produced a completed file"); }
            catch (OperationCanceledException) when (mode == "cancelled")
            { check(!File.Exists(item.SavePath), "cancelled range download was finalized"); }
            check(fullGets == (mode == "cancelled" ? 0 : mode == "interrupted" ? 2 : 1), "fallback repeated unexpectedly or ignored cancellation");
        });

        foreach (string mode in new[] { "capped", "extended", "missing", "wrong-start", "wrong-total", "wrong-unit" })
        await test($"repair: Content-Range validation ({mode})", async () =>
        {
            byte[] payload = "ABCDEFGHIJKLMNOP"u8.ToArray();
            int calls = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                bool probe = Interlocked.Increment(ref calls) == 1;
                var range = request.Headers.Range!.Ranges.Single();
                int from = (int)range.From!.Value, to = (int)range.To!.Value;
                if (!probe && mode == "capped") to = Math.Min(to, from + 1);
                if (!probe && mode == "extended") to = payload.Length - 1;
                if (!probe && mode == "wrong-start") from++;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    { Content = new ByteArrayContent(payload[from..(to + 1)]) };
                if (probe || mode != "missing")
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to,
                        !probe && mode == "wrong-total" ? payload.Length + 1 : payload.Length);
                if (!probe && mode == "wrong-unit") response.Content.Headers.ContentRange!.Unit = "items";
                return response;
            }));
            var item = new DownloadItem { Directory = tmp(), Request = new() { Url = "https://example.test/range.bin" } };
            bool accepted = mode is "capped" or "extended";
            try
            {
                await new SegmentedDownloader(http, new EngineOptions { MinSplitBytes = 1, MinChunkBytes = 4, MaxChunkBytes = 4 }).RunAsync(item, 2, CancellationToken.None);
                check(accepted, "unsafe range response was accepted");
                check(File.ReadAllBytes(item.SavePath).SequenceEqual(payload), "valid server subrange/tail corrupted the output");
            }
            catch (FatalDownloadException)
            {
                check(!accepted, "valid capped/extended server range was rejected");
                check(!File.Exists(item.SavePath), "unsafe response was finalized");
            }
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

        await test("repair: legacy bridge discovery and stable no-loop", () =>
        {
            // Exact numeric-only comparison shipped in 3.7.2.
            check(new Version("3.8.0") > new Version("3.7.2"), "old client cannot discover bridge");
            check(UpdateChecker.IsNewer("3.8.0", "1.0.0"), "previous stable client cannot discover refreshed build");
            const string json = """
            {"tag_name":"v3.8.0","body":"Compatibility update\nFM_STABLE_TARGET=1.0.0","assets":[{"name":"FM-Legacy-Update.exe","browser_download_url":"https://github.com/Ferdi-krbk/fm-download-manager/releases/download/v3.8.0/FM-Legacy-Update.exe"}]}
            """;
            var old = UpdateChecker.ParseRelease(json, "3.7.2");
            check(old?.HasUpdate == true && old.LatestVersion == "1.0.0", "bridge target was not mapped to stable");
            // v3.8.1 is also discoverable by the first stable parser, which only reserves v3.8.0.
            check(UpdateChecker.IsNewer("3.8.1", "1.0.0"), "initial stable build cannot discover the refreshed bridge");
            var refreshed = json.Replace("v3.8.0", "v3.8.1").Replace("FM_STABLE_TARGET=1.0.0", "FM_STABLE_TARGET=1.0.0\\nFM_STABLE_BUILD=3");
            check(UpdateChecker.ParseRelease(refreshed, "1.0.0", 1)?.HasUpdate == true, "same-version build refresh was not offered");
            check(UpdateChecker.ParseRelease(refreshed, "1.0.0", 3)?.HasUpdate == false, "same build creates an update loop");
            check(UpdateChecker.ParseRelease(refreshed, "1.0.0", 4)?.HasUpdate == false, "older stable build was offered as a downgrade");
            check(UpdateChecker.ParseRelease(refreshed, "2.0.0", 1)?.HasUpdate == false, "build marker overrode a newer product version");
            check(UpdateChecker.CurrentStableBuild == 6, "stable-build assembly metadata missing");
            var official = refreshed.Replace("v3.8.1", "v1.0.0").Replace("FM_STABLE_BUILD=3", "FM_STABLE_BUILD=4");
            check(UpdateChecker.ParseRelease(official, "1.0.0", 3)?.LatestBuild == 4, "official release build metadata was lost");
            check(UpdateChecker.ParseRelease(official, "1.0.0", 4)?.HasUpdate == false, "official release creates an update loop");
            var hotfix = refreshed.Replace("FM_STABLE_BUILD=3", "FM_STABLE_BUILD=5");
            check(UpdateChecker.ParseRelease(hotfix, "1.0.0", 4)?.HasUpdate == true, "installed build 4 cannot discover antivirus hotfix");
            check(UpdateChecker.ParseRelease(hotfix, "1.0.0", 5)?.HasUpdate == false, "installed hotfix creates an update loop");
            check(UpdateChecker.ParseRelease(hotfix, "1.0.0", 6)?.HasUpdate == false, "hotfix downgrades a newer build");
            var current = hotfix.Replace("FM_STABLE_BUILD=5", "FM_STABLE_BUILD=6");
            check(UpdateChecker.ParseRelease(current, "1.0.0", 5)?.HasUpdate == true, "installed build 5 cannot discover this update");
            check(UpdateChecker.ParseRelease(current, "1.0.0", 6)?.HasUpdate == false, "current build creates an update loop");
            check(UpdateChecker.ParseRelease(current, "1.0.0", 7)?.HasUpdate == false, "current update downgrades a newer build");
            var installed = UpdateChecker.ParseRelease(json, "1.0.0");
            check(installed?.HasUpdate == false, "bridge creates an endless update loop");
            check(UpdateChecker.ParseRelease(json.Replace("FM_STABLE_TARGET=1.0.0", "FM_STABLE_TARGET=garbage"), "1.0.0") == null, "invalid target accepted");
            check(UpdateChecker.ParseRelease(json.Replace("FM_STABLE_TARGET=1.0.0", "FM_STABLE_TARGET=3.8.0"), "1.0.0") == null, "legacy target accepted");
            return Task.CompletedTask;
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
