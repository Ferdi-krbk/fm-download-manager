using System.Windows.Media;

namespace McDm.App;

public static class FontCatalog
{
    // Preserve the resource base URI supplied by WPF's font collection. A plain
    // string FontFamily URI can otherwise silently fall back to a system face.
    public static FontFamily Interface { get; } = Fonts.GetFontFamilies(
        new Uri("pack://application:,,,/FMDownloadManager;component/Resources/Fonts/"))
        .Single(family => family.FamilyNames.Values.Contains("Inter"));
}
