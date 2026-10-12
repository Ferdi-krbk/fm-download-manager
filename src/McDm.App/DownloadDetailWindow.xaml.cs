using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using McDm.App.Resources;
using McDm.Core;

namespace McDm.App;

public partial class DownloadDetailWindow : Window
{
    private readonly DownloadItem _item;
    private readonly DispatcherTimer _timer;

    public sealed class SegmentRow
    {
        public int Index { get; set; }
        public string RangeText { get; set; } = "";
        public string DoneText { get; set; } = "";
        public string PercentText { get; set; } = "";
    }

    public DownloadDetailWindow(DownloadItem item)
    {
        InitializeComponent();
        _item = item;

        ApplyLocalization();
        UpdateInfo();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => UpdateInfo();
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void ApplyLocalization()
    {
        Title = Strings.DetailsTitle;
        LblFile.Text = Strings.DetailsFile;
        LblUrl.Text = Strings.DetailsUrl;
        LblStatus.Text = Strings.DetailsStatus;
        LblDownloaded.Text = Strings.DetailsDownloaded;
        LblSpeed.Text = Strings.DetailsSpeed;
        GrpSegments.Header = Strings.DetailsSegments;
        BtnClose.Content = Strings.SettingsCancel; // or close label
    }

    private void UpdateInfo()
    {
        TxtFileName.Text = string.IsNullOrEmpty(_item.FileName) ? "—" : _item.FileName;
        TxtUrl.Text = _item.Request.Url;
        TxtStatus.Text = _item.Stage != null ? $"{_item.Status} ({_item.Stage})" : _item.Status.ToString();

        string dlText = BytesConverter.Format(_item.DownloadedBytes);
        string totalText = _item.TotalBytes > 0 ? BytesConverter.Format(_item.TotalBytes) : "—";
        TxtDownloaded.Text = $"{dlText} / {totalText}";

        TxtSpeed.Text = _item.Speed > 0 ? $"{BytesConverter.Format(_item.Speed)}/s ({_item.ActiveConnections} connections)" : "0 B/s";
        PrgOverall.Value = _item.Progress;

        // Try reading segment state file if present
        var statePath = _item.SavePath + ".mcdm";
        if (File.Exists(statePath))
        {
            try
            {
                var json = File.ReadAllText(statePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Chunks", out var chunksEl) && chunksEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<SegmentRow>();
                    int idx = 0;
                    foreach (var c in chunksEl.EnumerateArray())
                    {
                        long start = c.GetProperty("Start").GetInt64();
                        long end = c.GetProperty("End").GetInt64();
                        long done = c.GetProperty("Done").GetInt64();
                        long length = end < 0 ? -1 : end - start + 1;
                        double pct = length > 0 ? (double)done / length : 0;

                        list.Add(new SegmentRow
                        {
                            Index = ++idx,
                            RangeText = end < 0 ? $"{start} - ?" : $"{start} - {end}",
                            DoneText = $"{BytesConverter.Format(done)} / {(length < 0 ? "—" : BytesConverter.Format(length))}",
                            PercentText = $"{pct:P0}"
                        });
                    }
                    GridSegments.ItemsSource = list;
                    return;
                }
            }
            catch { }
        }

        // If no segment state file, create a synthetic single-segment row if downloading or completed
        if (_item.DownloadedBytes > 0 || _item.TotalBytes > 0)
        {
            GridSegments.ItemsSource = new List<SegmentRow>
            {
                new()
                {
                    Index = 1,
                    RangeText = _item.TotalBytes > 0 ? $"0 - {_item.TotalBytes - 1}" : "0 - ?",
                    DoneText = $"{BytesConverter.Format(_item.DownloadedBytes)} / {(_item.TotalBytes > 0 ? BytesConverter.Format(_item.TotalBytes) : "—")}",
                    PercentText = $"{_item.Progress:P0}"
                }
            };
        }
        else
        {
            GridSegments.ItemsSource = null;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
