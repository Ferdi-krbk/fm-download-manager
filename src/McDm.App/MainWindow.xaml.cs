using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using McDm.Core;

namespace McDm.App;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand PasteCommand = new();
    private readonly DownloadManager _manager;
    private bool _allowClose;

    public MainWindow(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;
        Grid.ItemsSource = manager.Items;
    }

    public void AllowClose() { _allowClose = true; Close(); }

    // closing the window keeps the app (and the browser bridge) alive in the tray
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    private IEnumerable<DownloadItem> Selected => Grid.SelectedItems.Cast<DownloadItem>().ToList();

    private void PasteUrl_Click(object sender, RoutedEventArgs e)
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            StatusText.Text = "Clipboard does not contain an http(s) link.";
            return;
        }
        _manager.Add(new DownloadRequest { Url = text });
        StatusText.Text = "Added " + uri.Host;
    }

    private void Pause_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Pause(i.Id); }
    private void Resume_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Resume(i.Id); }
    private void Cancel_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Cancel(i.Id); }
    private void Remove_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Remove(i.Id); }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && File.Exists(i.SavePath))
            Process.Start("explorer.exe", $"/select,\"{i.SavePath}\"");
        else
            Process.Start("explorer.exe", $"\"{_manager.DefaultDownloadDirectory}\"");
    }

    private void Grid_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is DownloadItem { Status: DownloadStatus.Completed } i && File.Exists(i.SavePath))
            Process.Start(new ProcessStartInfo(i.SavePath) { UseShellExecute = true });
    }
}

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Format(value is long l ? l : -1);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    public static string Format(double bytes)
    {
        if (bytes < 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (bytes >= 1024 && i < u.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.#} {u[i]}";
    }
}

public sealed class SpeedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is double d && d > 0 ? BytesConverter.Format(d) + "/s" : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
