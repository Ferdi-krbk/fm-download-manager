using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using McDm.App.Resources;
using McDm.Core;

namespace McDm.App;

public partial class VideoQualityDialog : Window
{
    public string SelectedFormat { get; private set; } = "bestvideo+bestaudio/best";
    public bool AudioOnly { get; private set; } = false;
    public bool DownloadSubtitles { get; private set; } = true;
    public string SubtitleLangs { get; private set; } = "tr,en";
    public string? SelectedSubtitleUrl { get; private set; }
    public string? ResolvedUrl { get; private set; }
    public string? DetectedTitle { get; private set; }

    private readonly string _url;
    private readonly DownloadRequest? _request;
    private readonly ToolLauncher? _toolLauncher;
    private readonly CancellationTokenSource _cts = new();

    private List<VideoQualityInfo> _qualities = new();
    private List<AudioTrackInfo> _audioTracks = new();
    private List<SubtitleTrackInfo> _subtitles = new();

    public VideoQualityDialog(
        string url,
        string? title = null,
        bool defaultSubtitles = true,
        string defaultLangs = "tr,en",
        List<SubtitleTrackInfo>? detectedSubs = null,
        List<VideoQualityInfo>? detectedQualities = null,
        List<AudioTrackInfo>? detectedAudios = null,
        DownloadRequest? request = null,
        ToolLauncher? toolLauncher = null)
    {
        InitializeComponent();
        ApplyLocalization();

        _url = url;
        _request = request;
        _toolLauncher = toolLauncher;

        TxtVideoUrl.Text = url;
        TxtVideoTitle.Text = string.IsNullOrWhiteSpace(title) ? Strings.VideoDialogDefaultTitle : title;
        ChkDownloadSubtitles.IsChecked = defaultSubtitles;
        DownloadSubtitles = defaultSubtitles;
        SubtitleLangs = defaultLangs;

        if (detectedQualities is { Count: > 0 }) _qualities = detectedQualities;
        if (detectedAudios is { Count: > 0 }) _audioTracks = detectedAudios;
        if (detectedSubs is { Count: > 0 }) _subtitles = detectedSubs;

        if (_qualities.Count == 0)
        {
            _qualities = new List<VideoQualityInfo>
            {
                new() { Height = 2160, Label = "2160p 4K Ultra HD" },
                new() { Height = 1440, Label = "1440p 2K QHD" },
                new() { Height = 1080, Label = "1080p Full HD (MP4)" },
                new() { Height = 720, Label = Strings.IsTurkish ? "720p HD (Hızlı İndirme)" : "720p HD (Fast Download)" },
                new() { Height = 480, Label = "480p SD" },
                new() { Height = 360, Label = Strings.IsTurkish ? "360p Düşük" : "360p Low" }
            };
        }


        if (_audioTracks.Count > 0)
        {
            _audioTracks = StreamProbeService.SortAudioTracks(_audioTracks);
        }

        PopulateQualities();
        PopulateAudioTracks();
        PopulateSubtitles();

        Loaded += (_, _) => _ = ProbeStreamAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private void ApplyLocalization()
    {
        Title = Strings.VideoDialogTitle;
        if (TxtWinHeader != null) TxtWinHeader.Text = Strings.VideoDialogTitle;
        if (LblFormatOption != null) LblFormatOption.Text = Strings.VideoDialogFormatOption;
        if (TxtOptBestTitle != null) TxtOptBestTitle.Text = Strings.VideoDialogBestTitle;
        if (TxtOptBestSub != null) TxtOptBestSub.Text = Strings.VideoDialogBestSub;
        if (TxtOptAudioTitle != null) TxtOptAudioTitle.Text = Strings.VideoDialogAudioTitle;
        if (TxtOptAudioSub != null) TxtOptAudioSub.Text = Strings.VideoDialogAudioSub;
        if (LblAudioTrack != null) LblAudioTrack.Text = Strings.VideoDialogAudioTrack;
        if (ChkDownloadSubtitles != null) ChkDownloadSubtitles.Content = Strings.VideoDialogSubtitlesSave;
        if (LblSubtitleLang != null) LblSubtitleLang.Text = Strings.VideoDialogSubtitleLangLabel;
        if (BtnCancel != null) BtnCancel.Content = Strings.Cancel;
        if (BtnStart != null) BtnStart.Content = Strings.VideoDialogStart;
    }

    private void PopulateQualities()
    {
        string selectedTag = "best";
        foreach (var child in PnlQualityOptions.Children.OfType<RadioButton>())
        {
            if (child.IsChecked == true && child.Tag is string t) { selectedTag = t; break; }
        }

        PnlQualityOptions.Children.Clear();

        // 1. En Yüksek Kalite (Always top choice)
        var rbBest = CreateQualityRadioButton(
            "best",
            Strings.VideoDialogBestTitle,
            Strings.VideoDialogBestSub,
            "🎬",
            selectedTag == "best"
        );
        PnlQualityOptions.Children.Add(rbBest);

        // 2. Detected qualities
        if (_qualities.Count > 0)
        {
            AddSeparator(PnlQualityOptions);
            foreach (var q in _qualities)
            {
                string tag = q.Height.ToString();
                string subText = q.Height switch
                {
                    >= 4320 => Strings.IsTurkish ? "Ultra yüksek çözünürlük (8K UHD)" : "Ultra high resolution (8K UHD)",
                    >= 2160 => Strings.IsTurkish ? "Ultra yüksek çözünürlük (4K UHD)" : "Ultra high resolution (4K UHD)",
                    >= 1440 => Strings.IsTurkish ? "Yüksek çözünürlük (2K QHD)" : "High resolution (2K QHD)",
                    >= 1080 => Strings.IsTurkish ? "Standart yüksek çözünürlük (Full HD)" : "Standard high resolution (Full HD)",
                    >= 720 => Strings.IsTurkish ? "Daha küçük dosya boyutu, hızlı aktarım (HD)" : "Smaller file size, fast transfer (HD)",
                    _ => Strings.IsTurkish ? "Düşük boyutlu hızlı indirme" : "Compact file size, fast download"
                };

                var rb = CreateQualityRadioButton(
                    tag,
                    q.Label,
                    subText,
                    "🎬",
                    selectedTag == tag
                );
                PnlQualityOptions.Children.Add(rb);
            }
        }

        // 3. Audio Only
        AddSeparator(PnlQualityOptions);
        var rbAudio = CreateQualityRadioButton(
            "audio",
            Strings.VideoDialogAudioTitle,
            Strings.VideoDialogAudioSub,
            "🎵",
            selectedTag == "audio"
        );
        PnlQualityOptions.Children.Add(rbAudio);

        if (!PnlQualityOptions.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
        {
            rbBest.IsChecked = true;
        }
    }

    private RadioButton CreateQualityRadioButton(string tag, string title, string sub, string icon, bool isChecked)
    {
        var rb = new RadioButton
        {
            GroupName = "QualityGroup",
            Tag = tag,
            IsChecked = isChecked,
            Margin = new Thickness(0, 4, 0, 4)
        };

        var spOuter = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        spOuter.Children.Add(iconBlock);

        var spText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        spText.Children.Add(titleBlock);

        var subBlock = new TextBlock
        {
            Text = sub,
            FontSize = 10.5
        };
        subBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        spText.Children.Add(subBlock);

        spOuter.Children.Add(spText);
        rb.Content = spOuter;
        return rb;
    }

    private static void AddSeparator(StackPanel panel)
    {
        var sep = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4)
        };
        sep.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        panel.Children.Add(sep);
    }

