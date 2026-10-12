using System.Windows;
using System.Windows.Controls;

namespace McDm.App;

public static class WatermarkService
{
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.RegisterAttached(
            "Watermark",
            typeof(string),
            typeof(WatermarkService),
            new FrameworkPropertyMetadata(string.Empty));

    public static string GetWatermark(DependencyObject d) => (string)d.GetValue(WatermarkProperty);
    public static void SetWatermark(DependencyObject d, string value) => d.SetValue(WatermarkProperty, value);
}
