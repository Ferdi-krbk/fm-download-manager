using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace McDm.App;

public record AccentPreset(string Key, string Name, string Hex);

public static class ThemeManager
{
    public static readonly IReadOnlyList<AccentPreset> Presets = new AccentPreset[]
    {
        new("Crimson", "Kızıl Alev (Crimson)", "#E50914"),
        new("Cyan", "Siber Mavi (Electric Cyan)", "#00E5FF"),
        new("Emerald", "Zümrüt Yeşil (Matrix)", "#10B981"),
        new("Violet", "Neon Mor (Synthwave)", "#8B5CF6"),
        new("Orange", "Volkan Turuncu (Amber)", "#FF6B00"),
        new("Gold", "Lazer Altın (Gold)", "#F59E0B"),
        new("Pink", "Cyber Pink (Magenta)", "#EC4899"),
        new("Silver", "Titanyum Gümüş (Silver)", "#94A3B8"),
    };

    public static string CurrentAccentHex { get; private set; } = "#E50914";
    public static event Action<string>? AccentColorChanged;

    public static string GetPresetDisplayName(AccentPreset preset, bool isTurkish) => isTurkish ? preset.Name : preset.Key switch
    {
        "Crimson" => "Crimson Blaze (Red)",
        "Cyan" => "Electric Cyan (Cyber Blue)",
        "Emerald" => "Matrix Emerald (Green)",
        "Violet" => "Synthwave Violet (Purple)",
        "Orange" => "Amber Orange",
        "Gold" => "Laser Gold",
        "Pink" => "Cyber Pink (Magenta)",
        "Silver" => "Titanium Silver",
        _ => preset.Key
    };

    public static bool IsAmoled { get; private set; } = false;

    public static void ToggleTheme()
    {
        SetAmoledTheme(!IsAmoled);
    }

    public static void SetAmoledTheme(bool amoled)
    {
        IsAmoled = amoled;
        var res = Application.Current?.Resources;
        if (res == null) return;

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

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);

        try
        {
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                color = Color.FromRgb(r, g, b);
                return true;
            }
            if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static void ApplyAccentColor(string? hex)
    {
        if (!TryParseHex(hex, out var c))
        {
            c = Color.FromRgb(0xE5, 0x09, 0x14); // Fallback to crimson
            hex = "#E50914";
        }

        CurrentAccentHex = hex!.StartsWith("#") ? hex.ToUpperInvariant() : ("#" + hex).ToUpperInvariant();

        // Calculate lighter hover color
        byte rH = (byte)Math.Min(255, c.R + Math.Max(25, (255 - c.R) / 3));
        byte gH = (byte)Math.Min(255, c.G + Math.Max(25, (255 - c.G) / 3));
        byte bH = (byte)Math.Min(255, c.B + Math.Max(25, (255 - c.B) / 3));
        var colorHover = Color.FromRgb(rH, gH, bH);

        // Calculate darker press color
        byte rP = (byte)(c.R * 0.75);
        byte gP = (byte)(c.G * 0.75);
        byte bP = (byte)(c.B * 0.75);
        var colorPress = Color.FromRgb(rP, gP, bP);

        // Translucent dim tint
        var colorDim = Color.FromArgb(0x28, c.R, c.G, c.B);

        // Optimal foreground on accent (white vs dark charcoal based on perceived luminance)
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        var colorOnAccent = lum > 0.65 ? Color.FromRgb(0x0A, 0x0B, 0x0E) : Colors.White;

        var colorFlame = colorHover;

        // Build frozen brushes
        var accentBrush = new SolidColorBrush(c); accentBrush.Freeze();
        var accentHoverBrush = new SolidColorBrush(colorHover); accentHoverBrush.Freeze();
        var accentPressBrush = new SolidColorBrush(colorPress); accentPressBrush.Freeze();
        var accentDimBrush = new SolidColorBrush(colorDim); accentDimBrush.Freeze();
        var onAccentBrush = new SolidColorBrush(colorOnAccent); onAccentBrush.Freeze();
        var flameBrush = new SolidColorBrush(colorFlame); flameBrush.Freeze();

        var accentGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        accentGradient.GradientStops.Add(new GradientStop(c, 0));
        accentGradient.GradientStops.Add(new GradientStop(colorPress, 1));
        accentGradient.Freeze();

        var accentHoverGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        accentHoverGradient.GradientStops.Add(new GradientStop(colorHover, 0));
        accentHoverGradient.GradientStops.Add(new GradientStop(c, 1));
        accentHoverGradient.Freeze();

        var progressGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        progressGradient.GradientStops.Add(new GradientStop(colorHover, 0));
        progressGradient.GradientStops.Add(new GradientStop(c, 0.6));
        progressGradient.GradientStops.Add(new GradientStop(colorPress, 1));
        progressGradient.Freeze();

        var speedGraphFill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        speedGraphFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x45, c.R, c.G, c.B), 0));
        speedGraphFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x02, 0x0C, 0x0D, 0x12), 1));
        speedGraphFill.Freeze();

        var res = Application.Current?.Resources;
        if (res != null)
        {
            res["ColorAccent"] = c;
            res["ColorAccentHover"] = colorHover;
            res["ColorAccentPress"] = colorPress;
            res["ColorAccentDim"] = colorDim;
            res["ColorOnAccent"] = colorOnAccent;
            res["ColorFlame"] = colorFlame;

            res["AccentBrush"] = accentBrush;
            res["AccentHoverBrush"] = accentHoverBrush;
            res["AccentPressBrush"] = accentPressBrush;
            res["AccentDimBrush"] = accentDimBrush;
            res["OnAccentBrush"] = onAccentBrush;
            res["FlameBrush"] = flameBrush;

            res["AccentGradientBrush"] = accentGradient;
            res["AccentHoverGradientBrush"] = accentHoverGradient;
            res["ProgressGradient"] = progressGradient;
            res["SpeedGraphFillBrush"] = speedGraphFill;
        }

        AccentColorChanged?.Invoke(CurrentAccentHex);
    }
}
