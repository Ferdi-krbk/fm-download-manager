using System.IO;
using System.Windows;
using McDm.Core;
using Forms = System.Windows.Forms;

namespace McDm.App;

public partial class DownloadPromptDialog : Window
{
    private readonly DownloadRequest _request;

    public DownloadPromptDialog(DownloadRequest request, string defaultDir)
    {
        InitializeComponent();
        _request = request;

        string name = !string.IsNullOrWhiteSpace(request.FileName) 
            ? request.FileName 
            : ResolveInitialName(request.Url);

        TxtFileName.Text = name;
        TxtFolder.Text = !string.IsNullOrWhiteSpace(request.Directory) ? request.Directory : defaultDir;
        TxtUrl.Text = request.Url;

        Loaded += (_, _) =>
        {
            TxtFileName.Focus();
            TxtFileName.SelectAll();
        };
    }

    private static string ResolveInitialName(string url)
    {
        if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(url, @"dn=([^&]+)");
            if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
            return "magnet_download";
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var seg = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(seg)) return Uri.UnescapeDataString(seg);
        }
        return "download";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(TxtFolder.Text) ? TxtFolder.Text : "",
            Description = "İndirilecek klasörü seçin:"
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            TxtFolder.Text = dialog.SelectedPath;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtFileName.Text))
        {
            _request.FileName = TxtFileName.Text.Trim();
        }
        if (!string.IsNullOrWhiteSpace(TxtFolder.Text))
        {
            _request.Directory = TxtFolder.Text.Trim();
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
