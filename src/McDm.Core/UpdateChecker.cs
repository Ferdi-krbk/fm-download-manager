using System.Net.Http;
using System.Text.Json;

namespace McDm.Core;

public sealed class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public static class UpdateChecker
{
    public static string CurrentVersion
    {
        get
        {
            var entryVer = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);
            if (!string.IsNullOrWhiteSpace(entryVer) && entryVer != "0.0.0") return entryVer;
            var coreVer = typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3);
            if (!string.IsNullOrWhiteSpace(coreVer) && coreVer != "0.0.0") return coreVer;
            return "3.7.1";
        }
    }
    public const string RepoOwner = "Ferdi-krbk";
    public const string RepoName = "fm-download-manager";

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FMDownloadManager/{CurrentVersion}");
            http.Timeout = TimeSpan.FromSeconds(6);

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var cleanTag = tag.TrimStart('v', 'V').Trim();
            var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : $"https://github.com/{RepoOwner}/{RepoName}/releases";
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                        break;
                    }
                }
            }

            bool isNewer = IsNewer(cleanTag, CurrentVersion);
            return new UpdateInfo
            {
                HasUpdate = isNewer,
                LatestVersion = !string.IsNullOrEmpty(cleanTag) ? cleanTag : CurrentVersion,
                ReleaseUrl = releaseUrl,
                ReleaseNotes = body,
                DownloadUrl = !string.IsNullOrEmpty(downloadUrl) ? downloadUrl : releaseUrl
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        if (string.IsNullOrWhiteSpace(latest)) return false;
        if (string.IsNullOrWhiteSpace(current)) return true;

        static string Clean(string v) => v.Trim().TrimStart('v', 'V').Split('-')[0];
        string lClean = Clean(latest);
        string cClean = Clean(current);

        if (Version.TryParse(lClean, out var lVer) && Version.TryParse(cClean, out var cVer))
        {
            return lVer > cVer;
        }

        try
        {
            var lParts = lClean.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            var cParts = cClean.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            int maxLen = Math.Max(lParts.Length, cParts.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int l = i < lParts.Length ? lParts[i] : 0;
                int c = i < cParts.Length ? cParts[i] : 0;
                if (l != c) return l > c;
            }
            return false;
        }
        catch
        {
            return string.Compare(lClean, cClean, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
