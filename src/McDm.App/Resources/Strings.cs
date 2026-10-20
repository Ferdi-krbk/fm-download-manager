using System;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace McDm.App.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new ResourceManager("McDm.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static void SetLanguage(string? lang)
    {
        CultureInfo ci;
        if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase))
            ci = new CultureInfo("tr-TR");
        else if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
            ci = new CultureInfo("en-US");
        else
            ci = CultureInfo.InstalledUICulture ?? CultureInfo.CurrentUICulture;

        Culture = ci;
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = ci;
    }

    public static bool IsTurkish => (Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    public static string Get(string name) =>
        ResourceManager.GetString(name, Culture) ?? name;

    public static string AppTitle => Get(nameof(AppTitle));
    public static string AddFromClipboard => Get(nameof(AddFromClipboard));
    public static string AddUrls => Get(nameof(AddUrls));
    public static string Pause => Get(nameof(Pause));
    public static string Resume => Get(nameof(Resume));
    public static string Cancel => Get(nameof(Cancel));
    public static string Remove => Get(nameof(Remove));
    public static string RemoveAndDelete => Get(nameof(RemoveAndDelete));
    public static string Open => Get(nameof(Open));
    public static string OpenFolder => Get(nameof(OpenFolder));
    public static string CopyUrl => Get(nameof(CopyUrl));
    public static string Details => Get(nameof(Details));
    public static string Settings => Get(nameof(Settings));
    public static string BrowserSetup => Get(nameof(BrowserSetup));
    public static string CopyPairingCode => Get(nameof(CopyPairingCode));
    public static string PairingCodeCopied => Get(nameof(PairingCodeCopied));
    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));
    public static string CategoryAll => Get(nameof(CategoryAll));
    public static string CategoryImages => Get(nameof(CategoryImages));
    public static string CategoryMusic => Get(nameof(CategoryMusic));
    public static string CategoryVideo => Get(nameof(CategoryVideo));
    public static string CategoryApps => Get(nameof(CategoryApps));
    public static string CategoryDocuments => Get(nameof(CategoryDocuments));
    public static string CategoryArchives => Get(nameof(CategoryArchives));
    public static string CategoryOther => Get(nameof(CategoryOther));
    public static string ColName => Get(nameof(ColName));
    public static string ColSize => Get(nameof(ColSize));
    public static string ColProgress => Get(nameof(ColProgress));
    public static string ColSpeed => Get(nameof(ColSpeed));
    public static string ColConnections => Get(nameof(ColConnections));
    public static string ColStatus => Get(nameof(ColStatus));
    public static string ColError => Get(nameof(ColError));
    public static string StatusReady => Get(nameof(StatusReady));
    public static string DownloadPromptTitle => Get(nameof(DownloadPromptTitle));
    public static string DownloadPromptMessage => Get(nameof(DownloadPromptMessage));
    public static string DownloadComplete => Get(nameof(DownloadComplete));
    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string SettingsDownloadFolder => Get(nameof(SettingsDownloadFolder));
    public static string SettingsBrowse => Get(nameof(SettingsBrowse));
    public static string SettingsConnections => Get(nameof(SettingsConnections));
    public static string SettingsMaxConcurrent => Get(nameof(SettingsMaxConcurrent));
    public static string SettingsSpeedLimit => Get(nameof(SettingsSpeedLimit));
    public static string SettingsMinSize => Get(nameof(SettingsMinSize));
    public static string SettingsCaptureExtensions => Get(nameof(SettingsCaptureExtensions));
    public static string SettingsStartWithWindows => Get(nameof(SettingsStartWithWindows));
    public static string SettingsClipboardWatcher => Get(nameof(SettingsClipboardWatcher));
    public static string SettingsNotifications => Get(nameof(SettingsNotifications));
    public static string SettingsVideoFormat => Get(nameof(SettingsVideoFormat));
    public static string SettingsVideoQuality => Get(nameof(SettingsVideoQuality));
    public static string SettingsLanguage => Get(nameof(SettingsLanguage));
    public static string SettingsSave => Get(nameof(SettingsSave));
    public static string SettingsCancel => Get(nameof(SettingsCancel));
    public static string WizardTitle => Get(nameof(WizardTitle));
    public static string WizardSubtitle => Get(nameof(WizardSubtitle));
    public static string WizardStep1 => Get(nameof(WizardStep1));
    public static string WizardOpenFolder => Get(nameof(WizardOpenFolder));
    public static string WizardStep2 => Get(nameof(WizardStep2));
    public static string WizardOpenChrome => Get(nameof(WizardOpenChrome));
    public static string WizardOpenEdge => Get(nameof(WizardOpenEdge));
    public static string WizardOpenBrave => Get(nameof(WizardOpenBrave));
    public static string WizardStep3 => Get(nameof(WizardStep3));
    public static string WizardStep4 => Get(nameof(WizardStep4));
    public static string WizardCopyCode => Get(nameof(WizardCopyCode));
    public static string WizardConnectionStatus => Get(nameof(WizardConnectionStatus));
    public static string WizardWaiting => Get(nameof(WizardWaiting));
    public static string WizardConnected => Get(nameof(WizardConnected));
    public static string WizardClose => Get(nameof(WizardClose));
    public static string DetailsTitle => Get(nameof(DetailsTitle));
    public static string DetailsFile => Get(nameof(DetailsFile));
    public static string DetailsUrl => Get(nameof(DetailsUrl));
    public static string DetailsStatus => Get(nameof(DetailsStatus));
    public static string DetailsDownloaded => Get(nameof(DetailsDownloaded));
    public static string DetailsSpeed => Get(nameof(DetailsSpeed));
    public static string DetailsSegments => Get(nameof(DetailsSegments));
    public static string AddUrlsTitle => Get(nameof(AddUrlsTitle));
    public static string AddUrlsPrompt => Get(nameof(AddUrlsPrompt));
    public static string AddUrlsButton => Get(nameof(AddUrlsButton));
    public static string TrayOpen => Get(nameof(TrayOpen));
    public static string TrayQuit => Get(nameof(TrayQuit));
    public static string TrayStartWithWindows => Get(nameof(TrayStartWithWindows));
    public static string UpdateYtDlp => Get(nameof(UpdateYtDlp));
    public static string UpdatingYtDlp => Get(nameof(UpdatingYtDlp));
    public static string YtDlpUpdated => Get(nameof(YtDlpUpdated));
    public static string YtDlpUpdateFailed => Get(nameof(YtDlpUpdateFailed));

    // Common & Extended UI Localizations
    public static string Yes => IsTurkish ? "Evet" : "Yes";
    public static string No => IsTurkish ? "Hayır" : "No";
    public static string Ok => IsTurkish ? "Tamam" : "OK";
    public static string Browse => IsTurkish ? "Gözat..." : "Browse...";
    public static string CategoryTorrents => IsTurkish ? "Torrent / Magnet" : "Torrents / Magnet";
    public static string TimeLeft => IsTurkish ? "Kalan Süre" : "Time Left";
    public static string Completed => IsTurkish ? "Tamamlandı" : "Completed";
    public static string Unlimited => IsTurkish ? "Sınırsız" : "Unlimited";
    public static string SpeedLimit => IsTurkish ? "Hız Limiti" : "Speed Limit";
    public static string Active => IsTurkish ? "aktif" : "active";
    public static string Queued => IsTurkish ? "sırada" : "queued";
    public static string NoDownloadsTitle => IsTurkish ? "Henüz indirme yok" : "No downloads yet";
    public static string NoDownloadsSub => IsTurkish ? "İndirme başlatmak için bir bağlantı yapıştırın veya tarayıcı eklentisini kullanın." : "Paste a link to start downloading or use the browser extension.";
    public static string SelectFolder => IsTurkish ? "İndirilecek klasörü seçin:" : "Select download directory:";
    public static string SaveFolder => IsTurkish ? "Kaydedilecek Klasör:" : "Save Folder:";
    public static string FileName => IsTurkish ? "Dosya Adı:" : "File Name:";
    public static string WebAddress => IsTurkish ? "Web Bağlantısı (URL):" : "Web Address (URL):";
    public static string DownloadPromptQuestion => IsTurkish ? "Bu dosya FM Download Manager ile indirilsin mi?" : "Do you want to download this file with FM Download Manager?";
    public static string DownloadPromptTitleBar => IsTurkish ? "FM Download Manager — İndirme Onayı" : "FM Download Manager — Download Confirmation";
    public static string DownloadNow => IsTurkish ? "Şimdi İndir" : "Download Now";
    public static string AddUrlsTitleBar => IsTurkish ? "Bağlantı(lar) Ekle" : "Add Download Link(s)";
    public static string AddUrlsPromptText => IsTurkish ? "Her satıra bir indirme bağlantısı girin:" : "Enter one download URL per line:";
    public static string ValidLinksCount(int count) => IsTurkish ? $"{count} GEÇERLİ BAĞLANTI" : $"{count} VALID LINK{(count == 1 ? "" : "S")}";
    public static string VideoDialogTitle => IsTurkish ? "Video Kalitesi & Formatı Seçin" : "Select Video Quality & Format";
    public static string VideoDialogFormatOption => IsTurkish ? "KALİTE VE FORMAT SEÇENEĞİ:" : "QUALITY & FORMAT OPTIONS:";
    public static string VideoDialogProbing => IsTurkish ? "🔍 Akış formatları taranıyor..." : "🔍 Probing stream formats...";
    public static string VideoDialogBestTitle => IsTurkish ? "En Yüksek Kalite (Otomatik En İyi)" : "Best Quality (Auto Best)";
    public static string VideoDialogBestSub => IsTurkish ? "En iyi video ve ses akışlarını birleştirir (Önerilen)" : "Combines best video and audio streams (Recommended)";
    public static string VideoDialogAudioTitle => IsTurkish ? "Yalnızca Ses Çıkar (MP3 / 320 kbps)" : "Extract Audio Only (MP3 / 320 kbps)";
    public static string VideoDialogAudioSub => IsTurkish ? "Sesi otomatik en yüksek kalitede MP3'e dönüştürür" : "Automatically converts audio to high-bitrate MP3";
    public static string VideoDialogAudioTrack => IsTurkish ? "🔊 SES PARÇASI (DUBLAJ / ORİJİNAL):" : "🔊 AUDIO TRACK (DUB / ORIGINAL):";
    public static string VideoDialogSubtitles => IsTurkish ? "📝 ALTYAZI DİLİ (SEÇENEKSEL):" : "📝 SUBTITLE LANGUAGE (OPTIONAL):";
    public static string VideoDialogEmbedSubs => IsTurkish ? "Altyazıyı videonun içine kalıcı göm (Hardsub / MKV)" : "Embed subtitles directly into video (Softsub / MKV)";
    public static string VideoDialogStart => IsTurkish ? "İNDİRMEYİ BAŞLAT" : "START DOWNLOAD";
    public static string SettingsSchedulerTitle => IsTurkish ? "ZAMANLAYICI / KUYRUK PLANLAYICI" : "SCHEDULER / QUEUE PLANNER";
    public static string SettingsSchedulerEnable => IsTurkish ? "Zamanlayıcıyı etkinleştir (Planlanan saat aralığında çalış)" : "Enable scheduler (Run during scheduled hours)";
    public static string SettingsSchedulerStart => IsTurkish ? "BAŞLANGIÇ SAATİ (SS:dd)" : "START TIME (HH:mm)";
    public static string SettingsSchedulerStop => IsTurkish ? "BİTİŞ SAATİ (SS:dd)" : "STOP TIME (HH:mm)";
    public static string SettingsSchedulerAutoResume => IsTurkish ? "Başlangıç saatinde duraklatılan indirmeleri otomatik başlat" : "Automatically resume paused downloads at start time";
    public static string SettingsSchedulerAutoPause => IsTurkish ? "Bitiş saatinde çalışan indirmeleri otomatik duraklat" : "Automatically pause running downloads at stop time";
    public static string SettingsAutoCategorize => IsTurkish ? "Kategoriye göre alt klasörleme yap (Videos, Music, Torrents vb.)" : "Auto-categorize into subfolders (Videos, Music, Torrents etc.)";
    public static string SettingsAccentColor => IsTurkish ? "VURGU RENGİ / PALET (ACCENT COLOR)" : "ACCENT COLOR PALETTE";
    public static string SettingsCustomHex => IsTurkish ? "Özel HEX:" : "Custom HEX:";
    public static string SettingsPreview => IsTurkish ? "ÖNİZLE" : "PREVIEW";
    public static string ActiveConnections(int count) => IsTurkish ? $"{count} AKTİF" : $"{count} ACTIVE";
    public static string DetailsSegmentsHeader => IsTurkish ? "SEGMENT BAĞLANTILARI" : "SEGMENT CONNECTIONS";
    public static string DetailsRangeCol => IsTurkish ? "ARALIK" : "RANGE";
    public static string DetailsProgressCol => IsTurkish ? "İLERLEME" : "PROGRESS";
    public static string DetailsDownloadedCol => IsTurkish ? "İNDİRİLEN" : "DOWNLOADED";
    public static string PaletteTooltip => IsTurkish ? "Vurgu Rengi Paleti (Kişiselleştir)" : "Accent Color Palette (Customize)";
    public static string PaletteHeader => IsTurkish ? "VURGU RENGİ SEÇ" : "SELECT ACCENT COLOR";
    public static string PaletteOpenAllSettings => IsTurkish ? "Tüm Ayarları Aç..." : "Open All Settings...";
    public static string BrowserSetupHeader => IsTurkish ? "Tek Tıkla Tarayıcıya Kurulum" : "1-Click Browser Extension Setup";
    public static string BrowserSetupSub => IsTurkish ? "Tarayıcınızı seçin; eklenti otomatik olarak kurulacak ve eşleştirilecektir." : "Select your browser; the extension will be automatically installed and paired.";
    public static string BrowserSetupAutoSection => IsTurkish ? "OTOMATİK KURULUM SEÇENEKLERİ" : "AUTOMATIC SETUP OPTIONS";
    public static string BrowserSetupChromeTitle => IsTurkish ? "Google Chrome Eklenti Sayfasını Aç" : "Open Google Chrome Extensions Page";
    public static string BrowserSetupChromeSub => IsTurkish ? "Yolu panoya kopyalar, klasörü açar ve Chrome'u başlatır" : "Copies path, opens folder, and launches Chrome";
    public static string BrowserSetupEdgeTitle => IsTurkish ? "Microsoft Edge Eklenti Sayfasını Aç" : "Open Microsoft Edge Extensions Page";
    public static string BrowserSetupEdgeSub => IsTurkish ? "Yolu panoya kopyalar, klasörü açar ve Edge'i başlatır" : "Copies path, opens folder, and launches Edge";
    public static string BrowserSetupBraveTitle => IsTurkish ? "Brave Tarayıcı Eklenti Sayfasını Aç" : "Open Brave Browser Extensions Page";
    public static string BrowserSetupBraveSub => IsTurkish ? "Yolu panoya kopyalar, klasörü açar ve Brave'i başlatır" : "Copies path, opens folder, and launches Brave";
    public static string BrowserSetupManualSection => IsTurkish ? "MANUEL KURULUM ADIMLARI" : "MANUAL SETUP STEPS";
    public static string BrowserSetupPathTitle => IsTurkish ? "EKLENTİ KLASÖR YOLU" : "EXTENSION FOLDER PATH";
    public static string BrowserSetupTokenTitle => IsTurkish ? "GÜVENLİK EŞLEŞTİRME KODU (TOKEN)" : "SECURITY PAIRING TOKEN";
    public static string BrowserSetupCopy => IsTurkish ? "Kopyala" : "Copy";
    public static string BrowserSetupFolder => IsTurkish ? "Klasörü Aç" : "Open Folder";
    public static string BrowserSetupStatusTitle => IsTurkish ? "BAĞLANTI DURUMU" : "CONNECTION STATUS";
    public static string BrowserSetupWaiting => IsTurkish ? "Tarayıcı bekleniyor..." : "Waiting for browser...";
    public static string BrowserSetupConnected => IsTurkish ? "Bağlandı (Eklenti Aktif)" : "Connected (Extension Active)";

    // Video Quality Dialog
    public static string VideoDialogDefaultTitle => IsTurkish ? "Video İndirmesi" : "Video Download";
    public static string VideoDialogReady => IsTurkish ? "✓ Akış formatları hazır" : "✓ Stream formats ready";
    public static string VideoDialogTracksDetected(int count) => IsTurkish ? $"{count} Parça Tespit Edildi" : $"{count} Track{(count == 1 ? "" : "s")} Detected";
    public static string VideoDialogOriginalAudio => IsTurkish ? "Orijinal Ses" : "Original Audio";
    public static string VideoDialogNoSubtitles => IsTurkish ? "Harici Altyazı Yok (Video İçi Gömülü veya Yok)" : "No External Subtitles (Embedded or None)";
    public static string VideoDialogDownloadAllSubs => IsTurkish ? "Tüm Altyazıları İndir" : "Download All Subtitles";
    public static string VideoDialogSubtitlesSave => IsTurkish ? "💬 Altyazıyı videoya göm ve kaydet (.srt)" : "💬 Embed subtitles and save (.srt)";
    public static string VideoDialogSubtitleLangLabel => IsTurkish ? "Altyazı Dili:" : "Subtitle Language:";

    // Browser Setup Extra
    public static string BrowserSetupQuickTipTitle => IsTurkish ? "💡 2 Adımda Kolay Kurulum:" : "💡 2-Step Easy Setup:";
    public static string BrowserSetupQuickTipSteps => IsTurkish 
        ? "1. Butona tıklayın: Eklenti klasörü açılır ve yolu panonuza kopyalanır.\n2. Tarayıcıda sağ üstten 'Geliştirici modu'nu açın ve 'Paketlenmemiş öge yükle' diyerek bu klasörü seçin."
        : "1. Click the button: The extension folder opens and its path is copied to clipboard.\n2. In browser extensions page, enable 'Developer mode' and click 'Load unpacked' to select this folder.";
    public static string BrowserSetupQuickTipAuto => IsTurkish 
        ? "✓ Eşleştirme kodu gerekmez — Eklenti yüklendiği saniye otomatik bağlanır!" 
        : "✓ No pairing code needed — The extension connects automatically upon loading!";
    public static string BrowserSetupAdvanced => IsTurkish ? "Gelişmiş / Manuel Kurulum Bilgileri" : "Advanced / Manual Setup Details";
    public static string BrowserSetupFolderPath => IsTurkish ? "Eklenti Klasörü Konumu:" : "Extension Folder Location:";
    public static string BrowserSetupSecurityToken => IsTurkish ? "Eşleştirme Güvenlik Kodu:" : "Security Pairing Token:";
    public static string BrowserSetupComplete => IsTurkish ? "TAMAMLA" : "DONE";

    // Tray & Clipboard Prompts
    public static string TrayCheckUpdates => IsTurkish ? "Güncellemeleri Denetle..." : "Check for Updates...";
    public static string TrayNewVersionPrompt(string version) => IsTurkish 
        ? $"Yeni bir sürüm yayınlandı!\n\nFM Download Manager v{version}\n\nGüncellemeyi hemen indirip kurmak istiyor musunuz?" 
        : $"A new version is available!\n\nFM Download Manager v{version}\n\nWould you like to download and install this update now?";
    public static string TrayUpToDate(string current, string latest) => IsTurkish
        ? $"Uygulamanız güncel!\n\nKurulu Sürüm: v{current}\nSon Sürüm: v{latest}"
        : $"Your application is up to date!\n\nInstalled Version: v{current}\nLatest Version: v{latest}";
    public static string TrayCheckFailed => IsTurkish 
        ? "Güncellemeler denetlenirken GitHub bağlantısı kurulamadı." 
        : "Could not connect to GitHub while checking for updates.";
    public static string ClipboardMagnetPrompt => IsTurkish 
        ? "Panoda Magnet indirme bağlantısı algılandı. İndirmek istiyor musunuz?" 
        : "Magnet download link detected in clipboard. Do you want to download it?";
    public static string ClipboardTorrentPrompt => IsTurkish 
        ? "Panoda Torrent dosyası algılandı. İndirmek istiyor musunuz?" 
        : "Torrent file detected in clipboard. Do you want to download it?";
    public static string DownloadTorrentTitle => IsTurkish ? "Torrent İndir" : "Download Torrent";
    public static string ErrorOccurred => IsTurkish ? "Hata oluştu:\n" : "An error occurred:\n";
    public static string SchedulerTitle => IsTurkish ? "Zamanlayıcı" : "Scheduler";
    public static string SchedulerStarted => IsTurkish ? "Planlanan indirme saatine girildi. İndirmeler başlatıldı." : "Scheduled download hours started. Downloads resumed.";
    public static string SchedulerStopped => IsTurkish ? "Planlanan indirme saati sona erdi. İndirmeler duraklatıldı." : "Scheduled download hours ended. Downloads paused.";
    public static string DragDropTitle => IsTurkish ? "İndirmeyi Başlatmak İçin Buraya Bırakın" : "Drop Here to Start Download";
    public static string DragDropSubtitle => IsTurkish ? "URL bağlantıları, web linkleri veya .torrent dosyaları desteklenir" : "Supports URL links, web addresses or .torrent files";
    public static string TorrentAdded => IsTurkish ? "Torrent indirmesi eklendi." : "Torrent download added.";
    public static string InvalidUrl => IsTurkish ? "Geçersiz indirme bağlantısı." : "Invalid download URL.";
}
