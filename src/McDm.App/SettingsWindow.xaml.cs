using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using McDm.App.Resources;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace McDm.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onSave;

    public SettingsWindow(AppSettings settings, Action<AppSettings> onSave)
    {
        InitializeComponent();
        _settings = settings;
        _onSave = onSave;

        ApplyLocalization();
        LoadValues();
    }

    private void ApplyLocalization()
    {
        Title = Strings.SettingsTitle;
        TxtWinHeader.Text = Strings.SettingsTitle;
        LblDlFolder.Text = Strings.SettingsDownloadFolder;
        BtnBrowse.Content = Strings.SettingsBrowse;
        LblConnections.Text = Strings.SettingsConnections;
        LblMaxConcurrent.Text = Strings.SettingsMaxConcurrent;
        LblSpeedLimit.Text = Strings.SettingsSpeedLimit;
        LblMinSize.Text = Strings.SettingsMinSize;
        LblCaptureExts.Text = Strings.SettingsCaptureExtensions;
        LblVideoFormat.Text = Strings.SettingsVideoFormat;
        LblVideoQuality.Text = Strings.SettingsVideoQuality;
        LblLanguage.Text = Strings.SettingsLanguage;

        ChkStartWithWindows.Content = Strings.SettingsStartWithWindows;
        ChkClipboardWatcher.Content = Strings.SettingsClipboardWatcher;
        ChkNotifications.Content = Strings.SettingsNotifications;

        BtnSave.Content = Strings.SettingsSave;
        BtnCancel.Content = Strings.SettingsCancel;
    }

    private void LoadValues()
    {
        TxtDlFolder.Text = _settings.DownloadDirectory;
        TxtConnections.Text = _settings.ConnectionsPerDownload.ToString();
        TxtMaxConcurrent.Text = _settings.MaxConcurrentDownloads.ToString();
        TxtSpeedLimit.Text = _settings.SpeedLimitKbps.ToString();
        TxtMinSize.Text = (_settings.MinCaptureSizeBytes / (1024 * 1024)).ToString();
        TxtCaptureExts.Text = _settings.CapturedExtensions;

        // Video format
        for (int i = 0; i < CmbVideoFormat.Items.Count; i++)
        {
            if (CmbVideoFormat.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), _settings.PreferredVideoFormat, StringComparison.OrdinalIgnoreCase))
            {
                CmbVideoFormat.SelectedIndex = i;
                break;
            }
        }

        // Video quality
        for (int i = 0; i < CmbVideoQuality.Items.Count; i++)
        {
            if (CmbVideoQuality.Items[i] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int tagVal) && tagVal == _settings.MaxVideoResolution)
            {
                CmbVideoQuality.SelectedIndex = i;
                break;
            }
        }

        // Language
        for (int i = 0; i < CmbLanguage.Items.Count; i++)
        {
            if (CmbLanguage.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), _settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                CmbLanguage.SelectedIndex = i;
                break;
            }
        }

        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkClipboardWatcher.IsChecked = _settings.ClipboardWatcherEnabled;
        ChkNotifications.IsChecked = _settings.NotificationsEnabled;
        ChkAutoCategorize.IsChecked = _settings.AutoCategorizeSubfolders;

        ChkScheduler.IsChecked = _settings.SchedulerEnabled;
        TxtSchedStart.Text = _settings.SchedulerStartTime;
        TxtSchedStop.Text = _settings.SchedulerStopTime;
        ChkSchedAutoResume.IsChecked = _settings.SchedulerAutoResume;
        ChkSchedAutoPause.IsChecked = _settings.SchedulerAutoPause;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = Strings.SettingsDownloadFolder,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(TxtDlFolder.Text) ? TxtDlFolder.Text : ""
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            TxtDlFolder.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TxtDlFolder.Text))
            _settings.DownloadDirectory = TxtDlFolder.Text;

        if (int.TryParse(TxtConnections.Text, out int conn))
            _settings.ConnectionsPerDownload = Math.Clamp(conn, 1, 32);

        if (int.TryParse(TxtMaxConcurrent.Text, out int maxConc))
            _settings.MaxConcurrentDownloads = Math.Clamp(maxConc, 1, 10);

        if (int.TryParse(TxtSpeedLimit.Text, out int speedLimit))
            _settings.SpeedLimitKbps = Math.Max(0, speedLimit);

        if (int.TryParse(TxtMinSize.Text, out int minMb))
            _settings.MinCaptureSizeBytes = Math.Max(0, minMb) * 1024 * 1024;

        if (!string.IsNullOrWhiteSpace(TxtCaptureExts.Text))
            _settings.CapturedExtensions = TxtCaptureExts.Text.Trim();

        if (CmbVideoFormat.SelectedItem is ComboBoxItem fmtItem && fmtItem.Content != null)
            _settings.PreferredVideoFormat = fmtItem.Content.ToString()!;

        if (CmbVideoQuality.SelectedItem is ComboBoxItem qualItem &&
            int.TryParse(qualItem.Tag?.ToString(), out int res))
            _settings.MaxVideoResolution = res;

        if (CmbLanguage.SelectedItem is ComboBoxItem langItem && langItem.Tag != null)
            _settings.Language = langItem.Tag.ToString()!;

        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.ClipboardWatcherEnabled = ChkClipboardWatcher.IsChecked == true;
        _settings.NotificationsEnabled = ChkNotifications.IsChecked == true;
        _settings.AutoCategorizeSubfolders = ChkAutoCategorize.IsChecked == true;

        _settings.SchedulerEnabled = ChkScheduler.IsChecked == true;
        _settings.SchedulerStartTime = string.IsNullOrWhiteSpace(TxtSchedStart.Text) ? "02:00" : TxtSchedStart.Text.Trim();
        _settings.SchedulerStopTime = string.IsNullOrWhiteSpace(TxtSchedStop.Text) ? "07:00" : TxtSchedStop.Text.Trim();
        _settings.SchedulerAutoResume = ChkSchedAutoResume.IsChecked == true;
        _settings.SchedulerAutoPause = ChkSchedAutoPause.IsChecked == true;

        _settings.Save();
        _onSave(_settings);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
