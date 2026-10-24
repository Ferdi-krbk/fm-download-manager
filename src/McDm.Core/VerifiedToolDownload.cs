using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace McDm.Core;

public static class VerifiedToolDownload
{
    public static async Task<string> SaveAsync(HttpClient client, string url, string sha256, string target, CancellationToken cancel = default)
    {
        if (!Regex.IsMatch(sha256, "^[a-fA-F0-9]{64}$") || !Uri.TryCreate(url, UriKind.Absolute, out var source) || source.Scheme != "https")
            throw new ArgumentException("An HTTPS URL and SHA-256 are required");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file, cancel).ConfigureAwait(false);
            await using (var file = File.OpenRead(temporary))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel).ConfigureAwait(false));
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Downloaded tool failed SHA-256 verification; the installed tool was preserved");
            }
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally { SegmentedDownloader.TryDelete(temporary); }
    }
}
