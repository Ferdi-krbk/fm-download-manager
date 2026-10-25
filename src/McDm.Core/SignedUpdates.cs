using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McDm.Core;

/// <summary>Fail-closed update verification. This does not replace Windows Authenticode signing.</summary>
public static class SignedUpdates
{
    public sealed record Manifest(string Version, int Build, string Asset, string Sha256, long Size);

    internal static string PublicKey
    {
        get
        {
            using var stream = typeof(SignedUpdates).Assembly.GetManifestResourceStream("McDm.Core.update-public-key.pem")
                ?? throw new CryptographicException("Update verification key is missing");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    internal static Manifest VerifyManifest(byte[] bytes, byte[] signature, UpdateInfo update, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Update manifest signature is invalid");
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new CryptographicException("Update manifest is missing");
        if (manifest.Version != update.LatestVersion || manifest.Build != update.LatestBuild
            || manifest.Asset != update.AssetName || !Regex.IsMatch(manifest.Asset ?? "", @"^[A-Za-z0-9._-]+\.exe$")
            || !Regex.IsMatch(manifest.Sha256 ?? "", "^[a-fA-F0-9]{64}$")
            || manifest.Size <= 0 || manifest.Size > 1024L * 1024 * 1024)
            throw new CryptographicException("Update manifest does not match the requested version, build or asset");
        if (UpdateChecker.IsNewer(UpdateChecker.CurrentVersion, manifest.Version)
            || manifest.Version == UpdateChecker.CurrentVersion && manifest.Build < UpdateChecker.CurrentStableBuild)
            throw new CryptographicException("Older update packages are not accepted");
        return manifest;
    }

    internal static void ValidateAddresses(UpdateInfo update)
    {
        string prefix = $"/{UpdateChecker.RepoOwner}/{UpdateChecker.RepoName}/releases/download/";
        static Uri Parse(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
                ? uri : throw new CryptographicException("Update address is not a trusted HTTPS release asset");
        var asset = Parse(update.DownloadUrl);
        var manifest = Parse(update.ManifestUrl);
        var signature = Parse(update.SignatureUrl);
        string directory = asset.AbsolutePath[..(asset.AbsolutePath.LastIndexOf('/') + 1)];
        if (!directory.StartsWith(prefix, StringComparison.Ordinal)
            || directory[prefix.Length..].Trim('/').Contains('/') || directory.Length <= prefix.Length + 1
            || asset.AbsolutePath != directory + update.AssetName
            || manifest.AbsolutePath != directory + "update-manifest.json"
            || signature.AbsolutePath != directory + "update-manifest.sig")
            throw new CryptographicException("Update assets do not belong to the same trusted release");
    }

    public static async Task<string> DownloadVerifiedAsync(UpdateInfo update, CancellationToken cancel = default)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"FMDownloadManager/{UpdateChecker.CurrentVersion}");
        return await DownloadVerifiedAsync(http, update, Path.Combine(Path.GetTempPath(), "FMDownloadManager-updates"), PublicKey, cancel);
    }

    // Injectable transport/key only for regression tests; the app always uses its embedded key above.
    internal static async Task<string> DownloadVerifiedAsync(HttpClient http, UpdateInfo update, string root, string key, CancellationToken cancel = default)
    {
        ValidateAddresses(update);
        byte[] metadata = await ReadSmallAsync(http, update.ManifestUrl, 64 * 1024, cancel);
        byte[] signature = await ReadSmallAsync(http, update.SignatureUrl, 1024, cancel);
        var manifest = VerifyManifest(metadata, signature, update, key);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, manifest.Asset);
        try
        {
            using var response = await GetAsync(http, update.DownloadUrl, cancel);
            if (response.Content.Headers.ContentLength is { } length && length != manifest.Size)
                throw new CryptographicException("Update package length does not match its signed manifest");
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    received += count;
                    if (received > manifest.Size) throw new CryptographicException("Update package exceeds its signed size");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancel);
                }
                if (received != manifest.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Update package failed SHA-256 verification");
            }
            DownloadOrigin.MarkFile(target);
            return target;
        }
        catch
        {
            SegmentedDownloader.TryDelete(target);
            try { Directory.Delete(directory); } catch (IOException) { }
            throw;
        }
    }

    private static async Task<byte[]> ReadSmallAsync(HttpClient http, string url, int limit, CancellationToken cancel)
    {
        using var response = await GetAsync(http, url, cancel);
        await using var input = await response.Content.ReadAsStreamAsync(cancel);
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, cancel)) > 0)
        {
            if (output.Length + count > limit) throw new CryptographicException("Update metadata exceeds the size limit");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string url, CancellationToken cancel)
    {
        Uri current = new(url);
        for (int hop = 0; hop < 6; hop++)
        {
            // GitHub asset redirects can use dedicated asset/CDN hosts, but never HTTP.
            if (current.Scheme != "https" || current.UserInfo.Length > 0)
                throw new CryptographicException("Insecure update redirect rejected");
            var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (response.RequestMessage?.RequestUri?.Scheme != "https")
            { response.Dispose(); throw new CryptographicException("Insecure update response rejected"); }
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) throw new CryptographicException("Update redirect has no destination");
                current = new Uri(current, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            { response.Dispose(); throw new HttpRequestException("Update asset could not be downloaded"); }
            return response;
        }
        throw new CryptographicException("Too many update redirects");
    }
}
