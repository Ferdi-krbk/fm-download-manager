using System.Windows;
using System.Windows.Media;

namespace McDm.App;

public static class ThemeManager
{
    public static bool IsAmoled { get; private set; } = false;

    public static void ToggleTheme()
    {
        SetAmoledTheme(!IsAmoled);
    }

    public static void SetAmoledTheme(bool amoled)
    {
        IsAmoled = amoled;
        var res = Application.Current.Resources;

        if (amoled)
        {
            res["Bg0Brush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
            res["Bg1Brush"] = new SolidColorBrush(Color.FromRgb(0x06, 0x06, 0x08));
            res["Bg2Brush"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x12));
            res["Bg3Brush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x20));
            res["LineBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x24));
            res["AltRowBrush"] = new SolidColorBrush(Color.FromRgb(0x03, 0x03, 0x05));
        }
        else
        {
            res["Bg0Brush"] = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x12));
            res["Bg1Brush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1B));
            res["Bg2Brush"] = new SolidColorBrush(Color.FromRgb(0x18, 0x1A, 0x24));
            res["Bg3Brush"] = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x35));
            res["LineBrush"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x22, 0x2F));
            res["AltRowBrush"] = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x15));
        }
    }
}
