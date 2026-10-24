using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using McDm.App.Resources;
using McDm.Core;

namespace McDm.App;

public partial class BrowserSetupWindow : Window
{
    private readonly TokenStore _tokenStore;
    private readonly BridgeServer? _bridge;
    private readonly DispatcherTimer _pollTimer;
    private readonly string _extensionDir;

    public BrowserSetupWindow(TokenStore tokenStore, BridgeServer? bridge = null)
    {
        InitializeComponent();
        ApplyLocalization();

        _tokenStore = tokenStore;
        _bridge = bridge;

        // Extension directory: look in current app directory or project root
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate1 = Path.Combine(baseDir, "extension");
        var candidate2 = Path.Combine(baseDir, "..", "..", "..", "..", "extension");
        var candidate3 = Path.Combine(baseDir, "ChromeExtension");

        if (Directory.Exists(candidate1)) _extensionDir = Path.GetFullPath(candidate1);
        else if (Directory.Exists(candidate2)) _extensionDir = Path.GetFullPath(candidate2);
        else if (Directory.Exists(candidate3)) _extensionDir = Path.GetFullPath(candidate3);
        else _extensionDir = Path.Combine(baseDir, "extension");

        // Seed token.json into extension directory so it automatically pairs with zero user clicks
        SeedExtensionToken();

        TxtExtPath.Text = _extensionDir;
        TxtToken.Text = _tokenStore.Token;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pollTimer.Tick += (_, _) => CheckConnection();
        _pollTimer.Start();

        Loaded += (_, _) => CheckConnection();
        Closed += (_, _) => _pollTimer.Stop();
    }

    private void ApplyLocalization()
    {
        Title = Strings.BrowserSetup;
        TxtWinHeader.Text = Strings.BrowserSetup;
        TxtTitle.Text = Strings.BrowserSetupHeader;
        TxtSubtitle.Text = Strings.BrowserSetupSub;
        LblAutoSection.Text = Strings.BrowserSetupAutoSection;
        TxtChromeTitle.Text = Strings.BrowserSetupChromeTitle;
        TxtChromeSub.Text = Strings.BrowserSetupChromeSub;
        TxtEdgeTitle.Text = Strings.BrowserSetupEdgeTitle;
        TxtEdgeSub.Text = Strings.BrowserSetupEdgeSub;
        TxtBraveTitle.Text = Strings.BrowserSetupBraveTitle;
        TxtBraveSub.Text = Strings.BrowserSetupBraveSub;
        LblQuickTipTitle.Text = Strings.BrowserSetupQuickTipTitle;
        TxtQuickTipSteps.Text = Strings.BrowserSetupQuickTipSteps;
        TxtQuickTipAuto.Text = Strings.BrowserSetupQuickTipAuto;
        LblConnectionStatus.Text = Strings.BrowserSetupStatusTitle;
        ExpManual.Header = Strings.BrowserSetupAdvanced;
        LblExtPath.Text = Strings.BrowserSetupFolderPath;
        BtnOpenFolder.Content = Strings.BrowserSetupFolder;
        LblToken.Text = Strings.BrowserSetupSecurityToken;
        BtnCopyCode.Content = Strings.BrowserSetupCopy;
        BtnClose.Content = Strings.BrowserSetupComplete;
    }

    private void SeedExtensionToken()
    {
        var locations = new[]
        {
            _extensionDir,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extension"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "extension"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FM Download Manager", "extension")
        };

