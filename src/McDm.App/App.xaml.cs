using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using McDm.App.Resources;
using McDm.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace McDm.App;

public partial class App : System.Windows.Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FM Download Manager";
    private const string ShowEventName = @"Local\MCDownloadManager.Show";
    private const string QuitEventName = @"Local\MCDownloadManager.Quit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _quitEvent;
    private volatile bool _quitting;
    private DownloadManager? _manager;
    private BridgeServer? _bridge;
    private TokenStore? _tokenStore;
    private AppSettings? _settings;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _clipboardTimer;
    private DispatcherTimer? _trayTooltipTimer;
    private string _lastSeenClipboard = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--gallery"))
        {
            new GalleryWindow().Show();
            return;
        }

        _singleInstance = new Mutex(true, @"Local\MCDownloadManager.SingleInstance", out bool first);
        bool quitRequested = e.Args.Contains("--quit");
        if (!first)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(quitRequested ? QuitEventName : ShowEventName);
                ev.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        if (quitRequested) { Shutdown(); return; }

        _settings = AppSettings.Load();
        if (_settings.Language == "tr") Strings.Culture = new System.Globalization.CultureInfo("tr");
        else if (_settings.Language == "en") Strings.Culture = new System.Globalization.CultureInfo("en");

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _quitEvent };
            while (true)
            {
                int which = WaitHandle.WaitAny(handles);
                if (_quitting) return;
                if (which == 0) Dispatcher.BeginInvoke(ShowWindow);
                else Dispatcher.BeginInvoke(Quit);
            }
        }) { IsBackground = true, Name = "InstanceSignalListener" }.Start();

        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MC Download Manager");
        _tokenStore = new TokenStore(Path.Combine(data, "bridge.token"));

        var toolPaths = new ToolPaths();
        var videoEngine = new VideoDownloader(
            ytDlp: () => toolPaths.YtDlpPath is { } p ? new ToolLauncher(p) : null,
            ffmpeg: () => toolPaths.FfmpegPath,
            options: () => new VideoOptions
            {
                Container = _settings.PreferredVideoFormat,
                MaxHeight = _settings.MaxVideoResolution,
            });

        _manager = new DownloadManager(data, downloadDirectory: _settings.DownloadDirectory,
                                       uiContext: new DispatcherSynchronizationContext(Dispatcher),
                                       videoEngine: videoEngine)
        {
            DefaultConnections = _settings.ConnectionsPerDownload,
            MaxConcurrentDownloads = _settings.MaxConcurrentDownloads,
        };

        _manager.ItemCompleted += item =>
        {
            if (_settings?.NotificationsEnabled == true)
                _tray?.ShowBalloonTip(3000, "DOWNLOAD COMPLETE", item.FileName, Forms.ToolTipIcon.Info);
        };

        var hooks = new BridgeHooks
        {
            YtDlpAvailable = () => toolPaths.YtDlpAvailable,
            CaptureExtensions = () => (_settings?.CapturedExtensions ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MinCaptureSizeMB = () => (int)((_settings?.MinCaptureSizeBytes ?? 0) / (1024 * 1024)),
        };

        _bridge = new BridgeServer(_manager, _tokenStore, hooks);
        try { await _bridge.StartAsync(); }
        catch (Exception ex)
        {
            DarkMessageBox.Show(null, $"Browser bridge could not start on port {BridgeServer.DefaultPort}:\n{ex.Message}\n\n" +
                            "Browser integration will not work until the port is free.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        BuildTray();
        StartClipboardWatcher();
        StartTrayTooltipUpdater();

        _window = new MainWindow(_manager, _tokenStore, _settings);
        if (!e.Args.Contains("--background")) _window.Show();
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
        };
        menu.Items.Add(Strings.TrayOpen, null, (_, _) => ShowWindow());
        var startup = new Forms.ToolStripMenuItem(Strings.TrayStartWithWindows) { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.TrayQuit, null, (_, _) => Quit());

        Icon icon = SystemIcons.Application;
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
            using var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null) icon = new Icon(iconStream);
        }
        catch { }

        _tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = Strings.AppTitle,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void StartTrayTooltipUpdater()
    {
        _trayTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _trayTooltipTimer.Tick += (_, _) =>
        {
            if (_tray == null || _manager == null) return;
            int active = _manager.Items.Count(i => i.Status == DownloadStatus.Downloading);
            double totalSpeed = _manager.Items.Where(i => i.Status == DownloadStatus.Downloading).Sum(i => i.Speed);

            string text = active > 0
                ? $"{Strings.AppTitle}\n{active} active - {BytesConverter.Format(totalSpeed)}/s"
                : Strings.AppTitle;

            if (text.Length > 63) text = text[..63]; // NotifyIcon.Text max length is 63 chars on older Win32
            _tray.Text = text;
        };
        _trayTooltipTimer.Start();
    }

    private void StartClipboardWatcher()
    {
        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _clipboardTimer.Tick += (_, _) =>
        {
            if (_settings?.ClipboardWatcherEnabled != true) return;
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText().Trim();
                    if (text == _lastSeenClipboard) return;
                    _lastSeenClipboard = text;

                    if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https"))
                    {
                        var isVideo = VideoDownloader.IsVideoUrl(text);
                        var ext = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
                        var capturedList = (_settings.CapturedExtensions ?? "")
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        bool isCapturedFile = !string.IsNullOrEmpty(ext) && capturedList.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));

                        if (isVideo || isCapturedFile)
                        {
                            // Show confirmation prompt
                            var msg = string.Format(Strings.DownloadPromptMessage, text);
                            var res = DarkMessageBox.Show(MainWindow, msg, Strings.DownloadPromptTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (res == MessageBoxResult.Yes)
                            {
                                var kind = isVideo ? DownloadKind.Video : DownloadKind.File;
                                _manager?.Add(new DownloadRequest { Url = text, Kind = kind });
                                ShowWindow();
                            }
                        }
                    }
                }
            }
            catch { }
        };
        _clipboardTimer.Start();
    }

    private void ShowWindow()
    {
        if (_quitting) return;
        _window ??= new MainWindow(_manager!, _tokenStore!, _settings!);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _showEvent?.Set();
        _clipboardTimer?.Stop();
        _trayTooltipTimer?.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _manager?.Dispose();
        if (_bridge != null) await _bridge.DisposeAsync();
        _window?.AllowClose();
        Shutdown();
    }

    private static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) != null;
    }

    private static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
        else k.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
