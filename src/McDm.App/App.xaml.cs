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
    private DispatcherTimer? _schedulerTimer;
    private bool _schedulerWasInWindow;
    private TorrentDownloader? _torrentEngine;
    private string _lastSeenClipboard = "";
    private Action? _onBalloonClicked;

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("FM.DownloadManager.3.0");
        }
        catch { }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MC Download Manager");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] Domain: {args.ExceptionObject}\n");
            }
            catch { }
        };
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MC Download Manager");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] Dispatcher: {args.Exception}\n");
            }
            catch { }
        };

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

        try
        {
            _settings = AppSettings.Load();
            ThemeManager.ApplyThemeMode(_settings.AppTheme);
            ThemeManager.ApplyAccentColor(_settings.AccentColor);
            Strings.SetLanguage(_settings.Language);

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

        var data = AppPaths.DataDirectory;
        _tokenStore = new TokenStore(Path.Combine(data, "bridge.token"));

        var toolPaths = new ToolPaths();
        var videoEngine = new VideoDownloader(
            ytDlp: () => toolPaths.YtDlpPath is { } p ? new ToolLauncher(p) : null,
            ffmpeg: () => toolPaths.FfmpegPath,
            options: () => new VideoOptions
            {
                Container = _settings.PreferredVideoFormat,
                MaxHeight = _settings.MaxVideoResolution,
                SpeedLimitBytes = _manager != null ? _manager.SpeedLimiter.BytesPerSecond : ((long)_settings.SpeedLimitKbps * 1024L)
            });

        _torrentEngine = new TorrentDownloader(() => _manager != null ? _manager.SpeedLimiter.BytesPerSecond : ((long)_settings.SpeedLimitKbps * 1024L));
        _manager = new DownloadManager(data, downloadDirectory: _settings.DownloadDirectory,
                                       uiContext: new DispatcherSynchronizationContext(Dispatcher),
                                       videoEngine: videoEngine,
                                       torrentEngine: _torrentEngine)
        {
            DefaultConnections = _settings.ConnectionsPerDownload,
            MaxConcurrentDownloads = _settings.MaxConcurrentDownloads,
            AutoCategorizeSubfolders = _settings.AutoCategorizeSubfolders,
            VirusScanEnabled = _settings.VirusScanEnabled,
            CustomAntivirusPath = _settings.CustomAntivirusPath,
            CustomAntivirusArgs = _settings.CustomAntivirusArgs,
        };

        if (_settings.SpeedLimitKbps > 0)
        {
            _manager.SpeedLimiter.BytesPerSecond = (long)_settings.SpeedLimitKbps * 1024;
        }

        _manager.ItemCompleted += item =>
        {
            if (_settings?.NotificationsEnabled == true && _tray != null)
            {
                _onBalloonClicked = () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(item.SavePath) && File.Exists(item.SavePath))
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.SavePath}\"");
                        else if (!string.IsNullOrEmpty(item.SavePath) && Directory.Exists(item.SavePath))
                            System.Diagnostics.Process.Start("explorer.exe", $"\"{item.SavePath}\"");
                        else
                            Dispatcher.BeginInvoke(ShowWindow);
                    }
                    catch { }
                };
                _tray.ShowBalloonTip(3000, Strings.DownloadComplete, item.FileName, Forms.ToolTipIcon.Info);
            }
        };

        var hooks = new BridgeHooks
        {
            YtDlpAvailable = () => toolPaths.YtDlpAvailable,
            CaptureExtensions = () => (_settings?.CapturedExtensions ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MinCaptureSizeMB = () => (int)((_settings?.MinCaptureSizeBytes ?? 0) / (1024 * 1024)),
            AccentColor = () => ThemeManager.CurrentAccentHex ?? "#E50914",
            Language = () => Strings.IsTurkish ? "tr" : "en",
            ThemeMode = () => ThemeManager.CurrentThemeMode,
            UpdateConfig = (accent, lang, theme) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    bool changed = false;
                    if (!string.IsNullOrWhiteSpace(theme))
                    {
                        ThemeManager.ApplyThemeMode(theme);
                        if (_settings != null) { _settings.AppTheme = ThemeManager.CurrentThemeMode; changed = true; }
                    }
                    if (!string.IsNullOrWhiteSpace(accent))
                    {
                        ThemeManager.ApplyAccentColor(accent);
                        if (_settings != null) { _settings.AccentColor = ThemeManager.CurrentAccentHex; changed = true; }
                    }
                    if (!string.IsNullOrWhiteSpace(lang))
                    {
                        Strings.SetLanguage(lang);
                        if (_settings != null) { _settings.Language = lang; changed = true; }
                        _window?.ApplyLocalization();
                    }
                    if (changed) _settings?.Save();
                });
            },
            ConfirmDownloadAsync = async (req) =>
            {
                var tcs = new TaskCompletionSource<bool>();
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        bool isVideo = req.Kind == DownloadKind.Video || VideoDownloader.IsVideoUrl(req.Url);
                        if (isVideo)
                        {
                            if (req.SubtitlesOnly)
                            {
                                req.Kind = DownloadKind.Video;
                                tcs.SetResult(true);
                                return;
                            }

                            var launcher = toolPaths.YtDlpPath is { Length: > 0 } yp ? new ToolLauncher(yp) : null;
                            var qDlg = new VideoQualityDialog(
                                req.Url,
                                req.FileName,
                                req.WriteSubtitles || true,
                                req.SubtitleLangs ?? "tr,en",
                                req.Subtitles,
                                req.VideoQualities,
                                req.AudioTracks,
                                req,
                                launcher)
                            {
                                Topmost = true,
                                WindowStartupLocation = WindowStartupLocation.CenterScreen
                            };
                            qDlg.Activate();
                            qDlg.Focus();
                            bool? res = qDlg.ShowDialog();
                            if (res == true)
                            {
                                req.Kind = DownloadKind.Video;
                                req.CustomFormat = qDlg.SelectedFormat;
                                if (!string.IsNullOrWhiteSpace(qDlg.ResolvedUrl))
                                {
                                    if (string.IsNullOrWhiteSpace(req.Referer) && qDlg.ResolvedUrl != req.Url) req.Referer = req.Url;
                                    req.Url = qDlg.ResolvedUrl;
                                }
                                if (string.IsNullOrWhiteSpace(req.FileName) && !string.IsNullOrWhiteSpace(qDlg.DetectedTitle))
                                    req.FileName = qDlg.DetectedTitle;
                                req.WriteSubtitles = qDlg.DownloadSubtitles;
                                req.SubtitleLangs = qDlg.SubtitleLangs;
                                if (!string.IsNullOrEmpty(qDlg.SelectedSubtitleUrl))
                                {
                                    req.SubtitleUrl = qDlg.SelectedSubtitleUrl;
                                }
                                if (qDlg.AudioOnly)
                                {
                                    req.Container = "mp3";
                                    req.AudioOnly = true;
                                }
                                tcs.SetResult(true);
                            }
                            else
                            {
                                tcs.SetResult(false);
                            }
                        }
                        else
                        {
                            var pDlg = new DownloadPromptDialog(req, _settings?.DownloadDirectory ?? "")
                            {
                                Topmost = true,
                                WindowStartupLocation = WindowStartupLocation.CenterScreen
                            };
                            pDlg.Activate();
                            pDlg.Focus();
                            bool? res = pDlg.ShowDialog();
                            tcs.SetResult(res == true);
                        }
                    }
                    catch
                    {
                        tcs.SetResult(true);
                    }
                });
                return await tcs.Task;
            }
        };

        _bridge = new BridgeServer(_manager, _tokenStore, hooks);
        try 
        { 
            await _bridge.StartAsync(); 
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(null, $"Browser bridge could not start on port {BridgeServer.DefaultPort}:\n{ex.Message}\n\n" +
                            "Browser integration will not work until the port is free.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        BuildTray();
        StartClipboardWatcher();
        StartTrayTooltipUpdater();
        StartScheduler();

        _window = new MainWindow(_manager, _tokenStore, _settings, _bridge);
        if (!e.Args.Contains("--background")) _window.Show();

        // Non-blocking auto update check on startup
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000);
                var info = await UpdateChecker.CheckAsync();
                if (info != null && info.HasUpdate)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_settings?.NotificationsEnabled != false && _tray != null)
                        {
                            _onBalloonClicked = () =>
                            {
                                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true }); } catch { }
                            };
                            _tray.ShowBalloonTip(7000, 
                                Strings.IsTurkish ? "Yeni Güncelleme Mevcut!" : "New Update Available!",
                                Strings.IsTurkish 
                                    ? $"FM Download Manager v{info.LatestVersion} yayınlandı. Tıklayarak indirebilirsiniz." 
                                    : $"FM Download Manager v{info.LatestVersion} is available. Click to download.", 
                                Forms.ToolTipIcon.Info);
                        }

                        _window?.NotifyUpdateAvailable(info);
                    });
                }
            }
            catch { }
        });
        }
        catch (Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MC Download Manager");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] Startup fatal: {ex}\n");
            }
            catch { }
            DarkMessageBox.Show(null, Strings.ErrorOccurred + ex.Message, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        menu.Items.Add(Strings.TrayCheckUpdates, null, async (_, _) =>
        {
            var info = await UpdateChecker.CheckAsync();
            if (info != null && info.HasUpdate)
            {
                var res = DarkMessageBox.Show(_window,
                    Strings.TrayNewVersionPrompt(info.LatestVersion),
                    Strings.AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                {
                    if (!string.IsNullOrEmpty(info.DownloadUrl))
                    {
                        try
                        {
                            var tempFile = Path.Combine(Path.GetTempPath(), $"FMDownloadManager-Setup-{info.LatestVersion}.exe");
                            using (var http = new HttpClient())
                            using (var stream = await http.GetStreamAsync(info.DownloadUrl))
                            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await stream.CopyToAsync(fs);
                            }

                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempFile) { UseShellExecute = true });
                            Application.Current.Shutdown();
                            return;
                        }
                        catch { }
                    }
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true }); } catch { }
                }
            }
            else if (info != null)
            {
                DarkMessageBox.Show(_window, Strings.TrayUpToDate(UpdateChecker.CurrentVersion, info.LatestVersion),
                    Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DarkMessageBox.Show(_window, Strings.TrayCheckFailed,
                    Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.TrayQuit, null, (_, _) => Quit());

        Icon icon = SystemIcons.Application;
        try
        {
            var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
            if (sri?.Stream != null)
            {
                icon = new Icon(sri.Stream, 32, 32);
            }
        }
        catch { }

        if (icon == SystemIcons.Application)
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    using var assoc = Icon.ExtractAssociatedIcon(exe);
                    if (assoc != null) icon = (Icon)assoc.Clone();
                }
            }
            catch { }
        }

        _tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = Strings.AppTitle,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => _onBalloonClicked?.Invoke();
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

                    bool isMagnet = text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
                    bool isTorrentExt = text.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

                    if (isMagnet || isTorrentExt)
                    {
                        var msg = isMagnet ? Strings.ClipboardMagnetPrompt : Strings.ClipboardTorrentPrompt;
                        var res = DarkMessageBox.Show(MainWindow, msg, Strings.DownloadTorrentTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (res == MessageBoxResult.Yes)
                        {
                            _manager?.Add(new DownloadRequest { Url = text, Kind = DownloadKind.Torrent });
                            ShowWindow();
                        }
                        return;
                    }

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
                                if (isVideo)
                                {
                                    var qDlg = new VideoQualityDialog(text) { Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                                    if (qDlg.ShowDialog() == true)
                                    {
                                        _manager?.Add(new DownloadRequest
                                        {
                                            Url = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) ? qDlg.ResolvedUrl : text,
                                            FileName = !string.IsNullOrWhiteSpace(qDlg.DetectedTitle) ? qDlg.DetectedTitle : null,
                                            Referer = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) && qDlg.ResolvedUrl != text ? text : null,
                                            Kind = DownloadKind.Video,
                                            CustomFormat = qDlg.SelectedFormat,
                                            Container = qDlg.AudioOnly ? "mp3" : "mp4",
                                            AudioOnly = qDlg.AudioOnly,
                                            WriteSubtitles = qDlg.DownloadSubtitles,
                                            SubtitleLangs = qDlg.SubtitleLangs,
                                            SubtitleUrl = qDlg.SelectedSubtitleUrl
                                        });
                                        ShowWindow();
                                    }
                                }
                                else
                                {
                                    _manager?.Add(new DownloadRequest { Url = text, Kind = DownloadKind.File });
                                    ShowWindow();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        };
        _clipboardTimer.Start();
    }

    private void StartScheduler()
    {
        _schedulerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _schedulerTimer.Tick += (_, _) => CheckScheduler();
        _schedulerTimer.Start();
        CheckScheduler();
    }

    private void CheckScheduler()
    {
        if (_settings == null || !_settings.SchedulerEnabled || _manager == null)
        {
            _schedulerWasInWindow = false;
            return;
        }

        if (!TimeOnly.TryParse(_settings.SchedulerStartTime, out var start) ||
            !TimeOnly.TryParse(_settings.SchedulerStopTime, out var stop))
        {
            return;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);
        bool inWindow = start <= stop
            ? (now >= start && now < stop)
            : (now >= start || now < stop); // overnight span e.g. 23:00 to 06:00

        if (inWindow && !_schedulerWasInWindow)
        {
            _schedulerWasInWindow = true;
            if (_settings.SchedulerAutoResume)
            {
                _manager.ResumeAll();
                if (_settings.NotificationsEnabled)
                    _tray?.ShowBalloonTip(3000, Strings.SchedulerTitle, Strings.SchedulerStarted, Forms.ToolTipIcon.Info);
            }
        }
        else if (!inWindow && _schedulerWasInWindow)
        {
            _schedulerWasInWindow = false;
            if (_settings.SchedulerAutoPause)
            {
                _manager.PauseAll();
                if (_settings.NotificationsEnabled)
                    _tray?.ShowBalloonTip(3000, Strings.SchedulerTitle, Strings.SchedulerStopped, Forms.ToolTipIcon.Info);
            }
        }
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
        _schedulerTimer?.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        if (_manager != null) await _manager.DisposeAsync();
        _torrentEngine?.Dispose();
        if (_bridge != null) await _bridge.DisposeAsync();
        _window?.AllowClose();
        Shutdown();
    }

    public static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) != null;
    }

    public static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
        else k.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
