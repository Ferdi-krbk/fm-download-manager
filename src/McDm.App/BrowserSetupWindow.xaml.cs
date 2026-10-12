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
    private readonly DispatcherTimer _pollTimer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _extensionDir;

    public BrowserSetupWindow(TokenStore tokenStore)
    {
        InitializeComponent();
        _tokenStore = tokenStore;

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

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _pollTimer.Tick += async (_, _) => await CheckConnectionAsync();
        _pollTimer.Start();

        Loaded += async (_, _) => await CheckConnectionAsync();
        Closed += (_, _) => _pollTimer.Stop();
    }

    private void SeedExtensionToken()
    {
        try
        {
            if (!Directory.Exists(_extensionDir)) Directory.CreateDirectory(_extensionDir);
            var tokenFile = Path.Combine(_extensionDir, "token.json");
            var json = JsonSerializer.Serialize(new { token = _tokenStore.Token });
            File.WriteAllText(tokenFile, json);
        }
        catch { }
    }

    private async Task CheckConnectionAsync()
    {
        try
        {
            var token = _tokenStore.Token;
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{BridgeServer.DefaultPort}/ping");
            req.Headers.TryAddWithoutValidation("Origin", "chrome-extension://setup-wizard");
            req.Headers.TryAddWithoutValidation("X-MCDM-Token", token);

            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("paired", out var paired) && paired.GetBoolean())
                {
                    TxtStatus.Text = "Eklenti başarıyla bağlandı ve aktif! (v3.0.0)";
                    TxtStatus.Foreground = Brushes.LightGreen;
                    DotStatus.Fill = Brushes.LimeGreen;
                    return;
                }
            }
        }
        catch { }

        TxtStatus.Text = "Tarayıcı bekleniyor (Tek tıkla kurulum butonunu kullanın)...";
        TxtStatus.Foreground = (Brush)FindResource("AccentBrush");
        DotStatus.Fill = (Brush)FindResource("DangerBrush");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_extensionDir)) Directory.CreateDirectory(_extensionDir);
        Process.Start("explorer.exe", $"\"{_extensionDir}\"");
    }

    private void OpenChrome_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("chrome", "chrome.exe", "chrome://extensions");

    private void OpenEdge_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("edge", "msedge.exe", "edge://extensions");

    private void OpenBrave_Click(object sender, RoutedEventArgs e) =>
        InstallAndLaunchBrowser("brave", "brave.exe", "brave://extensions");

    private void InstallAndLaunchBrowser(string browserKey, string exe, string extensionsUrl)
    {
        SeedExtensionToken();

        // 1. Register in Windows Registry
        RegisterExtensionInRegistry(browserKey, _extensionDir);

        // 2. Put token on clipboard as fallback
        try { ClipboardHelper.SetText(_tokenStore.Token); } catch { }

        TxtStatus.Text = "🚀 Tarayıcı başlatılıyor, eklenti otomatik yükleniyor...";
        TxtStatus.Foreground = Brushes.DeepSkyBlue;

        // 3. Launch browser with extension
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--load-extension=\"{_extensionDir}\" {extensionsUrl}") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(extensionsUrl) { UseShellExecute = true });
            }
            catch { }
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
                key.SetValue("version", "3.0.0");
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
            BtnCopyCode.Content = "✓ Kod Kopyalandı";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
