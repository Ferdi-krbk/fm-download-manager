using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using McDm.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McDm.Tests;

public static class ReleaseSecurityTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check, Func<string> tmp)
    {
        using var key = RSA.Create(3072);
        using var wrongKey = RSA.Create(3072);
        byte[] payload = "harmless update fixture, never executed"u8.ToArray();
        int build = UpdateChecker.CurrentStableBuild;
        const string prefix = "https://github.com/Ferdi-krbk/fm-download-manager/releases/download/v3.8.1/";
        var update = new UpdateInfo { LatestVersion = "1.0.0", LatestBuild = build, AssetName = "FM-Legacy-Update.exe",
            DownloadUrl = prefix + "FM-Legacy-Update.exe", ManifestUrl = prefix + "update-manifest.json", SignatureUrl = prefix + "update-manifest.sig" };
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new SignedUpdates.Manifest("1.0.0", build, update.AssetName, Convert.ToHexString(SHA256.HashData(payload)), payload.Length));
        byte[] signature = key.SignData(metadata, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        string publicKey = key.ExportSubjectPublicKeyInfoPem();
        async Task Reject(Func<Task> operation)
        {
            try { await operation(); }
            catch (Exception error) when (error is CryptographicException or HttpRequestException or JsonException) { return; }
            throw new Exception("Unsafe update was accepted");
        }
        await test("security: signed updater downloads exact verified bytes and marks origin", async () =>
        {
            using var http = new HttpClient(new UpdateHandler(update, metadata, signature, payload));
            string result = await SignedUpdates.DownloadVerifiedAsync(http, update, tmp(), publicKey);
            check(File.ReadAllBytes(result).SequenceEqual(payload), "verified bytes differ");
            if (OperatingSystem.IsWindows()) check(File.ReadAllText(result + ":Zone.Identifier").Contains("ZoneId=3"), "verified update lacks internet mark");
        });
        await test("security: altered manifest and wrong signing key are rejected before exe download", async () =>
        {
            foreach (var sig in new[] { signature.Select((b, n) => n == 0 ? (byte)(b ^ 1) : b).ToArray(), wrongKey.SignData(metadata, HashAlgorithmName.SHA256, RSASignaturePadding.Pss) })
            {
                var handler = new UpdateHandler(update, metadata, sig, payload);
                using var http = new HttpClient(handler);
                await Reject(() => SignedUpdates.DownloadVerifiedAsync(http, update, tmp(), publicKey));
                check(handler.ExeRequests == 0, "untrusted manifest reached package download");
            }
            byte[] altered = metadata.ToArray(); altered[^2] ^= 1;
            using var alteredHttp = new HttpClient(new UpdateHandler(update, altered, signature, payload));
            await Reject(() => SignedUpdates.DownloadVerifiedAsync(alteredHttp, update, tmp(), publicKey));
        });
        await test("security: changed truncated or oversized update bytes are removed", async () =>
        {
            foreach (byte[] bad in new[] { payload.Select(b => (byte)(b ^ 1)).ToArray(), payload[..^1], payload.Concat(new byte[] { 1 }).ToArray() })
            {
                string root = tmp();
                using var http = new HttpClient(new UpdateHandler(update, metadata, signature, bad));
                await Reject(() => SignedUpdates.DownloadVerifiedAsync(http, update, root, publicKey));
                check(!Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).Any(), "rejected executable remains on disk");
            }
        });
        await test("security: signed version build asset mismatch and replay are rejected", async () =>
        {
            foreach (var manifest in new[] {
                new SignedUpdates.Manifest("1.0.1", build, update.AssetName, new string('a',64), payload.Length),
                new SignedUpdates.Manifest("1.0.0", build - 1, update.AssetName, new string('a',64), payload.Length),
                new SignedUpdates.Manifest("1.0.0", build, "other.exe", new string('a',64), payload.Length),
                new SignedUpdates.Manifest("1.0.0", build, update.AssetName, new string('a',64), long.MaxValue) })
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
                using var http = new HttpClient(new UpdateHandler(update, bytes, key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), payload));
                await Reject(() => SignedUpdates.DownloadVerifiedAsync(http, update, tmp(), publicKey));
            }
            var old = new UpdateInfo { LatestVersion = "1.0.0", LatestBuild = build - 1, AssetName = update.AssetName };
            byte[] replay = JsonSerializer.SerializeToUtf8Bytes(new SignedUpdates.Manifest("1.0.0", build - 1, update.AssetName, new string('a',64), payload.Length));
            await Reject(() => Task.FromResult(SignedUpdates.VerifyManifest(replay, key.SignData(replay, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), old, publicKey)));
        });
        await test("security: untrusted release hosts HTTP redirects and missing signatures fail closed", async () =>
        {
            foreach (string address in new[] { update.DownloadUrl.Replace("https:", "http:"), update.DownloadUrl.Replace("github.com/", "github.com.evil.test/"), update.DownloadUrl.Replace("Ferdi-krbk/", "other/"), update.DownloadUrl + "?redirect=1" })
            {
                var bad = new UpdateInfo { DownloadUrl = address, ManifestUrl = update.ManifestUrl, SignatureUrl = update.SignatureUrl, AssetName = update.AssetName };
                await Reject(() => Task.Run(() => SignedUpdates.ValidateAddresses(bad)));
            }
            foreach (var handler in new[] {
                new UpdateHandler(update, metadata, Array.Empty<byte>(), payload),
                new UpdateHandler(update, new byte[65537], signature, payload),
                new UpdateHandler(update, metadata, signature, payload) { RedirectToHttp = true } })
            {
                using var http = new HttpClient(handler);
                await Reject(() => SignedUpdates.DownloadVerifiedAsync(http, update, tmp(), publicKey));
                check(handler.ExeRequests == 0, "invalid metadata reached exe download");
            }
        });
        await test("security: Windows internet marks preserve bytes and only touch actual torrent outputs", () =>
        {
            if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
            string root = tmp();
            string folder = Path.Combine(root, "torrent"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "part.bin"); File.WriteAllBytes(file, payload);
            string unrelated = Path.Combine(folder, "personal.txt"); File.WriteAllText(unrelated, "unrelated fixture");
            DownloadOrigin.MarkCompleted(new DownloadItem { SavePath = folder, ContentFiles = new() { file } });
            check(!File.Exists(unrelated + ":Zone.Identifier"), "unrelated destination file was marked");
            check(File.ReadAllBytes(file).SequenceEqual(payload), "origin marking changed file bytes");
            check(File.ReadAllText(file + ":Zone.Identifier").Contains("ZoneId=3"), "nested file lacks mark");
            File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            DownloadOrigin.MarkFile(file);
            check(File.ReadAllText(file + ":Zone.Identifier").Contains("ZoneId=4"), "restricted mark was downgraded");
            check(!DownloadOrigin.MarkFile(Path.Combine(root, "missing")), "missing file was marked");
            return Task.CompletedTask;
        });
        if (new ToolPaths().YtDlpPath is { } ytDlp)
        await test("security: actual yt-dlp rejects a self-signed HTTPS video server", async () =>
        {
            using var certificateKey = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", certificateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate)));
            await using var server = builder.Build();
            int hits = 0;
            server.MapGet("/clip.mp4", () => { Interlocked.Increment(ref hits); return "not reached"; });
            await server.StartAsync();
            string address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            // Positive transport control is limited to this freshly generated loopback fixture.
            using (var control = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }))
                check(await control.GetStringAsync(address + "/clip.mp4") == "not reached", "TLS fixture transport is broken");
            Interlocked.Exchange(ref hits, 0);
            string root = tmp();
            var arguments = VideoDownloader.BuildArguments(address + "/clip.mp4", new VideoOptions(), root, root, null, null);
            arguments.InsertRange(arguments.IndexOf("--"), new[] { "--skip-download", "--extractor-retries", "0", "--retries", "0" });
            var start = new ProcessStartInfo(ytDlp) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await process.WaitForExitAsync(deadline.Token);
                string error = await stderr; await stdout;
                check(process.ExitCode != 0 && hits == 0 && error.Contains("certificate", StringComparison.OrdinalIgnoreCase),
                    $"yt-dlp TLS rejection was not confirmed (exit {process.ExitCode}, HTTP hits {hits}): {error}");
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        });
        else Console.WriteLine("SKIP actual yt-dlp TLS check: tool is not installed");
    }

    private sealed class UpdateHandler(UpdateInfo update, byte[] metadata, byte[] signature, byte[] payload) : HttpMessageHandler
    {
        public int ExeRequests { get; private set; }
        public bool RedirectToHttp { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (RedirectToHttp)
            {
                var redirected = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
                redirected.Headers.Location = new Uri("http://example.test/update");
                return Task.FromResult(redirected);
            }
            string url = request.RequestUri!.ToString();
            byte[] bytes = url == update.ManifestUrl ? metadata : url == update.SignatureUrl ? signature : payload;
            if (url == update.DownloadUrl) ExeRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
        }
    }
}
