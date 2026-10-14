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

        if (_url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            _url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            if (_qualities.Count == 0)
            {
                _qualities = new List<VideoQualityInfo>
                {
                    new() { Height = 1080, Label = "1080p Full HD (MP4)" },
                    new() { Height = 720, Label = "720p HD (Hızlı İndirme)" },
                    new() { Height = 480, Label = "480p SD" },
                    new() { Height = 360, Label = "360p Düşük" }
                };
            }
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

        PopulateQualities();
        PopulateAudioTracks();
        PopulateSubtitles();

        bool isYouTube = _url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                         _url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

        if (!isYouTube)
        {
            Loaded += (_, _) => _ = ProbeStreamAsync();
        }
        else
        {
            TxtProbeStatus.Visibility = Visibility.Collapsed;
        }

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
            "En iyi video ve ses akışlarını birleştirir (Önerilen)",
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
                    >= 1080 => "Standart yüksek çözünürlük (MP4)",
                    >= 720 => "Daha küçük dosya boyutu, hızlı aktarım",
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
        CmbAudioTrack.Items.Clear();
        if (_audioTracks.Count > 0)
        {
            foreach (var a in _audioTracks)
            {
                CmbAudioTrack.Items.Add(new ComboBoxItem
                {
                    Content = a.Label,
                    Tag = a.TrackId
                });
            }
            CmbAudioTrack.SelectedIndex = 0;
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
        CmbSubtitleLang.Items.Clear();

        var cleaned = new List<SubtitleTrackInfo>();
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

            var lang = (s.Lang ?? "").Trim().ToLowerInvariant();
            var label = (s.Label ?? "").Trim();
            bool isForced = uLower.Contains("forced") || uLower.Contains("zorunlu") || label.Contains("Forced", StringComparison.OrdinalIgnoreCase) || label.Contains("Zorunlu", StringComparison.OrdinalIgnoreCase);

            // Refine language code from isolated delimiters in URL if unknown or generic
            if (string.IsNullOrEmpty(lang) || lang == "und" || (lang == "tr" && label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase)))
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

            string baseName = lang switch
            {
                "tr" => "Türkçe",
                "en" => "İngilizce",
                "pt" => "Portekizce",
                "es" => "İspanyolca",
                "fr" => "Fransızca",
                "de" => "Almanca",
                "it" => "İtalyanca",
                "ru" => "Rusça",
                "ar" => "Arapça",
                _ => !string.IsNullOrEmpty(label) && !label.Equals("Altyazı", StringComparison.OrdinalIgnoreCase) ? label : "Altyazı"
            };

            var finalLabel = isForced ? $"{baseName} (Zorunlu)" : baseName;
            cleaned.Add(new SubtitleTrackInfo { Url = s.Url, Lang = lang, Label = finalLabel });
        }

        // Sort: Non-forced Turkish first, then forced Turkish, then English, then others
        cleaned.Sort((a, b) =>
        {
            bool aTr = a.Lang == "tr";
            bool bTr = b.Lang == "tr";
            if (aTr != bTr) return aTr ? -1 : 1;
            if (aTr && bTr)
            {
                bool aF = a.Label?.Contains("Zorunlu") == true;
                bool bF = b.Label?.Contains("Zorunlu") == true;
                if (aF != bF) return aF ? 1 : -1;
            }
            bool aEn = a.Lang == "en";
            bool bEn = b.Lang == "en";
            if (aEn != bEn) return aEn ? -1 : 1;
            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });

        _subtitles = cleaned;

        if (_subtitles.Count > 0)
        {
            foreach (var s in _subtitles)
            {
                CmbSubtitleLang.Items.Add(new ComboBoxItem
                {
                    Content = s.Label,
                    Tag = s
                });
            }
            if (_subtitles.Count > 1)
            {
                CmbSubtitleLang.Items.Add(new ComboBoxItem
                {
                    Content = "Tüm Altyazıları İndir",
                    Tag = "all"
                });
            }
            CmbSubtitleLang.SelectedIndex = 0;
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

            var result = await Task.Run(() => StreamProbeService.ProbeAsync(
                _url,
                _request?.Referer,
                _request?.UserAgent,
                _request?.Cookies,
                _request?.CookieHeader,
                _toolLauncher,
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
                    foreach (var s in result.Subtitles)
                    {
                        if (!_subtitles.Any(x => x.Lang == s.Lang || (x.Url.Length > 0 && x.Url == s.Url)))
                            _subtitles.Add(s);
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
                SubtitleLangs = track.Lang switch
                {
                    "tr" => "tr,tur,Turkish,turkish,tr-TR,tr-tr",
                    "en" => "en,eng,English,english,en-US,en-us",
                    _ => track.Lang
                };
            }
            else if (cbi.Tag is string tag)
            {
                if (tag == "none") DownloadSubtitles = false;
                else if (tag == "all") SubtitleLangs = "all";
                else SubtitleLangs = tag;
            }
        }

        string audioTrackChoice = "best";
        if (CmbAudioTrack.SelectedItem is ComboBoxItem abi && abi.Tag is string aTag)
        {
            audioTrackChoice = aTag;
        }

        string[] audioTokens = audioTrackChoice switch
        {
            "tr" => new[] { "ba[format_id*=Turkish]", "ba[format_note*=Turkish]", "ba[format_id*=Turk]", "ba[language^=tr]", "ba" },
            "original" => new[] { "ba[format_id*=Original]", "ba[format_note*=Original]", "ba[format_id*=Eng]", "ba[format_note*=Eng]", "ba[language^=en]", "ba[format_id!*=Turkish]", "ba[format_note!*=Turkish]", "ba" },
            "best" => new[] { "ba" },
            _ => new[] { $"ba[format_id={audioTrackChoice}]", $"ba[format_id*={audioTrackChoice}]", "ba" }
        };

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
            foreach (var a in audioTokens)
            {
                pairs.Add($"bv*[height<={h}][ext=mp4]+{a}[ext=m4a]");
                pairs.Add($"bv*[height<={h}]+{a}");
            }
            pairs.Add($"bv*[height<={h}]+ba");
            pairs.Add($"b[height<={h}]");
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
                pairs.Add($"bv*[ext=mp4]+{a}[ext=m4a]");
                pairs.Add($"bv*+{a}");
            }
            pairs.Add("bv*+ba");
            pairs.Add("b[ext=mp4]");
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
