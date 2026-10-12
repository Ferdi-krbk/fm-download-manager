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
            res["Bg1Brush"] = new SolidColorBrush(Color.FromRgb(0x07, 0x07, 0x0A));
            res["Bg2Brush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x10, 0x15));
            res["Bg3Brush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x17, 0x20));
            res["LineBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1D, 0x24));
            res["AltRowBrush"] = new SolidColorBrush(Color.FromRgb(0x03, 0x03, 0x05));
        }
        else
        {
            res["Bg0Brush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x10, 0x14));
            res["Bg1Brush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x17, 0x1E));
            res["Bg2Brush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x2A));
            res["Bg3Brush"] = new SolidColorBrush(Color.FromRgb(0x27, 0x2A, 0x38));
            res["LineBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x27, 0x33));
            res["AltRowBrush"] = new SolidColorBrush(Color.FromRgb(0x13, 0x14, 0x1A));
        }
    }
}
