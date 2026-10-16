using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace McDm.App;

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

public sealed class ProgressToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush MuteBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Color RedColor = Color.FromRgb(0xD9, 0x16, 0x3D);
    private static readonly Color AmberColor = Color.FromRgb(0xEA, 0xB3, 0x08);
    private static readonly Color GreenColor = Color.FromRgb(0x10, 0xB9, 0x81);

    static ProgressToBrushConverter()
    {
        MuteBrush.Freeze();
    }

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is double progress)
        {
            if (progress <= 0)
            {
                var red = new SolidColorBrush(RedColor);
                red.Freeze();
                return red;
            }
            if (progress >= 1.0)
            {
                var green = new SolidColorBrush(GreenColor);
                green.Freeze();
                return green;
            }

            Color res;
            if (progress <= 0.55)
            {
                double factor = progress / 0.55;
                byte r = (byte)(RedColor.R + (AmberColor.R - RedColor.R) * factor);
                byte g = (byte)(RedColor.G + (AmberColor.G - RedColor.G) * factor);
                byte b = (byte)(RedColor.B + (AmberColor.B - RedColor.B) * factor);
                res = Color.FromRgb(r, g, b);
            }
            else
            {
                double factor = (progress - 0.55) / 0.45;
                byte r = (byte)(AmberColor.R + (GreenColor.R - AmberColor.R) * factor);
                byte g = (byte)(AmberColor.G + (GreenColor.G - AmberColor.G) * factor);
                byte b = (byte)(AmberColor.B + (GreenColor.B - AmberColor.B) * factor);
                res = Color.FromRgb(r, g, b);
            }
            var brush = new SolidColorBrush(res);
            brush.Freeze();
            return brush;
        }
        return MuteBrush;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
