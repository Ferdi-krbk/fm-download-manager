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
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
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

        // 2. Comprehensive yt-dlp probe (YouTube, Vimeo, complex or encrypted streams)
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
                if (resMatch.Success && int.TryParse(resMatch.Groups[2].Value, out int height) && height > 0)
                {
                    if (heights.Add(height))
                    {
                        long? bitrate = null;
                        var bwMatch = Regex.Match(trimmed, @"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                        if (bwMatch.Success && long.TryParse(bwMatch.Groups[1].Value, out long bw)) bitrate = bw;

                        string label = height switch
                        {
                            >= 2160 => $"{height}p 4K Ultra HD",
                            >= 1440 => $"{height}p 2K QHD",
                            >= 1080 => $"{height}p Full HD",
                            >= 720 => $"{height}p HD",
                            >= 480 => $"{height}p SD",
                            _ => $"{height}p"
                        };

                        result.Qualities.Add(new VideoQualityInfo
                        {
                            Height = height,
                            Label = label,
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

                string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Audio";
                string lang = langMatch.Success ? langMatch.Groups[1].Value : "";
                string groupId = groupMatch.Success ? groupMatch.Groups[1].Value : "";
                bool isDef = defMatch.Success && defMatch.Groups[1].Value.Equals("YES", StringComparison.OrdinalIgnoreCase);

                string trackKey = $"{name}_{lang}".ToLowerInvariant();
                if (audioKeys.Add(trackKey))
                {
                    string trackId = "best";
                    string label = name;

                    if (name.Contains("original", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("ingilizce", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("english", StringComparison.OrdinalIgnoreCase) ||
                        lang.Equals("en", StringComparison.OrdinalIgnoreCase))
                    {
                        trackId = "original";
                        label = "Orijinal Ses (Original Audio)";
                    }
                    else if (name.Contains("turkish", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("türkçe", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("dublaj", StringComparison.OrdinalIgnoreCase) ||
                             lang.Equals("tr", StringComparison.OrdinalIgnoreCase))
                    {
                        trackId = "tr";
                        label = "Türkçe Dublaj (Turkish)";
                    }
                    else
                    {
                        trackId = !string.IsNullOrEmpty(lang) ? lang : name.ToLowerInvariant();
                        label = !string.IsNullOrEmpty(lang) ? $"{name} ({lang})" : name;
                    }

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
                string lang = langMatch.Success ? langMatch.Groups[1].Value : "tr";
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
                    string label = name;
                    if (name.Contains("türk", StringComparison.OrdinalIgnoreCase) || lang.Equals("tr", StringComparison.OrdinalIgnoreCase))
                        label = "Türkçe Altyazı";
                    else if (name.Contains("eng", StringComparison.OrdinalIgnoreCase) || lang.Equals("en", StringComparison.OrdinalIgnoreCase))
                        label = "İngilizce Altyazı";

                    result.Subtitles.Add(new SubtitleTrackInfo
                    {
                        Label = label,
                        Lang = lang,
                        Url = subUrl
                    });
                }
            }
        }

        // Sort qualities descending (1080 -> 720 -> 480 ...)
        result.Qualities = result.Qualities.OrderByDescending(q => q.Height).ToList();

        // Sort audio tracks: Original first, Turkish second, others after
        result.AudioTracks = result.AudioTracks.OrderBy(a => a.TrackId switch
        {
            "original" => 0,
            "tr" => 1,
            _ => 2
        }).ToList();

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
            if (!url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("--extractor-args"); psi.ArgumentList.Add("generic:impersonate");
            }

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                psi.ArgumentList.Add("--user-agent"); psi.ArgumentList.Add(userAgent);
            }
            if (!string.IsNullOrWhiteSpace(referer))
            {
                psi.ArgumentList.Add("--referer"); psi.ArgumentList.Add(referer);
            }
            if (cookieFile != null)
            {
                psi.ArgumentList.Add("--cookies"); psi.ArgumentList.Add(cookieFile);
            }

            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), ct);

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
                    int height = f.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out int hVal) ? hVal : 0;
                    string formatId = f.TryGetProperty("format_id", out var fid) ? fid.GetString() ?? "" : "";
                    string formatNote = f.TryGetProperty("format_note", out var fn) ? fn.GetString() ?? "" : "";
                    string lang = f.TryGetProperty("language", out var lEl) ? lEl.GetString() ?? "" : "";

                    // Video quality
                    if (height > 0 && vcodec != "none")
                    {
                        if (heights.Add(height))
                        {
                            long? bitrate = f.TryGetProperty("tbr", out var tbrEl) && tbrEl.TryGetInt64(out long tbrVal) ? tbrVal : null;
                            string label = height switch
                            {
                                >= 2160 => $"{height}p 4K Ultra HD",
                                >= 1440 => $"{height}p 2K QHD",
                                >= 1080 => $"{height}p Full HD",
                                >= 720 => $"{height}p HD",
                                >= 480 => $"{height}p SD",
                                _ => $"{height}p"
                            };

                            result.Qualities.Add(new VideoQualityInfo
                            {
                                Height = height,
                                Label = label,
                                FormatId = formatId,
                                Bitrate = bitrate
                            });
                        }
                    }

                    // Audio track
                    bool isAudio = vcodec == "none" || acodec != "none" || formatId.Contains("audio") || formatNote.Contains("audio");
                    if (isAudio)
                    {
                        string note = $"{formatNote} {formatId} {lang}".Trim();
                        string trackId = "best";
                        string label = "Ses Akışı";

                        if (note.Contains("Original", StringComparison.OrdinalIgnoreCase) ||
                            note.Contains("Eng", StringComparison.OrdinalIgnoreCase) ||
                            lang.Equals("en", StringComparison.OrdinalIgnoreCase))
                        {
                            trackId = "original";
                            label = "Orijinal Ses (Original Audio)";
                        }
                        else if (note.Contains("Turkish", StringComparison.OrdinalIgnoreCase) ||
                                 note.Contains("Turk", StringComparison.OrdinalIgnoreCase) ||
                                 lang.Equals("tr", StringComparison.OrdinalIgnoreCase))
                        {
                            trackId = "tr";
                            label = "Türkçe Dublaj (Turkish)";
                        }
                        else if (!string.IsNullOrEmpty(lang))
                        {
                            trackId = lang;
                            label = $"{lang.ToUpperInvariant()} Ses Akışı";
                        }

                        if (!audioMap.ContainsKey(trackId))
                        {
                            audioMap[trackId] = new AudioTrackInfo
                            {
                                TrackId = trackId,
                                Label = label,
                                Lang = lang,
                                FormatId = formatId
                            };
                        }
                    }
                }

                result.Qualities = result.Qualities.OrderByDescending(q => q.Height).ToList();
                result.AudioTracks = audioMap.Values.OrderBy(a => a.TrackId switch
                {
                    "original" => 0,
                    "tr" => 1,
                    _ => 2
                }).ToList();
            }

            // Subtitles
            void ExtractSubs(string propName)
            {
                if (root.TryGetProperty(propName, out var subsEl) && subsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in subsEl.EnumerateObject())
                    {
                        string lang = prop.Name;
                        if (result.Subtitles.Any(s => s.Lang == lang)) continue;

                        string label = lang switch
                        {
                            "tr" or "tur" => "Türkçe Altyazı",
                            "en" or "eng" => "İngilizce Altyazı",
                            _ => $"{lang.ToUpperInvariant()} Altyazı"
                        };

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

            ExtractSubs("subtitles");
            ExtractSubs("automatic_captions");

            return result;
        }
        finally
        {
            if (cookieFile != null) SegmentedDownloader.TryDelete(cookieFile);
        }
    }
}