        foreach (var loc in locations)
        {
            try
            {
                if (Directory.Exists(loc))
                {
                    var tokenFile = Path.Combine(loc, "token.json");
                    var json = JsonSerializer.Serialize(new { token = _tokenStore.Token });
                    File.WriteAllText(tokenFile, json);
                }
            }
            catch { }
        }
    }

    private void CheckConnection()
    {
        bool connected = _bridge != null 
            && _bridge.LastExtensionSeenUtc.HasValue 
            && (DateTime.UtcNow - _bridge.LastExtensionSeenUtc.Value).TotalSeconds <= 12 
            && _bridge.LastExtensionPaired;

        if (connected)
        {
            string bName = (_bridge?.LastExtensionBrowser ?? (Strings.IsTurkish ? "Tarayıcı" : "Browser")).ToUpperInvariant();
            TxtStatus.Text = Strings.IsTurkish
                ? $"✓ {bName} eklentisi başarıyla bağlandı ve aktif!"
                : $"✓ {bName} extension successfully connected and active!";
            TxtStatus.Foreground = Brushes.LightGreen;
            DotStatus.Fill = Brushes.LimeGreen;
        }
        else
        {
            TxtStatus.Text = Strings.BrowserSetupWaiting;
            TxtStatus.Foreground = (Brush)FindResource("TextMuteBrush");
            DotStatus.Fill = (Brush)FindResource("DangerBrush");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_extensionDir)) Directory.CreateDirectory(_extensionDir);
        Process.Start("explorer.exe", $"\"{_extensionDir}\"");
    }

    private void OpenChrome_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("chrome", "chrome.exe", "https://chrome.google.com");

    private void OpenEdge_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("edge", "msedge.exe", "edge://extensions");

    private void OpenBrave_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("brave", "brave.exe", "brave://extensions");

    private void InstallAndLaunchBrowser(string browserKey, string exe, string extensionsUrl)
    {
        SeedExtensionToken();

        // 1. Copy extension path to clipboard for 1-click paste
        try { ClipboardHelper.SetText(_extensionDir); } catch { }

        // 2. Open extension folder in Explorer so the user can easily see/drag it
        try
        {
            if (!Directory.Exists(_extensionDir)) Directory.CreateDirectory(_extensionDir);
            Process.Start("explorer.exe", $"\"{_extensionDir}\"");
        }
        catch { }

        // 3. Register in Windows Registry
        RegisterExtensionInRegistry(browserKey, _extensionDir);

        TxtStatus.Text = Strings.IsTurkish
            ? "Klasör ve tarayıcı açıldı. 'Paketlenmemiş öge yükle' diyerek panodaki klasörü seçin..."
            : "Folder and browser opened. Click 'Load unpacked' and select the folder from your clipboard...";
        TxtStatus.Foreground = (Brush)FindResource("AccentHoverBrush");

        // 4. Launch browser directly to extensions management page
        try
        {
            if (browserKey == "chrome")
            {
                // Chrome blocks chrome://extensions from cmdline in some versions; open with new window
                try { Process.Start(new ProcessStartInfo(exe, "chrome://extensions") { UseShellExecute = true }); }
                catch { Process.Start(new ProcessStartInfo("chrome://extensions") { UseShellExecute = true }); }
            }
            else
            {
                Process.Start(new ProcessStartInfo(exe, extensionsUrl) { UseShellExecute = true });
            }
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(extensionsUrl) { UseShellExecute = true }); } catch { }
        }
    }

    private static void RegisterExtensionInRegistry(string browserKey, string extensionDir)
    {
        try
        {
            string keyPath = browserKey switch
            {
                "chrome" => @"Software\Google\Chrome\Extensions\fmdmdownloadmanager",
                "edge" => @"Software\Microsoft\Edge\Extensions\fmdmdownloadmanager",
                "brave" => @"Software\BraveSoftware\Brave-Browser\Extensions\fmdmdownloadmanager",
                _ => @"Software\Google\Chrome\Extensions\fmdmdownloadmanager"
            };

            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key != null)
            {
                key.SetValue("path", extensionDir);
                key.SetValue("version", BridgeServer.Version);
            }
        }
        catch { }
    }

    private async void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtToken.Text?.Trim() ?? "";
        TxtToken.Focus();
        TxtToken.SelectAll();
        BtnCopyCode.IsEnabled = false;

        bool ok = await Task.Run(() => ClipboardHelper.SetText(text));
        BtnCopyCode.IsEnabled = true;

        if (ok)
        {
            BtnCopyCode.Content = Strings.IsTurkish ? "✓ Kod Kopyalandı" : "✓ Code Copied";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
