using System.Windows;
using McDm.Core;

namespace McDm.App;

public partial class VideoQualityDialog : Window
{
    public string SelectedFormat { get; private set; } = "bestvideo+bestaudio/best";
    public bool AudioOnly { get; private set; } = false;

    public VideoQualityDialog(string url, string? title = null)
    {
        InitializeComponent();
        TxtVideoUrl.Text = url;
        TxtVideoTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video İndirmesi" : title;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (OptAudioOnly.IsChecked == true)
        {
            SelectedFormat = "ba/b/bestaudio/best";
            AudioOnly = true;
        }
        else if (Opt720p.IsChecked == true)
        {
            SelectedFormat = "bv*[height<=720]+ba/b[height<=720]/bv*+ba/b/best";
        }
        else if (Opt1080p.IsChecked == true)
        {
            SelectedFormat = "bv*[height<=1080]+ba/b[height<=1080]/bv*+ba/b/best";
        }
        else
        {
            SelectedFormat = "bv*+ba/b/best";
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