    private void PopulateAudioTracks()
    {
        object? previouslySelected = (CmbAudioTrack.SelectedItem as ComboBoxItem)?.Tag;

        CmbAudioTrack.Items.Clear();

        if (_audioTracks.Count > 1)
        {
            ComboBoxItem? itemToSelect = null;
            var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in _audioTracks)
            {
                string displayLabel = GetCleanAudioTrackName(a);
                if (!seenLabels.Add(displayLabel))
                {
                    if (!string.IsNullOrEmpty(a.FormatId)) displayLabel += $" [{a.FormatId}]";
                }

                var cbi = new ComboBoxItem
                {
                    Content = displayLabel,
                    Tag = a
                };
                CmbAudioTrack.Items.Add(cbi);

                if (previouslySelected != null)
                {
                    if (previouslySelected is AudioTrackInfo prevAti &&
                        (string.Equals(prevAti.TrackId, a.TrackId, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrEmpty(prevAti.Lang) && string.Equals(prevAti.Lang, a.Lang, StringComparison.OrdinalIgnoreCase))))
                        itemToSelect = cbi;
                    else if (previouslySelected is string prevStr &&
                             (string.Equals(prevStr, a.TrackId, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(prevStr, a.Lang, StringComparison.OrdinalIgnoreCase)))
                        itemToSelect = cbi;
                }
            }

            CmbAudioTrack.SelectedItem = itemToSelect ?? CmbAudioTrack.Items[0];
            TxtAudioTrackCount.Text = Strings.VideoDialogTracksDetected(_audioTracks.Count);
        }
        else
        {
            var singleTag = _audioTracks.Count == 1 ? (object)_audioTracks[0] : "best";
            CmbAudioTrack.Items.Add(new ComboBoxItem
            {
                Content = Strings.VideoDialogOriginalAudio,
                Tag = singleTag
            });
            CmbAudioTrack.SelectedIndex = 0;
            TxtAudioTrackCount.Text = "";
        }
    }

