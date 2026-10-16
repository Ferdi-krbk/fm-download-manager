using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using McDm.Core;

namespace McDm.App;

public partial class VideoQualityDialog : Window
{
    public string SelectedFormat { get; private set; } = "bestvideo+bestaudio/best";
    public bool AudioOnly { get; private set; } = false;
    public bool DownloadSubtitles { get; private set; } = true;
    public string SubtitleLangs { get; private set; } = "tr,en";
    public string? SelectedSubtitleUrl { get; private set; }

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
        _url = url;
        _request = request;
        _toolLauncher = toolLauncher;

        TxtVideoUrl.Text = url;
        TxtVideoTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video İndirmesi" : title;
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
                new() { Height = 720, Label = "720p HD (Hızlı İndirme)" },
                new() { Height = 480, Label = "480p SD" },
                new() { Height = 360, Label = "360p Düşük" }
            };
        }

        if (_url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            _url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            if (_subtitles.Count == 0)
            {
                _subtitles = new List<SubtitleTrackInfo>
                {
                    new() { Lang = "tr", Label = "Türkçe Altyazı" },
                    new() { Lang = "en", Label = "İngilizce Altyazı" },
                    new() { Lang = "all", Label = "Tüm Altyazılar" }
                };
                DownloadSubtitles = true;
                ChkDownloadSubtitles.IsChecked = true;
            }
        }

        if (_audioTracks.Count > 0)
        {
            _audioTracks = _audioTracks.OrderBy(a =>
            {
                bool isTr = a.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) || a.Label.Contains("Türkçe", StringComparison.OrdinalIgnoreCase) || a.TrackId == "tr";
                bool isOrig = a.IsDefault || a.Label.Contains("Orijinal", StringComparison.OrdinalIgnoreCase) || a.TrackId == "original";
                bool isEn = a.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

                if (isTr) return 0;
                if (isOrig) return 1;
                if (isEn) return 2;
                return 3;
            }).ThenBy(a => a.Label).ToList();
        }

        PopulateQualities();
        PopulateAudioTracks();
        PopulateSubtitles();

        Loaded += (_, _) => _ = ProbeStreamAsync();
        Closed += (_, _) => _cts.Cancel();
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
            "En Yüksek Kalite (Otomatik En İyi)",
            "4K / 2K / 1080p mevcut en yüksek video ve ses akışını birleştirir (Önerilen)",
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
                    >= 4320 => "Ultra yüksek çözünürlük (8K UHD)",
                    >= 2160 => "Ultra yüksek çözünürlük (4K UHD)",
                    >= 1440 => "Yüksek çözünürlük (2K QHD)",
                    >= 1080 => "Standart yüksek çözünürlük (Full HD)",
                    >= 720 => "Daha küçük dosya boyutu, hızlı aktarım (HD)",
                    _ => "Düşük boyutlu hızlı indirme"
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
            "Yalnızca Ses Çıkar (MP3 / 320 kbps)",
            "Görüntüyü atar, sadece müziği / sesi kaydeder",
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
        if (_audioTracks.Count > 0)
        {
            ComboBoxItem? itemToSelect = null;
            foreach (var a in _audioTracks)
            {
                string displayLabel = a.Label;
                if (!displayLabel.Contains("Dublaj", StringComparison.OrdinalIgnoreCase) &&
                    !displayLabel.Contains("Orijinal", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.IsDefault || displayLabel.Contains("original", StringComparison.OrdinalIgnoreCase))
                    {
                        displayLabel = $"Orijinal Ses ({displayLabel})";
                    }
                    else if (a.Lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) || displayLabel.Equals("Türkçe", StringComparison.OrdinalIgnoreCase))
                    {
                        displayLabel = "Türkçe Dublaj";
                    }
                    else
                    {
                        displayLabel = $"{displayLabel} Dublaj";
                    }
                }

                var cbi = new ComboBoxItem
                {
                    Content = displayLabel,
                    Tag = a
                };
                CmbAudioTrack.Items.Add(cbi);

                if (previouslySelected != null)
                {
                    if (previouslySelected is AudioTrackInfo prevAti && prevAti.TrackId == a.TrackId)
                        itemToSelect = cbi;
                    else if (previouslySelected is string prevStr && (prevStr == a.TrackId || prevStr == a.Lang))
                        itemToSelect = cbi;
                }
            }
            CmbAudioTrack.SelectedItem = itemToSelect ?? CmbAudioTrack.Items[0];
            TxtAudioTrackCount.Text = $"{_audioTracks.Count} Parça Tespit Edildi";
        }
        else
        {
            CmbAudioTrack.Items.Add(new ComboBoxItem
            {
                Content = "En Yüksek Kalite Ses Akışı (Varsayılan)",
                Tag = "best"
            });
            CmbAudioTrack.Items.Add(new ComboBoxItem
            {
                Content = "Türkçe Dublaj (Varsa)",
                Tag = "tr"
            });
            CmbAudioTrack.Items.Add(new ComboBoxItem
            {
                Content = "Orijinal Ses (Varsa)",
                Tag = "original"
            });
            CmbAudioTrack.SelectedIndex = 0;
            TxtAudioTrackCount.Text = "";
        }
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

            // Exclude HLS playlists, segments, and audio/video files
            if (uLower.Contains("sublist_") || uLower.Contains(".m3u8") || uLower.Contains("playlist") || uLower.Contains("manifest"))
                continue;
            if (uLower.Contains("/hls/") && !uLower.EndsWith(".vtt") && !uLower.EndsWith(".srt"))
                continue;

            if (!string.IsNullOrEmpty(u) && !seenUrls.Add(u))
                continue;

            var lang = (s.Lang ?? "").Trim();
            var label = (s.Label ?? "").Trim();
            bool isForced = uLower.Contains("forced") || uLower.Contains("zorunlu") || label.Contains("Forced", StringComparison.OrdinalIgnoreCase) || label.Contains("Zorunlu", StringComparison.OrdinalIgnoreCase);
            bool isAuto = label.Contains("Otomatik", StringComparison.OrdinalIgnoreCase) || uLower.Contains("auto");

            // Check for explicit lang query parameter in YouTube/timedtext URLs (e.g. &lang=tr, &lang=es)
            var ytLangMatch = System.Text.RegularExpressions.Regex.Match(u, @"[?&]lang=([a-zA-Z0-9_-]+)");
            if (ytLangMatch.Success)
            {
                lang = ytLangMatch.Groups[1].Value.ToLowerInvariant();
            }
            else if (string.IsNullOrEmpty(lang) || lang == "und" || (lang == "tr" && label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase)))
            {
                var match = System.Text.RegularExpressions.Regex.Match(uLower, @"(?:^|[-_./])(tr|tur|turkish|turkce|en|eng|english|pt|por|portuguese|es|spa|spanish|fr|fre|french|de|ger|german|it|ita|italian|ru|rus|russian|ar|ara|arabic)(?:[-_./]|$)");
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

            string finalLabel;
            if (!string.IsNullOrEmpty(label) && !label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase) && !label.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                finalLabel = label.Contains("Altyazı", StringComparison.OrdinalIgnoreCase) ? label : $"{label} Altyazı";
            }
            else
            {
                finalLabel = StreamProbeService.FormatSubtitleLabel(lang, isAuto, isForced);
            }

            string dedupKey = $"{lang}_{finalLabel}";
            if (!seenKeys.Add(dedupKey))
                continue;

            cleaned.Add(new SubtitleTrackInfo { Url = s.Url ?? "", Lang = lang, Label = finalLabel });
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
                    Content = "Tüm Altyazıları İndir",
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
                Content = "Harici Altyazı Yok (Video İçi Gömülü veya Yok)",
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
            TxtProbeStatus.Text = "🔍 Akış formatları taranıyor...";

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
                if (result.Qualities.Count > 0)
                {
                    _qualities = result.Qualities;
                    PopulateQualities();
                    updated = true;
                }

                if (result.AudioTracks.Count > 0)
                {
                    _audioTracks = result.AudioTracks;
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

                if (!string.IsNullOrWhiteSpace(result.Title) && TxtVideoTitle.Text == "Video İndirmesi")
                {
                    TxtVideoTitle.Text = result.Title;
                }

                TxtProbeStatus.Text = updated ? "✓ Akış formatları hazır" : "";
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
                string cleanLang = track.Lang.Split('-')[0].ToLowerInvariant();
                SubtitleLangs = cleanLang switch
                {
                    "tr" => "tr,tur,Turkish,turkish,tr-TR,tr-tr",
                    "en" => "en,eng,English,english,en-US,en-us",
                    _ => $"{track.Lang},{cleanLang}.*,{cleanLang}"
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
            if (!string.IsNullOrEmpty(selectedAudioTrack.FormatId))
            {
                audioTokensList.Add($"ba[format_id={selectedAudioTrack.FormatId}]");
                audioTokensList.Add($"ba[format_id*={selectedAudioTrack.FormatId}]");
            }

            string lang = selectedAudioTrack.Lang?.Trim() ?? "";
            string cleanLang = lang.Split('-')[0].ToLowerInvariant();

            if (selectedAudioTrack.TrackId == "tr" || cleanLang == "tr")
            {
                audioTokensList.Add("ba[language^=tr]");
                audioTokensList.Add("ba[format_note*=Turkish]");
                audioTokensList.Add("ba[format_note*=Türkçe]");
                audioTokensList.Add("ba[format_id*=Turkish]");
                audioTokensList.Add("ba[format_id*=Turk]");
            }
            else if (selectedAudioTrack.TrackId == "original" || selectedAudioTrack.IsDefault)
            {
                audioTokensList.Add("ba[format_note*=Original]");
                audioTokensList.Add("ba[format_note*=original]");
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

            audioTokensList.Add("ba");
        }
        else
        {
            switch (audioTrackChoice)
            {
                case "tr":
                    audioTokensList.AddRange(new[] { "ba[language^=tr]", "ba[format_note*=Turkish]", "ba[format_note*=Türkçe]", "ba[format_id*=Turkish]", "ba[format_id*=Turk]", "ba" });
                    break;
                case "original":
                    audioTokensList.AddRange(new[] { "ba[format_note*=Original]", "ba[format_id*=Original]", "ba[format_id*=Eng]", "ba[format_note*=Eng]", "ba[language^=en]", "ba" });
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
