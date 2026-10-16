using System.Windows;
using System.Windows.Media;

namespace McDm.App;

public partial class DarkMessageBox : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public DarkMessageBox(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        TxtTitle.Text = string.IsNullOrWhiteSpace(title) ? "FM DOWNLOAD MANAGER" : title;
        TxtMessage.Text = message;

        // Setup Icon Path
        switch (image)
        {
            case MessageBoxImage.Information:
                // Circle info icon
                IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
                IconPath.Fill = (Brush)FindResource("AccentBrush");
                break;
            case MessageBoxImage.Warning:
                // Warning triangle
                IconPath.Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
                IconPath.Fill = (Brush)FindResource("AccentBrush");
                break;
            case MessageBoxImage.Error:
                // Error circle with cross
                IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z");
                IconPath.Fill = (Brush)FindResource("DangerBrush");
                break;
            case MessageBoxImage.Question:
                // Question circle
                IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.75z");
                IconPath.Fill = (Brush)FindResource("AccentBrush");
                break;
            default:
                IconBox.Visibility = Visibility.Collapsed;
                break;
        }

        // Setup Buttons
        BtnYes.Content = McDm.App.Resources.Strings.Yes;
        BtnNo.Content = McDm.App.Resources.Strings.No;
        BtnOk.Content = McDm.App.Resources.Strings.Ok;
        BtnCancel.Content = McDm.App.Resources.Strings.SettingsCancel;

        BtnYes.Visibility = Visibility.Collapsed;
        BtnNo.Visibility = Visibility.Collapsed;
        BtnOk.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        switch (buttons)
        {
            case MessageBoxButton.OK:
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                break;
            case MessageBoxButton.OKCancel:
                BtnOk.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;
            case MessageBoxButton.YesNo:
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnNo.IsCancel = true;
                break;
            case MessageBoxButton.YesNoCancel:
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;
        }
    }

    public static MessageBoxResult Show(Window? owner, string message, string title = "FM Download Manager", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var win = new DarkMessageBox(message, title, buttons, image);
        if (owner != null && owner.IsVisible)
        {
            win.Owner = owner;
        }
        else if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        else
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        win.ShowDialog();
        return win.Result;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.None;
        Close();
    }
}