    private static string GetCleanAudioTrackName(AudioTrackInfo a)
    {
        string lang = (!string.IsNullOrWhiteSpace(a.Lang) ? a.Lang : a.TrackId ?? "").Trim().ToLowerInvariant();
        if (lang.Contains('-')) lang = lang.Split('-')[0];
        if (lang.Contains('_')) lang = lang.Split('_')[0];

        bool isTr = Strings.IsTurkish;
        var info = StreamProbeService.GetLanguageInfo(lang, a.Label);
        string name = isTr ? info.Turkish : GetEnglishLanguageName(lang, info.Turkish);

        if (string.Equals(name, "Bilinmeyen", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(name))
        {
            if (!string.IsNullOrWhiteSpace(a.Label) &&
                !a.Label.Equals("Audio", StringComparison.OrdinalIgnoreCase) &&
                !a.Label.Equals("Ses", StringComparison.OrdinalIgnoreCase))
            {
                name = a.Label.Trim();
            }
            else
            {
                name = isTr ? "Orijinal Ses" : "Original Audio";
            }
        }

        name = name.Replace("(Varsa)", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("(If Available)", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

        bool isOriginal = a.TrackId == "original" ||
                          a.IsDefault ||
                          (a.Label?.Contains("Original", StringComparison.OrdinalIgnoreCase) == true) ||
                          (a.Label?.Contains("Orijinal", StringComparison.OrdinalIgnoreCase) == true);

        bool isDub = (a.Label?.Contains("Dub", StringComparison.OrdinalIgnoreCase) == true) ||
                     (a.Label?.Contains("Dublaj", StringComparison.OrdinalIgnoreCase) == true);

        if (isOriginal && !name.Contains("Orijinal", StringComparison.OrdinalIgnoreCase) && !name.Contains("Original", StringComparison.OrdinalIgnoreCase))
        {
            return isTr ? $"{name} (Orijinal)" : $"{name} (Original)";
        }
        if (isDub && !name.Contains("Dub", StringComparison.OrdinalIgnoreCase))
        {
            return isTr ? $"{name} (Dublaj)" : $"{name} (Dub)";
        }

        return name;
    }

    private static string GetEnglishLanguageName(string code, string fallback) => code switch
    {
        "tr" or "tur" => "Turkish",
        "en" or "eng" => "English",
        "es" or "spa" => "Spanish",
        "de" or "ger" or "deu" => "German",
        "fr" or "fre" or "fra" => "French",
        "it" or "ita" => "Italian",
        "ru" or "rus" => "Russian",
        "pt" or "por" => "Portuguese",
        "ja" or "jpn" => "Japanese",
        "ko" or "kor" => "Korean",
        "zh" or "zho" or "chi" => "Chinese",
        "ar" or "ara" => "Arabic",
        "hi" or "hin" => "Hindi",
        "nl" or "nld" or "dut" => "Dutch",
        "pl" or "pol" => "Polish",
        "sv" or "swe" => "Swedish",
        "no" or "nor" => "Norwegian",
        "da" or "dan" => "Danish",
        "fi" or "fin" => "Finnish",
        "el" or "ell" or "gre" => "Greek",
        "he" or "heb" => "Hebrew",
        "uk" or "ukr" => "Ukrainian",
        "id" or "ind" => "Indonesian",
        "vi" or "vie" => "Vietnamese",
        "th" or "tha" => "Thai",
        "fa" or "fas" or "per" => "Persian",
        "ro" or "ron" or "rum" => "Romanian",
        "hu" or "hun" => "Hungarian",
        "cs" or "ces" or "cze" => "Czech",
        _ => fallback
    };

    private static bool IsVideoOrAudioHlsPlaylist(string uLower, SubtitleTrackInfo s)
    {
        if (uLower.Contains("sublist_aud") || uLower.Contains("sublist_vid") || uLower.Contains("master.m3u8") || uLower.Contains("index-f"))
            return true;

        bool hasSubtitleHint = uLower.Contains("sub") || uLower.Contains("caption") || uLower.Contains("vtt") ||
                               uLower.Contains("srt") || uLower.Contains("altyazi") || uLower.Contains("text") ||
                               !string.IsNullOrEmpty(s.Lang) || !string.IsNullOrEmpty(s.Code) ||
                               (!string.IsNullOrEmpty(s.Label) && !s.Label.Equals("Subtitle", StringComparison.OrdinalIgnoreCase));

        if ((uLower.Contains(".m3u8") || uLower.Contains("playlist") || uLower.Contains("manifest")) && !hasSubtitleHint)
            return true;

        if (uLower.Contains("/hls/") && !uLower.EndsWith(".vtt") && !uLower.EndsWith(".srt") && !hasSubtitleHint)
            return true;

        return false;
    }

    private void PopulateSubtitles()
    {
        object? previouslySelected = (CmbSubtitleLang.SelectedItem as ComboBoxItem)?.Tag;

        CmbSubtitleLang.Items.Clear();

        var cleaned = new List<SubtitleTrackInfo>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in _subtitles)
        {
            var u = s.Url?.Trim() ?? "";
            var uLower = u.ToLowerInvariant();

            // Exclude video/audio HLS playlists while keeping legitimate HLS subtitle playlists (#EXT-X-MEDIA:TYPE=SUBTITLES)
            if (!string.IsNullOrEmpty(uLower) && IsVideoOrAudioHlsPlaylist(uLower, s))
                continue;

            var lang = (!string.IsNullOrWhiteSpace(s.Lang) ? s.Lang : s.Code ?? "").Trim();
            var label = (s.Label ?? "").Trim();
            bool isForced = uLower.Contains("forced") || uLower.Contains("zorunlu") || label.Contains("Forced", StringComparison.OrdinalIgnoreCase) || label.Contains("Zorunlu", StringComparison.OrdinalIgnoreCase);
            bool isAuto = label.Contains("Otomatik", StringComparison.OrdinalIgnoreCase) || uLower.Contains("kind=asr") || uLower.Contains("auto");

            // Check for explicit lang query parameter in YouTube/timedtext URLs (e.g. &lang=tr, &tlang=tr)
            var ytLangMatch = System.Text.RegularExpressions.Regex.Match(u, @"[?&](?:tlang|lang)=([a-zA-Z0-9_-]+)");
            if (ytLangMatch.Success)
            {
                lang = ytLangMatch.Groups[1].Value.ToLowerInvariant();
            }
            else if (string.IsNullOrEmpty(lang) || lang == "und" || (lang == "tr" && label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase)))
            {
                var match = System.Text.RegularExpressions.Regex.Match(uLower, @"(?:^|[-_./])(tr|tur|turkish|turkce|en|eng|english|pt|por|portuguese|es|spa|spanish|fr|fre|french|de|ger|german|it|ita|italian|ru|rus|russian|ar|ara|arabic)(?:[-_./?]|$)");
                if (match.Success)
                {
                    var val = match.Groups[1].Value;
                    if (val.StartsWith("tr") || val.StartsWith("tur")) lang = "tr";
                    else if (val.StartsWith("en") || val.StartsWith("eng")) lang = "en";
                    else if (val.StartsWith("pt") || val.StartsWith("por")) lang = "pt";
                    else if (val.StartsWith("es") || val.StartsWith("spa")) lang = "es";
                    else if (val.StartsWith("fr")) lang = "fr";
                    else if (val.StartsWith("de") || val.StartsWith("ger")) lang = "de";
                    else if (val.StartsWith("it")) lang = "it";
                    else if (val.StartsWith("ru")) lang = "ru";
                    else if (val.StartsWith("ar")) lang = "ar";
                }
                else if (uLower.Contains("altyazi"))
                {
                    lang = "tr";
                }
            }

            if (!string.IsNullOrEmpty(u) && !seenUrls.Add($"{u}|{lang}"))
                continue;

            string finalLabel;
            bool isTr = Strings.IsTurkish;
            if (!string.IsNullOrEmpty(label) && !label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase) && !label.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) && !label.Equals("Subtitles", StringComparison.OrdinalIgnoreCase))
            {
                if (isTr)
                    finalLabel = label.Contains("Altyazı", StringComparison.OrdinalIgnoreCase) ? label : $"{label} Altyazı";
                else
                    finalLabel = (label.Contains("Subtitle", StringComparison.OrdinalIgnoreCase) || label.Contains("Subtitles", StringComparison.OrdinalIgnoreCase)) ? label : $"{label} Subtitles";
            }
            else
            {
                finalLabel = StreamProbeService.FormatSubtitleLabel(lang, isAuto, isForced, isTr);
            }

            string dedupKey = $"{lang}_{finalLabel}";
            if (!seenKeys.Add(dedupKey))
                continue;

            cleaned.Add(new SubtitleTrackInfo { Url = s.Url ?? "", Lang = lang, Code = lang, Label = finalLabel });
        }

