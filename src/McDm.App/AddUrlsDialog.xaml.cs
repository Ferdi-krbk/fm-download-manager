using System.IO;
using System.Windows;
using McDm.App.Resources;
using Forms = System.Windows.Forms;

namespace McDm.App;

public partial class AddUrlsDialog : Window
{
    public List<string> ResultUrls { get; } = new();
    public string? SelectedDirectory { get; private set; }

    public AddUrlsDialog(string? defaultDir = null)
    {
        InitializeComponent();
        Title = Strings.AddUrlsTitle;
        TxtWinHeader.Text = Strings.AddUrlsTitleBar;
        PromptText.Text = Strings.AddUrlsPromptText;
        LblFolder.Text = Strings.SaveFolder;
        BtnBrowse.Content = Strings.Browse;
        OkButton.Content = Strings.AddUrlsButton;
        CancelButton.Content = Strings.SettingsCancel;

        string initialDir = !string.IsNullOrWhiteSpace(defaultDir) && Directory.Exists(defaultDir)
            ? defaultDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        TxtFolder.Text = initialDir;

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                UrlsBox.Text = text;
                UrlsBox.SelectAll();
            }
        }
        UpdateValidCount();
        UrlsBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(TxtFolder.Text) ? TxtFolder.Text : "",
            Description = Strings.SelectFolder
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            TxtFolder.Text = dialog.SelectedPath;
        }
    }

    private void UrlsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateValidCount();
    }

    private static bool IsValidLink(string l)
    {
        if (string.IsNullOrWhiteSpace(l)) return false;
        if (l.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return true;
        if (l.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(l, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
    }

    private void UpdateValidCount()
    {
        if (UrlsBox == null || ValidLinksCountText == null) return;
        var lines = UrlsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int valid = lines.Count(IsValidLink);
        ValidLinksCountText.Text = Strings.ValidLinksCount(valid);
        if (OkButton != null) OkButton.IsEnabled = valid > 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var lines = UrlsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (IsValidLink(line))
            {
                ResultUrls.Add(line);
            }
        }

        if (ResultUrls.Count == 0)
        {
            DarkMessageBox.Show(this, Strings.IsTurkish ? "Lütfen geçerli en az bir indirme bağlantısı girin." : "Please enter at least one valid download link.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedDirectory = !string.IsNullOrWhiteSpace(TxtFolder?.Text) ? TxtFolder.Text.Trim() : null;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
