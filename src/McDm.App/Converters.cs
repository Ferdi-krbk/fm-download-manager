using System.Globalization;
using System.Windows.Data;

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