        // Sort: Non-forced Turkish first, then forced Turkish, then auto Turkish, then English, then others
        cleaned.Sort((a, b) =>
        {
            int Score(SubtitleTrackInfo x)
            {
                bool isTr = x.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
                bool isEn = x.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                bool isForced = x.Label?.Contains("Zorunlu") == true;
                bool isAuto = x.Label?.Contains("Otomatik") == true;

                if (isTr && !isForced && !isAuto) return 0;
                if (isTr && isForced) return 1;
                if (isTr && isAuto) return 2;
                if (isEn && !isForced && !isAuto) return 3;
                if (isEn && isAuto) return 4;
                if (!isAuto) return 5;
                return 6;
            }

            int sa = Score(a);
            int sb = Score(b);
            if (sa != sb) return sa.CompareTo(sb);
            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });

        _subtitles = cleaned;

        if (_subtitles.Count > 0)
        {
            ComboBoxItem? itemToSelect = null;
            foreach (var s in _subtitles)
            {
                var cbi = new ComboBoxItem
                {
                    Content = s.Label,
                    Tag = s
                };
                CmbSubtitleLang.Items.Add(cbi);

                if (previouslySelected != null)
                {
                    if (previouslySelected is SubtitleTrackInfo prevSti && prevSti.Lang.Equals(s.Lang, StringComparison.OrdinalIgnoreCase))
                        itemToSelect = cbi;
                    else if (previouslySelected is string prevStr && prevStr.Equals(s.Lang, StringComparison.OrdinalIgnoreCase))
                        itemToSelect = cbi;
                }
            }
            if (_subtitles.Count > 1)
            {
                var allItem = new ComboBoxItem
                {
                    Content = Strings.VideoDialogDownloadAllSubs,
                    Tag = "all"
                };
                CmbSubtitleLang.Items.Add(allItem);
                if (previouslySelected is string pStr && pStr == "all") itemToSelect = allItem;
            }
            CmbSubtitleLang.SelectedItem = itemToSelect ?? CmbSubtitleLang.Items[0];
            ChkDownloadSubtitles.IsChecked = true;
        }
        else
        {
            CmbSubtitleLang.Items.Add(new ComboBoxItem
            {
                Content = Strings.VideoDialogNoSubtitles,
                Tag = "none"
            });
            CmbSubtitleLang.SelectedIndex = 0;
            ChkDownloadSubtitles.IsChecked = false;
        }
    }

    private async Task ProbeStreamAsync()
    {
        try
        {
            TxtProbeStatus.Visibility = Visibility.Visible;
            TxtProbeStatus.Text = Strings.VideoDialogProbing;

            var launcher = _toolLauncher;
            if (launcher == null)
            {
                var tp = new ToolPaths();
                if (tp.YtDlpPath is { Length: > 0 } yp)
                    launcher = new ToolLauncher(yp);
            }

            var result = await Task.Run(() => StreamProbeService.ProbeAsync(
                _url,
                _request?.Referer,
                _request?.UserAgent,
                _request?.Cookies,
                _request?.CookieHeader,
                launcher,
                _cts.Token), _cts.Token);

            if (_cts.IsCancellationRequested) return;

            await Dispatcher.InvokeAsync(() =>
            {
                bool updated = false;
                if (!string.IsNullOrWhiteSpace(result.ResolvedUrl)) { ResolvedUrl = result.ResolvedUrl; TxtVideoUrl.Text = result.ResolvedUrl; }
                if (result.Qualities.Count > 0)
                {
                    _qualities = result.Qualities;
                    PopulateQualities();
                    updated = true;
                }

                if (result.AudioTracks.Count > 0)
                {
                    // Merge probed audio tracks with browser-detected ones so we never lose browser-detected dubbing tracks
                    var mergedAudios = new Dictionary<string, AudioTrackInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in _audioTracks)
                    {
                        string key = !string.IsNullOrEmpty(a.TrackId) ? a.TrackId : a.Lang;
                        if (!string.IsNullOrEmpty(key)) mergedAudios[key] = a;
                    }
                    foreach (var a in result.AudioTracks)
                    {
                        string key = !string.IsNullOrEmpty(a.TrackId) ? a.TrackId : a.Lang;
                        if (!string.IsNullOrEmpty(key)) mergedAudios[key] = a;
                    }
                    _audioTracks = StreamProbeService.SortAudioTracks(mergedAudios.Values);
                    PopulateAudioTracks();
                    updated = true;
                }

                if (result.Subtitles.Count > 0)
                {
                    bool hasOnlyPlaceholders = _subtitles.All(s => string.IsNullOrEmpty(s.Url));
                    if (hasOnlyPlaceholders)
                    {
                        _subtitles = new List<SubtitleTrackInfo>(result.Subtitles);
                    }
                    else
                    {
                        foreach (var s in result.Subtitles)
                        {
                            if (!_subtitles.Any(x => x.Lang.Equals(s.Lang, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(x.Url) && x.Url == s.Url)))
                                _subtitles.Add(s);
                        }
                    }
                    PopulateSubtitles();
                    updated = true;
                }
                else if (_subtitles.Any(s => string.IsNullOrEmpty(s.Url)))
                {
                    _subtitles.Clear();
                    PopulateSubtitles();
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(result.Title) && (TxtVideoTitle.Text == "Video İndirmesi" || TxtVideoTitle.Text == "Video Download"))
                {
                    TxtVideoTitle.Text = result.Title;
                    DetectedTitle = result.Title;
                }
                else if (!string.IsNullOrWhiteSpace(result.Title) && string.IsNullOrWhiteSpace(DetectedTitle))
                {
                    DetectedTitle = result.Title;
                }

                TxtProbeStatus.Text = updated ? Strings.VideoDialogReady : "";
                if (!updated) TxtProbeStatus.Visibility = Visibility.Collapsed;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtProbeStatus.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        DownloadSubtitles = ChkDownloadSubtitles.IsChecked == true;
        if (CmbSubtitleLang.SelectedItem is ComboBoxItem cbi)
        {
            if (cbi.Tag is SubtitleTrackInfo track)
            {
                SelectedSubtitleUrl = track.Url;
                string rawLang = !string.IsNullOrWhiteSpace(track.Lang) ? track.Lang : (track.Code ?? "tr");
                string cleanLang = rawLang.Split('-')[0].ToLowerInvariant();
                SubtitleLangs = cleanLang switch
                {
                    "tr" => "tr,tur,Turkish,turkish,tr-TR,tr-tr",
                    "en" => "en,eng,English,english,en-US,en-us",
                    _ => $"{rawLang},{cleanLang}.*,{cleanLang}"
                };
            }
            else if (cbi.Tag is string tag)
            {
                if (tag == "none") DownloadSubtitles = false;
                else if (tag == "all") SubtitleLangs = "all";
                else SubtitleLangs = tag;
            }
        }

        AudioTrackInfo? selectedAudioTrack = null;
        string audioTrackChoice = "best";
        if (CmbAudioTrack.SelectedItem is ComboBoxItem abi)
        {
            if (abi.Tag is AudioTrackInfo ati)
            {
                selectedAudioTrack = ati;
                audioTrackChoice = ati.TrackId;
            }
            else if (abi.Tag is string aTag)
            {
                audioTrackChoice = aTag;
            }
        }

        var audioTokensList = new List<string>();
        if (selectedAudioTrack != null)
        {
            string lang = selectedAudioTrack.Lang?.Trim() ?? "";
            string cleanLang = lang.Split('-')[0].ToLowerInvariant();

            // Put language-specific selectors BEFORE raw FormatId if FormatId is a generic YouTube itag (e.g. "251" or "140")
            // because on multi-audio YouTube videos, yt-dlp names tracks "251-0", "251-1", etc. with [language=tr]
            bool hasSpecificFormatId = !string.IsNullOrEmpty(selectedAudioTrack.FormatId) &&
                                       selectedAudioTrack.FormatId != "251" &&
                                       selectedAudioTrack.FormatId != "140" &&
                                       selectedAudioTrack.FormatId != "139";

            if (hasSpecificFormatId)
            {
                audioTokensList.Add($"ba[format_id={selectedAudioTrack.FormatId}]");
                audioTokensList.Add($"ba[format_id*={selectedAudioTrack.FormatId}]");
            }

            if (selectedAudioTrack.TrackId == "tr" || cleanLang == "tr")
            {
                audioTokensList.Add("ba[language^=tr]");
                audioTokensList.Add("ba[language=tur]");
                audioTokensList.Add("ba[format_note*=Turkish]");
                audioTokensList.Add("ba[format_note*=Türkçe]");
                audioTokensList.Add("ba[format_id*=Turkish]");
                audioTokensList.Add("ba[format_id*=Turk]");
                audioTokensList.Add("ba[format_id*=tr]");
            }
            else if (selectedAudioTrack.TrackId == "original" || selectedAudioTrack.IsDefault)
            {
                if (!string.IsNullOrEmpty(cleanLang) && cleanLang != "und")
                {
                    audioTokensList.Add($"ba[language^={cleanLang}][format_note*=original]");
                    audioTokensList.Add($"ba[language^={cleanLang}][format_note*=Original]");
                }
                audioTokensList.Add("ba[format_note*=Original]");
                audioTokensList.Add("ba[format_note*=original]");
                audioTokensList.Add("ba[format_note*=default]");
                audioTokensList.Add("ba[format_id*=Original]");
                if (!string.IsNullOrEmpty(cleanLang) && cleanLang != "und")
                {
                    audioTokensList.Add($"ba[language^={cleanLang}]");
                }
            }
            else if (!string.IsNullOrEmpty(cleanLang) && cleanLang != "und")
            {
                audioTokensList.Add($"ba[language^={cleanLang}]");
                if (lang != cleanLang) audioTokensList.Add($"ba[language^={lang}]");
                var langInfo = StreamProbeService.GetLanguageInfo(cleanLang);
                if (langInfo.Turkish != "Bilinmeyen")
                {
                    audioTokensList.Add($"ba[format_note*={langInfo.Turkish}]");
                    audioTokensList.Add($"ba[format_note*={langInfo.Native}]");
                }
            }

            if (!hasSpecificFormatId && !string.IsNullOrEmpty(selectedAudioTrack.FormatId))
            {
                audioTokensList.Add($"ba[format_id={selectedAudioTrack.FormatId}]");
                audioTokensList.Add($"ba[format_id^={selectedAudioTrack.FormatId}]");
            }

            audioTokensList.Add("ba");
        }
        else
        {
            switch (audioTrackChoice)
            {
                case "tr":
                    audioTokensList.AddRange(new[] { "ba[language^=tr]", "ba[language=tur]", "ba[format_note*=Turkish]", "ba[format_note*=Türkçe]", "ba[format_id*=Turkish]", "ba[format_id*=Turk]", "ba" });
                    break;
                case "original":
                    audioTokensList.AddRange(new[] { "ba[format_note*=Original]", "ba[format_note*=original]", "ba[format_note*=default]", "ba[format_id*=Original]", "ba[format_id*=Eng]", "ba[format_note*=Eng]", "ba[language^=en]", "ba" });
                    break;
                case "best":
                    audioTokensList.Add("ba");
                    break;
                default:
                    audioTokensList.AddRange(new[] { $"ba[format_id={audioTrackChoice}]", $"ba[language^={audioTrackChoice}]", "ba" });
                    break;
            }
        }
        string[] audioTokens = audioTokensList.Distinct().ToArray();

        string qualityChoice = "best";
        foreach (var rb in PnlQualityOptions.Children.OfType<RadioButton>())
        {
            if (rb.IsChecked == true && rb.Tag is string qTag)
            {
                qualityChoice = qTag;
                break;
            }
        }

        if (qualityChoice == "audio")
        {
            SelectedFormat = string.Join("/", audioTokens) + "/ba/bestaudio/best";
            AudioOnly = true;
        }
        else if (int.TryParse(qualityChoice, out int h) && h > 0)
        {
            var pairs = new List<string>();
            if (h > 1080)
            {
                // 2K (1440p) / 4K (2160p) / 8K (4320p): do NOT restrict to [ext=mp4] first,
                // because 2K/4K streams on YouTube and many CDNs are VP9/AV1 in webm/mp4 and
                // [height<=2160][ext=mp4] would match 1080p H.264 instead of 4K/2K!
                foreach (var a in audioTokens)
                {
                    pairs.Add($"bv*[height<={h}]+{a}");
                }
                pairs.Add($"bv*[height<={h}]+ba");
                pairs.Add($"b[height<={h}]");
            }
            else
            {
                foreach (var a in audioTokens)
                {
                    pairs.Add($"bv*[height<={h}][ext=mp4]+{a}[ext=m4a]");
                    pairs.Add($"bv*[height<={h}]+{a}");
                }
                pairs.Add($"bv*[height<={h}]+ba");
                pairs.Add($"b[height<={h}]");
            }
            foreach (var a in audioTokens)
            {
                pairs.Add($"bv*+{a}");
            }
            pairs.Add("bv*+ba");
            pairs.Add("b");
            pairs.Add("best");
            SelectedFormat = string.Join("/", pairs.Distinct());
        }
        else
        {
            var pairs = new List<string>();
            foreach (var a in audioTokens)
            {
                pairs.Add($"bv*+{a}");
            }
            pairs.Add("bv*+ba");
            pairs.Add("b");
            pairs.Add("best");
            SelectedFormat = string.Join("/", pairs.Distinct());
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
