using System.Globalization;
using System.Resources;

namespace McDm.App.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new ResourceManager("McDm.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

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
}
