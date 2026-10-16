using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McDm.Core;

public sealed class StreamProbeResult
{
    public List<VideoQualityInfo> Qualities { get; set; } = new();
    public List<AudioTrackInfo> AudioTracks { get; set; } = new();
    public List<SubtitleTrackInfo> Subtitles { get; set; } = new();
    public string? Title { get; set; }
}

public static class StreamProbeService
{
    private static readonly Dictionary<string, (string Turkish, string Native)> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "tr", ("Türkçe", "Türkçe") },
        { "tur", ("Türkçe", "Türkçe") },
        { "en", ("İngilizce", "English") },
        { "eng", ("İngilizce", "English") },
        { "es", ("İspanyolca", "Español") },
        { "spa", ("İspanyolca", "Español") },
        { "de", ("Almanca", "Deutsch") },
        { "ger", ("Almanca", "Deutsch") },
        { "deu", ("Almanca", "Deutsch") },
        { "fr", ("Fransızca", "Français") },
        { "fre", ("Fransızca", "Français") },
        { "fra", ("Fransızca", "Français") },
        { "it", ("İtalyanca", "Italiano") },
        { "ita", ("İtalyanca", "Italiano") },
        { "ru", ("Rusça", "Русский") },
        { "rus", ("Rusça", "Русский") },
        { "pt", ("Portekizce", "Português") },
        { "por", ("Portekizce", "Português") },
        { "ja", ("Japonca", "日本語") },
        { "jpn", ("Japonca", "日本語") },
        { "ko", ("Korece", "한국어") },
        { "kor", ("Korece", "한국어") },
        { "zh", ("Çince", "中文") },
        { "zho", ("Çince", "中文") },
        { "chi", ("Çince", "中文") },
        { "ar", ("Arapça", "العربية") },
        { "ara", ("Arapça", "العربية") },
        { "hi", ("Hintçe", "हिन्दी") },
        { "hin", ("Hintçe", "हिन्दी") },
        { "bn", ("Bengalce", "বাংলা") },
        { "ben", ("Bengalce", "বাংলা") },
        { "id", ("Endonezyaca", "Bahasa Indonesia") },
        { "ind", ("Endonezyaca", "Bahasa Indonesia") },
        { "nl", ("Felemenkçe", "Nederlands") },
        { "nld", ("Felemenkçe", "Nederlands") },
        { "dut", ("Felemenkçe", "Nederlands") },
        { "pl", ("Lehçe", "Polski") },
        { "pol", ("Lehçe", "Polski") },
        { "uk", ("Ukraynaca", "Українська") },
        { "ukr", ("Ukraynaca", "Українська") },
        { "vi", ("Vietnamca", "Tiếng Việt") },
        { "vie", ("Vietnamca", "Tiếng Việt") },
        { "th", ("Tayca", "ไทย") },
        { "tha", ("Tayca", "ไทย") },
        { "fa", ("Farsça", "فارسی") },
        { "per", ("Farsça", "فارسی") },
        { "fas", ("Farsça", "فارسی") },
        { "el", ("Yunanca", "Ελληνικά") },
        { "ell", ("Yunanca", "Ελληνικά") },
        { "gre", ("Yunanca", "Ελληνικά") },
        { "he", ("İbranice", "עברית") },
        { "heb", ("İbranice", "עברית") },
        { "sv", ("İsveççe", "Svenska") },
        { "swe", ("İsveççe", "Svenska") },
        { "no", ("Norveççe", "Norsk") },
        { "nor", ("Norveççe", "Norsk") },
        { "da", ("Danca", "Dansk") },
        { "dan", ("Danca", "Dansk") },
        { "fi", ("Fince", "Suomi") },
        { "fin", ("Fince", "Suomi") },
        { "cs", ("Çekçe", "Čeština") },
        { "ces", ("Çekçe", "Čeština") },
        { "cze", ("Çekçe", "Čeština") },
        { "hu", ("Macarca", "Magyar") },
        { "hun", ("Macarca", "Magyar") },
        { "ro", ("Romence", "Română") },
        { "ron", ("Romence", "Română") },
        { "rum", ("Romence", "Română") },
        { "az", ("Azerbaycan Türkçesi", "Azərbaycan") },
        { "aze", ("Azerbaycan Türkçesi", "Azərbaycan") },
        { "kk", ("Kazakça", "Қазақ") },
        { "kaz", ("Kazakça", "Қазақ") },
        { "uz", ("Özbekçe", "Oʻzbek") },
        { "uzb", ("Özbekçe", "Oʻzbek") }
    };

    public static (string Turkish, string Native) GetLanguageInfo(string? langCode, string? rawNote = null)
    {
        string code = (langCode ?? "").Trim().ToLowerInvariant();
        if (code.Contains('-')) code = code.Split('-')[0];
        if (code.Contains('_')) code = code.Split('_')[0];

        if (!string.IsNullOrEmpty(code) && LanguageNames.TryGetValue(code, out var info))
        {
            return info;
        }

        if (!string.IsNullOrEmpty(rawNote))
        {
            foreach (var kvp in LanguageNames)
            {
                if (rawNote.Contains(kvp.Value.Turkish, StringComparison.OrdinalIgnoreCase) ||
                    rawNote.Contains(kvp.Value.Native, StringComparison.OrdinalIgnoreCase) ||
                    rawNote.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
        }

        if (!string.IsNullOrEmpty(code))
        {
            try
            {
                var ci = System.Globalization.CultureInfo.GetCultureInfo(code);
                string trName = ci.DisplayName;
                string natName = ci.NativeName;
                if (!string.IsNullOrEmpty(trName) && !string.IsNullOrEmpty(natName))
                {
                    trName = char.ToUpper(trName[0]) + (trName.Length > 1 ? trName.Substring(1) : "");
                    natName = char.ToUpper(natName[0]) + (natName.Length > 1 ? natName.Substring(1) : "");
                    return (trName, natName);
                }
            }
            catch { }
        }

        string fallback = !string.IsNullOrEmpty(langCode) ? langCode.ToUpperInvariant() : "Bilinmeyen";
        return (fallback, fallback);
    }

    public static string FormatAudioLabel(string? langCode, string? formatNote = null, bool isOriginal = false)
    {
        var (trName, natName) = GetLanguageInfo(langCode, formatNote);
        string langTag = trName.Equals(natName, StringComparison.OrdinalIgnoreCase) ? trName : $"{trName} ({natName})";

        if (isOriginal)
        {
            return $"Orijinal Ses - {langTag}";
        }

        if (langCode?.StartsWith("tr", StringComparison.OrdinalIgnoreCase) == true ||
            formatNote?.Contains("Turkish", StringComparison.OrdinalIgnoreCase) == true ||
            formatNote?.Contains("Türkçe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Türkçe Dublaj";
        }

        return $"{trName} Dublaj ({natName})";
    }

    public static string FormatSubtitleLabel(string? langCode, bool isAuto = false, bool isForced = false)
    {
        var (trName, natName) = GetLanguageInfo(langCode);
        string langTag = trName.Equals(natName, StringComparison.OrdinalIgnoreCase) ? trName : $"{trName} ({natName})";

        if (isForced)
        {
            return $"{langTag} Altyazı (Zorunlu)";
        }
        if (isAuto)
        {
            return $"{langTag} Altyazı (Otomatik)";
        }
        return $"{langTag} Altyazı";
    }

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public static int NormalizeResolutionTier(int width, int height, string? formatNote = null)
    {
        if (!string.IsNullOrWhiteSpace(formatNote))
        {
            var m = Regex.Match(formatNote, @"\b(4320|2160|1440|1080|720|480|360|240|144)p", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int parsedTier))
                return parsedTier;
        }

        int longSide = Math.Max(width, height);
        int shortSide = width > 0 && height > 0 ? Math.Min(width, height) : height;

        if (longSide >= 7600 || shortSide >= 4320) return 4320;
        if (longSide >= 3800 || shortSide >= 2160) return 2160;
        if (longSide >= 2500 || shortSide >= 1440) return 1440;
        if (longSide >= 1900 || shortSide >= 1080) return 1080;
        if (longSide >= 1260 || shortSide >= 720) return 720;
        if (longSide >= 840 || shortSide >= 480) return 480;
        if (longSide >= 630 || shortSide >= 360) return 360;
        return shortSide > 0 ? shortSide : height;
    }

    public static string FormatQualityLabel(int height) => height switch
    {
        >= 4320 => $"{height}p 8K Ultra HD",
        >= 2160 => $"{height}p 4K Ultra HD",
        >= 1440 => $"{height}p 2K QHD",
        >= 1080 => $"{height}p Full HD",
        >= 720 => $"{height}p HD",
        >= 480 => $"{height}p SD",
        >= 360 => $"{height}p Düşük",
        _ => $"{height}p"
    };

    public static async Task<StreamProbeResult> ProbeAsync(
        string url,
        string? referer = null,
        string? userAgent = null,
        List<BrowserCookie>? cookies = null,
        string? cookieHeader = null,
        ToolLauncher? ytDlpLauncher = null,
        CancellationToken ct = default)
    {
        var result = new StreamProbeResult();
        if (string.IsNullOrWhiteSpace(url)) return result;

        // 1. Ultra-fast direct HLS manifest parsing (~100-300ms)
        try
        {
            if (IsHlsCandidate(url))
            {
                var hlsResult = await TryProbeHlsAsync(url, referer, userAgent, cookies, cookieHeader, ct);
                if (hlsResult != null && (hlsResult.Qualities.Count > 0 || hlsResult.AudioTracks.Count > 0))
                {
                    return hlsResult;
                }
            }
        }
        catch { }

        // 1b. Ultra-fast direct YouTube page ytInitialPlayerResponse parsing (~300-500ms)
        try
        {
            if (VideoDownloader.IsYouTubeUrl(url))
            {
                var ytFast = await TryProbeYouTubeFastAsync(url, userAgent, ct);
                if (ytFast != null && ytFast.Qualities.Count > 0)
                {
                    return ytFast;
                }
            }
        }
        catch { }

        // 2. Comprehensive yt-dlp probe (YouTube fallback, Vimeo, complex or encrypted streams)
        if (ytDlpLauncher == null)
        {
            var tp = new ToolPaths();
            if (tp.YtDlpPath is { Length: > 0 } yp)
                ytDlpLauncher = new ToolLauncher(yp);
        }

        if (ytDlpLauncher != null)
        {
            try
            {
                var ytdlResult = await TryProbeYtDlpAsync(url, referer, userAgent, cookies, cookieHeader, ytDlpLauncher, ct);
                if (ytdlResult != null && (ytdlResult.Qualities.Count > 0 || ytdlResult.AudioTracks.Count > 0 || ytdlResult.Subtitles.Count > 0))
                {
                    return ytdlResult;
                }
            }
            catch { }
        }

        return result;
    }

    public static async Task<StreamProbeResult?> TryProbeYouTubeFastAsync(string url, string? userAgent, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", !string.IsNullOrWhiteSpace(userAgent)
            ? userAgent
            : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var html = await resp.Content.ReadAsStringAsync(ct);
        return ParseYouTubePlayerResponseFromHtml(html);
    }

    public static StreamProbeResult? ParseYouTubePlayerResponseFromHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        int idx = html.IndexOf("ytInitialPlayerResponse", StringComparison.Ordinal);
        if (idx < 0) return null;

        int start = html.IndexOf('{', idx);
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;
        int end = -1;
        for (int i = start; i < html.Length; i++)
        {
            char ch = html[i];
            if (escape) { escape = false; continue; }
            if (ch == '\\') { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (!inString)
            {
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) { end = i + 1; break; }
                }
            }
        }
        if (end <= start) return null;

        using var doc = JsonDocument.Parse(html.AsMemory(start, end - start));
        var root = doc.RootElement;
        var result = new StreamProbeResult();

        if (root.TryGetProperty("videoDetails", out var vd) && vd.ValueKind == JsonValueKind.Object)
        {
            if (vd.TryGetProperty("title", out var tEl) && tEl.GetString() is { Length: > 0 } t)
                result.Title = t;
        }

        if (root.TryGetProperty("streamingData", out var sd) && sd.ValueKind == JsonValueKind.Object)
        {
            var heights = new HashSet<int>();
            var audioMap = new Dictionary<string, AudioTrackInfo>(StringComparer.OrdinalIgnoreCase);

            void ProcessFormatArray(JsonElement arr)
            {
                if (arr.ValueKind != JsonValueKind.Array) return;
                foreach (var f in arr.EnumerateArray())
                {
                    string mime = f.TryGetProperty("mimeType", out var mEl) ? mEl.GetString() ?? "" : "";
                    int width = f.TryGetProperty("width", out var wEl) && wEl.TryGetInt32(out int wVal) ? wVal : 0;
                    int rawHeight = f.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out int hVal) ? hVal : 0;
                    string qLabel = f.TryGetProperty("qualityLabel", out var qlEl) ? qlEl.GetString() ?? "" : "";
                    int tier = NormalizeResolutionTier(width, rawHeight, qLabel);

                    if (tier > 0 && (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || rawHeight > 0))
                    {
                        if (heights.Add(tier))
                        {
                            long? bitrate = f.TryGetProperty("bitrate", out var brEl) && brEl.TryGetInt64(out long brVal) ? brVal / 1000 : null;
                            result.Qualities.Add(new VideoQualityInfo
                            {
                                Height = tier,
                                Label = FormatQualityLabel(tier),
                                Bitrate = bitrate
                            });
                        }
                    }

                    if (f.TryGetProperty("audioTrack", out var atEl) && atEl.ValueKind == JsonValueKind.Object)
                    {
                        string dispName = atEl.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() ?? "" : "";
                        string rawId = atEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        bool isDef = atEl.TryGetProperty("audioIsDefault", out var adEl) && adEl.ValueKind == JsonValueKind.True;
                        string langCode = rawId.Contains('.') ? rawId.Split('.')[0] : rawId;
                        bool isExplicitOrig = dispName.Contains("original", StringComparison.OrdinalIgnoreCase) || dispName.Contains("orijinal", StringComparison.OrdinalIgnoreCase);
                        bool isTr = langCode.Equals("tr", StringComparison.OrdinalIgnoreCase) || dispName.Contains("Türkçe", StringComparison.OrdinalIgnoreCase) || dispName.Contains("Turkish", StringComparison.OrdinalIgnoreCase);
                        bool isOrig = isExplicitOrig || (!isTr && isDef);
                        if (isTr && !isExplicitOrig) isOrig = false;
                        string trackId = isTr && !isExplicitOrig ? "tr" : (isOrig ? "original" : (!string.IsNullOrEmpty(langCode) ? langCode : dispName));
                        int itag = f.TryGetProperty("itag", out var itEl) && itEl.TryGetInt32(out int itVal) ? itVal : 0;
                        string fmtId = itag > 0 ? itag.ToString() : "";
                        bool isOpus = mime.Contains("opus", StringComparison.OrdinalIgnoreCase) || itag == 251;

                        if (!string.IsNullOrEmpty(trackId))
                        {
                            if (!audioMap.TryGetValue(trackId, out var existing))
                            {
                                audioMap[trackId] = new AudioTrackInfo
                                {
                                    TrackId = trackId,
                                    Lang = langCode,
                                    Label = FormatAudioLabel(langCode, dispName, isOrig),
                                    FormatId = fmtId,
                                    IsDefault = isDef || isOrig
                                };
                            }
                            else if (isOpus && existing.FormatId != "251" && !string.IsNullOrEmpty(fmtId))
                            {
                                existing.FormatId = fmtId;
                            }
                        }
                    }
                }
            }

            if (sd.TryGetProperty("adaptiveFormats", out var af)) ProcessFormatArray(af);
            if (sd.TryGetProperty("formats", out var fmts)) ProcessFormatArray(fmts);

            result.Qualities = result.Qualities.OrderByDescending(q => q.Height).ToList();
            result.AudioTracks = SortAudioTracks(audioMap.Values);
        }

        if (root.TryGetProperty("captions", out var caps) &&
            caps.TryGetProperty("playerCaptionsTracklistRenderer", out var pctr) &&
            pctr.TryGetProperty("captionTracks", out var ctArr) &&
            ctArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in ctArr.EnumerateArray())
            {
                string lang = c.TryGetProperty("languageCode", out var lcEl) ? lcEl.GetString() ?? "" : "";
                string vssId = c.TryGetProperty("vssId", out var vEl) ? vEl.GetString() ?? "" : "";
                string baseUrl = c.TryGetProperty("baseUrl", out var uEl) ? uEl.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(baseUrl) && !Regex.IsMatch(baseUrl, @"[?&]fmt=", RegexOptions.IgnoreCase))
                {
                    baseUrl += (baseUrl.Contains('?') ? "&" : "?") + "fmt=vtt";
                }
                bool isAuto = vssId.StartsWith("a.", StringComparison.OrdinalIgnoreCase);
                string label = FormatSubtitleLabel(lang, isAuto: isAuto);
                if (!string.IsNullOrEmpty(lang) && !result.Subtitles.Any(s => s.Lang.Equals(lang, StringComparison.OrdinalIgnoreCase) && s.Label == label))
                {
                    result.Subtitles.Add(new SubtitleTrackInfo
                    {
                        Lang = lang,
                        Code = lang,
                        Label = label,
                        Url = baseUrl
                    });
                }
            }
        }

        return result;
    }

    public static List<AudioTrackInfo> SortAudioTracks(IEnumerable<AudioTrackInfo> tracks)
    {
        return tracks.OrderBy(a =>
        {
            bool isOrig = a.TrackId == "original" || a.IsDefault || a.Label.Contains("Orijinal", StringComparison.OrdinalIgnoreCase);
            bool isTr = a.TrackId == "tr" || a.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) || a.Label.Contains("Türkçe", StringComparison.OrdinalIgnoreCase);
            bool isEn = a.TrackId == "en" || a.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

            if (isOrig) return 0;
            if (isTr) return 1;
            if (isEn) return 2;
            return 3;
        }).ThenBy(a => a.Label).ToList();
    }

    private static bool IsHlsCandidate(string url)
    {
        var u = url.ToLowerInvariant();
        return u.Contains(".m3u8") || u.Contains("/hls/") || u.Contains("master.txt") ||
               u.Contains("playlist") || u.Contains("/manifest");
    }

    private static string UpgradeToMasterHlsUrl(string url)
    {
        var upgraded = url;
        if (Regex.IsMatch(upgraded, @"index-f\d+-[a-z0-9-]+\.m3u8", RegexOptions.IgnoreCase))
            upgraded = Regex.Replace(upgraded, @"index-f\d+-[a-z0-9-]+\.m3u8", "master.m3u8", RegexOptions.IgnoreCase);
        else if (Regex.IsMatch(upgraded, @"sublist_vid\d*\.m3u8", RegexOptions.IgnoreCase))
            upgraded = Regex.Replace(upgraded, @"sublist_vid\d*\.m3u8", "master.m3u8", RegexOptions.IgnoreCase);
        return upgraded;
    }

    public static async Task<StreamProbeResult?> TryProbeHlsAsync(
        string url,
        string? referer,
        string? userAgent,
        List<BrowserCookie>? cookies,
        string? cookieHeader,
        CancellationToken ct)
    {
        string targetUrl = UpgradeToMasterHlsUrl(url);

        using var req = new HttpRequestMessage(HttpMethod.Get, targetUrl);
        if (!string.IsNullOrWhiteSpace(userAgent))
            req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        else
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");

        if (!string.IsNullOrWhiteSpace(referer))
        {
            req.Headers.TryAddWithoutValidation("Referer", referer);
            if (Uri.TryCreate(referer, UriKind.Absolute, out var rUri))
                req.Headers.TryAddWithoutValidation("Origin", $"{rUri.Scheme}://{rUri.Authority}");
        }
        else if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var tUri))
        {
            req.Headers.TryAddWithoutValidation("Origin", $"{tUri.Scheme}://{tUri.Authority}");
        }

        string? effectiveCookie = null;
        if (!string.IsNullOrWhiteSpace(cookieHeader)) effectiveCookie = cookieHeader;
        else if (cookies is { Count: > 0 })
        {
            effectiveCookie = string.Join("; ", cookies.Where(c => !string.IsNullOrEmpty(c.Name)).Select(c => $"{c.Name}={c.Value}"));
        }
        if (!string.IsNullOrWhiteSpace(effectiveCookie))
            req.Headers.TryAddWithoutValidation("Cookie", effectiveCookie);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // If upgraded master failed with 404, fallback to original url
            if (targetUrl != url)
            {
                return await TryProbeHlsAsync(url, referer, userAgent, cookies, cookieHeader, ct);
            }
            return null;
        }

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!text.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase)) return null;

        return ParseHlsManifestText(text, targetUrl);
    }

    public static StreamProbeResult ParseHlsManifestText(string text, string baseManifestUrl)
    {
        var result = new StreamProbeResult();
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

        var heights = new HashSet<int>();
        var audioKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 1. Video stream variant: #EXT-X-STREAM-INF
            if (trimmed.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                var resMatch = Regex.Match(trimmed, @"RESOLUTION=(\d+)x(\d+)", RegexOptions.IgnoreCase);
                if (resMatch.Success &&
                    int.TryParse(resMatch.Groups[1].Value, out int width) &&
                    int.TryParse(resMatch.Groups[2].Value, out int rawHeight) && rawHeight > 0)
                {
                    int height = NormalizeResolutionTier(width, rawHeight);
                    if (heights.Add(height))
                    {
                        long? bitrate = null;
                        var bwMatch = Regex.Match(trimmed, @"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                        if (bwMatch.Success && long.TryParse(bwMatch.Groups[1].Value, out long bw)) bitrate = bw;

                        result.Qualities.Add(new VideoQualityInfo
                        {
                            Height = height,
                            Label = FormatQualityLabel(height),
                            Bitrate = bitrate
                        });
                    }
                }
            }

            // 2. Audio track: #EXT-X-MEDIA:TYPE=AUDIO
            else if (trimmed.StartsWith("#EXT-X-MEDIA:", StringComparison.OrdinalIgnoreCase) &&
                     trimmed.Contains("TYPE=AUDIO", StringComparison.OrdinalIgnoreCase))
            {
                var nameMatch = Regex.Match(trimmed, @"NAME=""([^""]+)""", RegexOptions.IgnoreCase);
                var langMatch = Regex.Match(trimmed, @"LANGUAGE=""([^""]+)""", RegexOptions.IgnoreCase);
                var groupMatch = Regex.Match(trimmed, @"GROUP-ID=""([^""]+)""", RegexOptions.IgnoreCase);
                var defMatch = Regex.Match(trimmed, @"DEFAULT=(YES|NO)", RegexOptions.IgnoreCase);
                var uriMatch = Regex.Match(trimmed, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);

                string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Audio";
                string lang = langMatch.Success ? langMatch.Groups[1].Value : "";
                string groupId = groupMatch.Success ? groupMatch.Groups[1].Value : "";
                string uri = uriMatch.Success ? uriMatch.Groups[1].Value : "";
                bool isDef = defMatch.Success && defMatch.Groups[1].Value.Equals("YES", StringComparison.OrdinalIgnoreCase);

                string trackKey = $"{name}_{lang}_{uri}".ToLowerInvariant();
                if (audioKeys.Add(trackKey))
                {
                    bool isTr = name.Contains("turkish", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("türkçe", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("turkce", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("dublaj", StringComparison.OrdinalIgnoreCase) ||
                                lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ||
                                lang.StartsWith("tur", StringComparison.OrdinalIgnoreCase) ||
                                Regex.IsMatch(uri, @"(?:^|[-_./])(tr|tur|turkish|dublaj)(?:[-_./]|$)", RegexOptions.IgnoreCase);

                    bool isExplicitOrig = name.Contains("original", StringComparison.OrdinalIgnoreCase) ||
                                          name.Contains("orijinal", StringComparison.OrdinalIgnoreCase) ||
                                          name.Contains("altyazı", StringComparison.OrdinalIgnoreCase) ||
                                          name.Contains("altyazi", StringComparison.OrdinalIgnoreCase) ||
                                          name.Contains("ingilizce", StringComparison.OrdinalIgnoreCase) ||
                                          name.Contains("english", StringComparison.OrdinalIgnoreCase) ||
                                          lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                                          Regex.IsMatch(uri, @"(?:^|[-_./])(en|eng|english|orig|original)(?:[-_./]|$)", RegexOptions.IgnoreCase);

                    // IMPORTANT: Turkish movie sites set DEFAULT=YES on "Türkçe Dublaj".
                    // Never classify a Turkish track as "Original" just because DEFAULT=YES unless it explicitly says "Original/Orijinal"!
                    bool isOrig = !isTr && (isExplicitOrig || isDef);

                    if (isTr && string.IsNullOrEmpty(lang)) lang = "tr";
                    else if (isOrig && string.IsNullOrEmpty(lang)) lang = "en";

                    string trackId;
                    if (isTr) trackId = "tr";
                    else if (isOrig) trackId = "original";
                    else trackId = !string.IsNullOrEmpty(lang) ? lang : name.ToLowerInvariant();

                    // Ensure we never collide two distinct audio tracks onto the same TrackId
                    if (result.AudioTracks.Any(a => a.TrackId.Equals(trackId, StringComparison.OrdinalIgnoreCase)))
                    {
                        trackId = $"{trackId}_{result.AudioTracks.Count + 1}";
                    }

                    string label = FormatAudioLabel(lang, name, isOrig);

                    result.AudioTracks.Add(new AudioTrackInfo
                    {
                        TrackId = trackId,
                        Label = label,
                        Lang = lang,
                        IsDefault = isDef
                    });
                }
            }

            // 3. Subtitles: #EXT-X-MEDIA:TYPE=SUBTITLES
            else if (trimmed.StartsWith("#EXT-X-MEDIA:", StringComparison.OrdinalIgnoreCase) &&
                     trimmed.Contains("TYPE=SUBTITLES", StringComparison.OrdinalIgnoreCase))
            {
                var nameMatch = Regex.Match(trimmed, @"NAME=""([^""]+)""", RegexOptions.IgnoreCase);
                var langMatch = Regex.Match(trimmed, @"LANGUAGE=""([^""]+)""", RegexOptions.IgnoreCase);
                var uriMatch = Regex.Match(trimmed, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);

                string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Subtitle";
                string lang = langMatch.Success ? langMatch.Groups[1].Value.ToLowerInvariant() : "und";
                string uri = uriMatch.Success ? uriMatch.Groups[1].Value : "";

                string subUrl = "";
                if (!string.IsNullOrEmpty(uri))
                {
                    try { subUrl = new Uri(new Uri(baseManifestUrl), uri).AbsoluteUri; }
                    catch { subUrl = uri; }
                }

                string subKey = $"{name}_{lang}_{subUrl}".ToLowerInvariant();
                if (subKeys.Add(subKey))
                {
                    if (name.Contains("türk", StringComparison.OrdinalIgnoreCase) || lang.Equals("tr", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("-tr-"))
                    {
                        lang = "tr";
                    }
                    else if (name.Contains("eng", StringComparison.OrdinalIgnoreCase) || lang.Equals("en", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("-en-"))
                    {
                        lang = "en";
                    }
                    else if (lang.Equals("pt", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("-pt-"))
                    {
                        lang = "pt";
                    }
                    else if (lang.Equals("es", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("-es-"))
                    {
                        lang = "es";
                    }
                    else if (lang.Equals("fr", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("-fr-"))
                    {
                        lang = "fr";
                    }

                    bool isForced = name.Contains("forced", StringComparison.OrdinalIgnoreCase) || name.Contains("zorunlu", StringComparison.OrdinalIgnoreCase) || subUrl.Contains("forced");
                    string label = FormatSubtitleLabel(lang, isAuto: false, isForced: isForced);

                    result.Subtitles.Add(new SubtitleTrackInfo
                    {
                        Label = label,
                        Lang = lang,
                        Url = subUrl
                    });
                }
            }
        }

        // If the HLS URL indicates a Turkish Dual-Audio encode (e.g. "trdual", "dual") and only 1 track was listed in the manifest
        // (or one track had generic/missing metadata), ensure both "Orijinal Ses" and "Türkçe Dublaj" are selectable!
        if (Regex.IsMatch(baseManifestUrl ?? "", @"(trdual|dual[-_]?audio|[-_]dual)", RegexOptions.IgnoreCase))
        {
            bool hasTr = result.AudioTracks.Any(a => a.TrackId == "tr" || a.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) || a.Label.Contains("Türkçe", StringComparison.OrdinalIgnoreCase));
            bool hasOrig = result.AudioTracks.Any(a => a.TrackId == "original" || a.Label.Contains("Orijinal", StringComparison.OrdinalIgnoreCase));

            if (hasTr && !hasOrig)
            {
                result.AudioTracks.Add(new AudioTrackInfo
                {
                    TrackId = "original",
                    Lang = "en",
                    Label = "Orijinal Ses - İngilizce (English)",
                    IsDefault = false
                });
            }
            else if (hasOrig && !hasTr)
            {
                result.AudioTracks.Add(new AudioTrackInfo
                {
                    TrackId = "tr",
                    Lang = "tr",
                    Label = "Türkçe Dublaj",
                    IsDefault = false
                });
            }
            else if (result.AudioTracks.Count == 0)
            {
                result.AudioTracks.Add(new AudioTrackInfo { TrackId = "original", Lang = "en", Label = "Orijinal Ses - İngilizce (English)", IsDefault = true });
                result.AudioTracks.Add(new AudioTrackInfo { TrackId = "tr", Lang = "tr", Label = "Türkçe Dublaj", IsDefault = false });
            }
        }

        // Sort qualities descending (1080 -> 720 -> 480 ...)
        result.Qualities = result.Qualities.OrderByDescending(q => q.Height).ToList();

        // Sort audio tracks consistently (Original first, Turkish second, English third)
        result.AudioTracks = SortAudioTracks(result.AudioTracks);

        return result;
    }

    private static async Task<StreamProbeResult?> TryProbeYtDlpAsync(
        string url,
        string? referer,
        string? userAgent,
        List<BrowserCookie>? cookies,
        string? cookieHeader,
        ToolLauncher tool,
        CancellationToken ct)
    {
        string? cookieFile = null;
        try
        {
            var reqMock = new DownloadRequest { Url = url, Cookies = cookies, CookieHeader = cookieHeader };
            var cookieText = VideoDownloader.BuildNetscapeCookies(reqMock);
            if (cookieText.Contains('\t'))
            {
                cookieFile = Path.Combine(Path.GetTempPath(), "mcdm-probe-" + Guid.NewGuid().ToString("N") + ".cookies.txt");
                await File.WriteAllTextAsync(cookieFile, cookieText, new UTF8Encoding(false), ct);
            }

            var psi = new ProcessStartInfo(tool.FileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var l in tool.Lead) psi.ArgumentList.Add(l);

            psi.ArgumentList.Add("--dump-single-json");
            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--socket-timeout"); psi.ArgumentList.Add("8");
            psi.ArgumentList.Add("--no-warnings");
            bool isYt = url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
            if (!isYt)
            {
                psi.ArgumentList.Add("--extractor-args"); psi.ArgumentList.Add("generic:impersonate");
            }

            if (!string.IsNullOrWhiteSpace(userAgent) && !isYt)
            {
                psi.ArgumentList.Add("--user-agent"); psi.ArgumentList.Add(userAgent);
            }
            if (!string.IsNullOrWhiteSpace(referer) && !isYt)
            {
                psi.ArgumentList.Add("--referer"); psi.ArgumentList.Add(referer);
            }
            if (cookieFile != null && !isYt)
            {
                psi.ArgumentList.Add("--cookies"); psi.ArgumentList.Add(cookieFile);
            }

            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(45), ct);

            var finishedTask = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), timeoutTask);
            if (finishedTask == timeoutTask)
            {
                try { proc.Kill(); } catch { }
                return null;
            }

            string json = await stdoutTask;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new StreamProbeResult();
            if (root.TryGetProperty("title", out var titleEl) && titleEl.GetString() is { Length: > 0 } t)
                result.Title = t;

            // Formats array
            if (root.TryGetProperty("formats", out var formatsEl) && formatsEl.ValueKind == JsonValueKind.Array)
            {
                var heights = new HashSet<int>();
                var audioMap = new Dictionary<string, AudioTrackInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var f in formatsEl.EnumerateArray())
                {
                    string vcodec = f.TryGetProperty("vcodec", out var vc) ? vc.GetString() ?? "" : "";
                    string acodec = f.TryGetProperty("acodec", out var ac) ? ac.GetString() ?? "" : "";
                    int width = f.TryGetProperty("width", out var wEl) && wEl.TryGetInt32(out int wVal) ? wVal : 0;
                    int rawHeight = f.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out int hVal) ? hVal : 0;
                    string formatId = f.TryGetProperty("format_id", out var fid) ? fid.GetString() ?? "" : "";
                    string formatNote = f.TryGetProperty("format_note", out var fn) ? fn.GetString() ?? "" : "";
                    string lang = f.TryGetProperty("language", out var lEl) ? lEl.GetString() ?? "" : "";
                    int height = NormalizeResolutionTier(width, rawHeight, formatNote);

                    // Video quality
                    if (height > 0 && vcodec != "none")
                    {
                        if (heights.Add(height))
                        {
                            long? bitrate = f.TryGetProperty("tbr", out var tbrEl) && tbrEl.TryGetInt64(out long tbrVal) ? tbrVal : null;

                            result.Qualities.Add(new VideoQualityInfo
                            {
                                Height = height,
                                Label = FormatQualityLabel(height),
                                FormatId = formatId,
                                Bitrate = bitrate
                            });
                        }
                    }

                    // Audio track
                    bool isAudio = (vcodec == "none" || string.IsNullOrEmpty(vcodec)) &&
                                   (acodec != "none" || formatId.Contains("audio") || formatNote.Contains("audio"));
                    if (isAudio)
                    {
                        string cleanLang = (lang ?? "").Trim().ToLowerInvariant();
                        if (cleanLang.Contains('-')) cleanLang = cleanLang.Split('-')[0];
                        if (cleanLang.Contains('_')) cleanLang = cleanLang.Split('_')[0];

                        if (string.IsNullOrEmpty(cleanLang))
                        {
                            var info = GetLanguageInfo(null, formatNote);
                            if (info.Turkish != "Bilinmeyen")
                            {
                                cleanLang = LanguageNames.FirstOrDefault(x => x.Value.Turkish == info.Turkish).Key ?? "";
                            }
                        }

                        bool isTr = cleanLang == "tr" || cleanLang == "tur" ||
                                    formatNote.Contains("Turkish", StringComparison.OrdinalIgnoreCase) ||
                                    formatNote.Contains("Türkçe", StringComparison.OrdinalIgnoreCase) ||
                                    formatNote.Contains("Turkce", StringComparison.OrdinalIgnoreCase) ||
                                    formatNote.Contains("Dublaj", StringComparison.OrdinalIgnoreCase) ||
                                    formatId.Contains("Turkish", StringComparison.OrdinalIgnoreCase) ||
                                    formatId.Contains("Dublaj", StringComparison.OrdinalIgnoreCase);

                        bool isExplicitOrig = formatNote.Contains("Original", StringComparison.OrdinalIgnoreCase) ||
                                              formatNote.Contains("Orijinal", StringComparison.OrdinalIgnoreCase) ||
                                              formatId.Contains("Original", StringComparison.OrdinalIgnoreCase);

                        bool isDefaultPref = formatNote.Contains("default", StringComparison.OrdinalIgnoreCase) ||
                                             (f.TryGetProperty("language_preference", out var lpEl) && lpEl.TryGetInt32(out int lp) && lp > 0);

                        bool isOriginal = isExplicitOrig || (!isTr && isDefaultPref);
                        if (isTr && !isExplicitOrig) isOriginal = false;

                        string trackId = isTr && !isExplicitOrig ? "tr" : (isOriginal ? "original" : (!string.IsNullOrEmpty(cleanLang) ? cleanLang : formatId));
                        string label = FormatAudioLabel(!string.IsNullOrEmpty(cleanLang) ? cleanLang : lang, formatNote, isOriginal);

                        if (!audioMap.TryGetValue(trackId, out var existingTrack))
                        {
                            audioMap[trackId] = new AudioTrackInfo
                            {
                                TrackId = trackId,
                                Label = label,
                                Lang = !string.IsNullOrEmpty(lang) ? lang : cleanLang,
                                FormatId = formatId,
                                IsDefault = isOriginal
                            };
                        }
                        else
                        {
                            bool isOpus = acodec.Contains("opus", StringComparison.OrdinalIgnoreCase) || formatId.StartsWith("251");
                            bool existingIsOpus = existingTrack.FormatId?.StartsWith("251") == true;
                            if (isOpus && !existingIsOpus)
                            {
                                existingTrack.FormatId = formatId;
                            }
                        }
                    }
                }

                result.Qualities = result.Qualities.OrderByDescending(q => q.Height).ToList();
                result.AudioTracks = SortAudioTracks(audioMap.Values);
            }

            // Subtitles
            void ExtractSubs(string propName, bool isAuto)
            {
                if (root.TryGetProperty(propName, out var subsEl) && subsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in subsEl.EnumerateObject())
                    {
                        string lang = prop.Name;
                        if (result.Subtitles.Any(s => s.Lang.Equals(lang, StringComparison.OrdinalIgnoreCase))) continue;

                        string label = FormatSubtitleLabel(lang, isAuto: isAuto);

                        string subUrl = "";
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (item.TryGetProperty("url", out var uEl) && uEl.GetString() is { Length: > 0 } u)
                                {
                                    string ext = item.TryGetProperty("ext", out var eEl) ? eEl.GetString() ?? "" : "";
                                    if (ext is "vtt" or "srt" || string.IsNullOrEmpty(subUrl))
                                        subUrl = u;
                                }
                            }
                        }

                        result.Subtitles.Add(new SubtitleTrackInfo
                        {
                            Label = label,
                            Lang = lang,
                            Url = subUrl
                        });
                    }
                }
            }

            ExtractSubs("subtitles", isAuto: false);
            ExtractSubs("automatic_captions", isAuto: true);

            result.Subtitles = result.Subtitles.OrderBy(s =>
            {
                bool isTr = s.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
                bool isEn = s.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                bool isAuto = s.Label?.Contains("Otomatik") == true;

                if (isTr && !isAuto) return 0;
                if (isTr && isAuto) return 1;
                if (isEn && !isAuto) return 2;
                if (isEn && isAuto) return 3;
                if (!isAuto) return 4;
                return 5;
            }).ThenBy(s => s.Label).ToList();

            return result;
        }
        finally
        {
            if (cookieFile != null) SegmentedDownloader.TryDelete(cookieFile);
        }
    }
}
