using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using McDm.App.Resources;
using McDm.Core;

namespace McDm.App;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand PasteCommand = new();
    private readonly DownloadManager _manager;
    private readonly TokenStore _tokenStore;
    private readonly AppSettings _settings;
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _speedTimer;
    private bool _allowClose;

    private static readonly HashSet<string> ImgExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico" };
    private static readonly HashSet<string> MusicExts = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma" };
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".m4v" };
    private static readonly HashSet<string> AppExts = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".apk", ".appx", ".iso" };
    private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".epub", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" };
    private static readonly HashSet<string> ArcExts = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" };

    public MainWindow(DownloadManager manager, TokenStore tokenStore, AppSettings settings)
    {
        InitializeComponent();
        _manager = manager;
        _tokenStore = tokenStore;
        _settings = settings;

        _view = CollectionViewSource.GetDefaultView(_manager.Items);
        _view.Filter = FilterItem;
        Grid.ItemsSource = _view;

        ApplyLocalization();

        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _speedTimer.Tick += (_, _) => UpdateTotalSpeed();
        _speedTimer.Start();
    }

    public void ApplyLocalization()
    {
        Title = Strings.AppTitle;

        TxtSearchWatermark.Text = "🔍 " + Strings.SearchPlaceholder;

        CtxOpen.Header = Strings.Open;
        CtxOpenFolder.Header = Strings.OpenFolder;
        CtxPause.Header = Strings.Pause;
        CtxResume.Header = Strings.Resume;
        CtxCancel.Header = Strings.Cancel;
        CtxRemove.Header = Strings.Remove;
        CtxRemoveAndDelete.Header = Strings.RemoveAndDelete;
        CtxCopyUrl.Header = Strings.CopyUrl;
        CtxDetails.Header = Strings.Details;

        ColHeaderName.Header = Strings.ColName;
        ColHeaderSize.Header = Strings.ColSize;
        ColHeaderProgress.Header = Strings.ColProgress;
        ColHeaderSpeed.Header = Strings.ColSpeed;
        ColHeaderConn.Header = Strings.ColConnections;
        ColHeaderStatus.Header = Strings.ColStatus;
        ColHeaderError.Header = Strings.ColError;

        StatusText.Text = Strings.StatusReady;
    }

    private void UpdateTotalSpeed()
    {
        double totalSpeed = _manager.Items.Where(i => i.Status == DownloadStatus.Downloading).Sum(i => i.Speed);
        int active = _manager.Items.Count(i => i.Status == DownloadStatus.Downloading);
        TotalSpeedText.Text = active > 0
            ? $"{active} downloading - {BytesConverter.Format(totalSpeed)}/s"
            : "";
    }

    private bool FilterItem(object obj)
    {
        if (obj is not DownloadItem item) return false;

        // Search text
        string q = TxtSearch?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(q))
        {
            bool nameMatch = item.FileName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;
            bool urlMatch = item.Request?.Url?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;
            if (!nameMatch && !urlMatch) return false;
        }

        // Category
        string cat = (LstCategories?.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "all";
        if (cat == "all") return true;

        string ext = Path.GetExtension(item.FileName ?? "");
        return cat switch
        {
            "images" => ImgExts.Contains(ext),
            "music" => MusicExts.Contains(ext),
            "video" => VideoExts.Contains(ext) || item.Request?.Kind == DownloadKind.Video,
            "apps" => AppExts.Contains(ext),
            "documents" => DocExts.Contains(ext),
            "archives" => ArcExts.Contains(ext),
            "other" => !ImgExts.Contains(ext) && !MusicExts.Contains(ext) && !VideoExts.Contains(ext) &&
                       !AppExts.Contains(ext) && !DocExts.Contains(ext) && !ArcExts.Contains(ext) &&
                       item.Request?.Kind != DownloadKind.Video,
            _ => true
        };
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
    }

    private void Categories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _view?.Refresh();
    }

    public void AllowClose() { _allowClose = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(Hide));
        }
        base.OnClosing(e);
    }

    private IEnumerable<DownloadItem> Selected => Grid.SelectedItems.Cast<DownloadItem>().ToList();

    private void PasteUrl_Click(object sender, RoutedEventArgs e)
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            StatusText.Text = "Clipboard does not contain an http(s) link.";
            return;
        }
        var kind = VideoDownloader.IsVideoUrl(text) ? DownloadKind.Video : DownloadKind.File;
        _manager.Add(new DownloadRequest { Url = text, Kind = kind });
        StatusText.Text = "Added " + uri.Host;
    }

    private void AddUrls_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddUrlsDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultUrls.Count > 0)
        {
            foreach (var u in dlg.ResultUrls)
            {
                var kind = VideoDownloader.IsVideoUrl(u) ? DownloadKind.Video : DownloadKind.File;
                _manager.Add(new DownloadRequest { Url = u, Kind = kind });
            }
            StatusText.Text = $"Added {dlg.ResultUrls.Count} URL(s)";
        }
    }

    private void Pause_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Pause(i.Id); }
    private void Resume_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Resume(i.Id); }
    private void Cancel_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Cancel(i.Id); }
    private void Remove_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Remove(i.Id); }

    private void RemoveAndDelete_Click(object s, RoutedEventArgs e)
    {
        foreach (var i in Selected)
        {
            _manager.Remove(i.Id);
            try { if (File.Exists(i.SavePath)) File.Delete(i.SavePath); } catch { }
            try { if (File.Exists(i.SavePath + ".mcdm-part")) File.Delete(i.SavePath + ".mcdm-part"); } catch { }
            try { if (File.Exists(i.SavePath + ".mcdm")) File.Delete(i.SavePath + ".mcdm"); } catch { }
        }
    }

    private void Open_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && File.Exists(i.SavePath))
            Process.Start(new ProcessStartInfo(i.SavePath) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && File.Exists(i.SavePath))
            Process.Start("explorer.exe", $"/select,\"{i.SavePath}\"");
        else
            Process.Start("explorer.exe", $"\"{_manager.DefaultDownloadDirectory}\"");
    }

    private void CopyUrl_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && !string.IsNullOrEmpty(i.Request.Url))
            Clipboard.SetText(i.Request.Url);
    }

    private void Details_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i)
        {
            var win = new DownloadDetailWindow(i) { Owner = this };
            win.ShowDialog();
        }
    }

    private void BrowserSetup_Click(object sender, RoutedEventArgs e)
    {
        var win = new BrowserSetupWindow(_tokenStore) { Owner = this };
        win.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, updated =>
        {
            _manager.DefaultDownloadDirectory = updated.DownloadDirectory;
            _manager.DefaultConnections = updated.ConnectionsPerDownload;
            _manager.MaxConcurrentDownloads = updated.MaxConcurrentDownloads;

            // Apply culture change if user switched language
            if (updated.Language == "tr") Strings.Culture = new System.Globalization.CultureInfo("tr");
            else if (updated.Language == "en") Strings.Culture = new System.Globalization.CultureInfo("en");
            else Strings.Culture = null;

            ApplyLocalization();
        })
        { Owner = this };
        win.ShowDialog();
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        var tools = new ToolPaths();
        var ytDlpPath = tools.YtDlpPath;
        if (string.IsNullOrEmpty(ytDlpPath))
        {
            MessageBox.Show("yt-dlp is not installed. Run scripts/fetch-tools.ps1 first.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText.Text = Strings.UpdatingYtDlp;
        BtnUpdateYtDlp.IsEnabled = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "-U",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                StatusText.Text = Strings.YtDlpUpdated;
                MessageBox.Show(Strings.YtDlpUpdated, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Strings.YtDlpUpdateFailed, ex.Message);
            MessageBox.Show(string.Format(Strings.YtDlpUpdateFailed, ex.Message), Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnUpdateYtDlp.IsEnabled = true;
        }
    }

    private void Grid_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is DownloadItem { Status: DownloadStatus.Completed } i && File.Exists(i.SavePath))
            Process.Start(new ProcessStartInfo(i.SavePath) { UseShellExecute = true });
        else if (Grid.SelectedItem is DownloadItem itm)
        {
            var win = new DownloadDetailWindow(itm) { Owner = this };
            win.ShowDialog();
        }
    }
}
