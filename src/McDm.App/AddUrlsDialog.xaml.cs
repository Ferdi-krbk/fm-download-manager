using System.Windows;
using McDm.App.Resources;

namespace McDm.App;

public partial class AddUrlsDialog : Window
{
    public List<string> ResultUrls { get; } = new();

    public AddUrlsDialog()
    {
        InitializeComponent();
        Title = Strings.AddUrlsTitle;
        TxtWinHeader.Text = Strings.AddUrlsTitle;
        PromptText.Text = Strings.AddUrlsPrompt;
        OkButton.Content = Strings.AddUrlsButton;
        CancelButton.Content = Strings.SettingsCancel;

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

    private void UrlsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateValidCount();
    }

    private void UpdateValidCount()
    {
        if (UrlsBox == null || ValidLinksCountText == null) return;
        var lines = UrlsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int valid = lines.Count(l => Uri.TryCreate(l, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"));
        bool isTr = Strings.Culture?.TwoLetterISOLanguageName == "tr";
        ValidLinksCountText.Text = isTr ? $"{valid} geçerli bağlantı" : $"{valid} valid links";
        if (OkButton != null) OkButton.IsEnabled = valid > 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var lines = UrlsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                ResultUrls.Add(line);
            }
        }

        if (ResultUrls.Count == 0)
        {
            DarkMessageBox.Show(this, "Please enter at least one valid http(s) URL.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
