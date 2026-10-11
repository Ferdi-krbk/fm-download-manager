using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using McDm.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace McDm.App;

public partial class App : System.Windows.Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "MC Download Manager";
    private const string ShowEventName = @"Local\MCDownloadManager.Show";
    private const string QuitEventName = @"Local\MCDownloadManager.Quit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _quitEvent;
    private volatile bool _quitting;
    private DownloadManager? _manager;
    private BridgeServer? _bridge;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\MCDownloadManager.SingleInstance", out bool first);
        bool quitRequested = e.Args.Contains("--quit");
        if (!first)
        {
            // tell the running instance to bring its window to the front (or to quit), then leave
            try
            {
                using var ev = EventWaitHandle.OpenExisting(quitRequested ? QuitEventName : ShowEventName);
                ev.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        if (quitRequested) { Shutdown(); return; }   // nothing running, nothing to quit

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
        _manager = new DownloadManager(data, uiContext: new DispatcherSynchronizationContext(Dispatcher));
        _manager.ItemCompleted += item =>
            _tray?.ShowBalloonTip(3000, "Download complete", item.FileName, Forms.ToolTipIcon.Info);

        _bridge = new BridgeServer(_manager, new TokenStore());
        try { await _bridge.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Browser bridge could not start on port {BridgeServer.DefaultPort}:\n{ex.Message}\n\n" +
                            "Browser integration will not work until the port is free.", "MC Download Manager");
        }

        BuildTray();
        _window = new MainWindow(_manager);
        if (!e.Args.Contains("--background")) _window.Show();
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application, // replaced by the real icon in step 4
            Text = "MC Download Manager",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        if (_quitting) return;
        _window ??= new MainWindow(_manager!);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Activate() alone is refused by Windows' foreground lock; a short Topmost toggle reliably raises the window.
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _showEvent?.Set();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _manager?.Dispose();                 // pauses everything and persists resume state
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
