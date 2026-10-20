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

        string name = !string.IsNullOrWhiteSpace(request.FileName) && !FileNames.IsGenericFileName(request.FileName)
            ? request.FileName 
            : ResolveInitialName(request.Url);

        TxtFileName.Text = name;
        TxtFolder.Text = !string.IsNullOrWhiteSpace(request.Directory) ? request.Directory : defaultDir;
        TxtUrl.Text = request.Url;

        ApplyLocalization();

        Loaded += (_, _) =>
        {
            TxtFileName.Focus();
            TxtFileName.SelectAll();
        };
    }

    private void ApplyLocalization()
    {
        Title = McDm.App.Resources.Strings.DownloadPromptTitleBar;
        TxtWinHeader.Text = McDm.App.Resources.Strings.DownloadPromptTitleBar;
        TxtHeaderQuestion.Text = McDm.App.Resources.Strings.DownloadPromptQuestion;
        LblFileName.Text = McDm.App.Resources.Strings.FileName;
        LblFolder.Text = McDm.App.Resources.Strings.SaveFolder;
        BtnBrowse.Content = McDm.App.Resources.Strings.Browse;
        LblUrl.Text = McDm.App.Resources.Strings.WebAddress;
        BtnCancel.Content = McDm.App.Resources.Strings.SettingsCancel;
        BtnStart.Content = McDm.App.Resources.Strings.DownloadNow;
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
            if (!string.IsNullOrWhiteSpace(seg) && !FileNames.IsGenericFileName(seg)) return Uri.UnescapeDataString(seg);
        }
        return "download";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(TxtFolder.Text) ? TxtFolder.Text : "",
            Description = McDm.App.Resources.Strings.SelectFolder
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
            var editedName = TxtFileName.Text.Trim();
            // If user input has no extension, but URL has a known extension, preserve it!
            if (string.IsNullOrEmpty(Path.GetExtension(editedName)) && Uri.TryCreate(_request.Url, UriKind.Absolute, out var u))
            {
                var ext = Path.GetExtension(u.AbsolutePath);
                if (!string.IsNullOrEmpty(ext) && ext.Length <= 8)
                {
                    editedName += ext;
                }
            }
            _request.FileName = editedName;
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
