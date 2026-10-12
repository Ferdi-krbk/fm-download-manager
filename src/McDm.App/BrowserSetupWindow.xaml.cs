using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
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

        ApplyLocalization();
        TxtExtPath.Text = _extensionDir;
        TxtToken.Text = _tokenStore.Token;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) => await CheckConnectionAsync();
        _pollTimer.Start();

        Loaded += async (_, _) => await CheckConnectionAsync();
        Closed += (_, _) => _pollTimer.Stop();
    }

    private void ApplyLocalization()
    {
        Title = Strings.WizardTitle;
        TxtTitle.Text = Strings.WizardTitle;
        TxtSubtitle.Text = Strings.WizardSubtitle;
        LblStep1.Text = Strings.WizardStep1;
        BtnOpenFolder.Content = Strings.WizardOpenFolder;
        LblStep2.Text = Strings.WizardStep2;
        BtnChrome.Content = Strings.WizardOpenChrome;
        BtnEdge.Content = Strings.WizardOpenEdge;
        BtnBrave.Content = Strings.WizardOpenBrave;
        LblStep3.Text = Strings.WizardStep3;
        LblStep4.Text = Strings.WizardStep4;
        BtnCopyCode.Content = Strings.WizardCopyCode;
        LblConnectionStatus.Text = Strings.WizardConnectionStatus;
        TxtStatus.Text = Strings.WizardWaiting;
        BtnClose.Content = Strings.WizardClose;
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
                    TxtStatus.Text = "🟢 " + Strings.WizardConnected;
                    TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
                    return;
                }
            }
        }
        catch { }

        TxtStatus.Text = "⏳ " + Strings.WizardWaiting;
        TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_extensionDir)) Directory.CreateDirectory(_extensionDir);
        Process.Start("explorer.exe", $"\"{_extensionDir}\"");
    }

    private void OpenChrome_Click(object sender, RoutedEventArgs e) =>
        TryLaunchBrowser("chrome.exe", "chrome://extensions");

    private void OpenEdge_Click(object sender, RoutedEventArgs e) =>
        TryLaunchBrowser("msedge.exe", "edge://extensions");

    private void OpenBrave_Click(object sender, RoutedEventArgs e) =>
        TryLaunchBrowser("brave.exe", "brave://extensions");

    private static void TryLaunchBrowser(string exe, string url)
    {
        try { Process.Start(new ProcessStartInfo(exe, url) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtToken.Text);
        BtnCopyCode.Content = "✓ " + Strings.PairingCodeCopied;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
