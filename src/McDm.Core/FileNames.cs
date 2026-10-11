namespace McDm.Core;

public static class FileNames
{
    private static readonly char[] WindowsInvalid = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
    };

    /// <summary>Makes any server/browser supplied name safe as a single Windows file name.</summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "download";
        name = Path.GetFileName(name.Replace('\\', '/'));
        var chars = name.Select(c => c < 32 || WindowsInvalid.Contains(c) ? '_' : c).ToArray();
        name = new string(chars).Trim().TrimEnd('.', ' ');
        if (name.Length == 0) return "download";

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        if (Reserved.Contains(stem)) stem = "_" + stem;
        if (stem.Length + ext.Length > 200) stem = stem[..Math.Max(1, 200 - ext.Length)];
        return stem + ext;
    }

    /// <summary>Picks a path in <paramref name="dir"/> that collides with neither finished nor in-progress files.</summary>
    public static string Unique(string dir, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 0; ; i++)
        {
            var candidate = i == 0 ? fileName : $"{stem} ({i}){ext}";
            var path = Path.Combine(dir, candidate);
            if (!File.Exists(path) && !File.Exists(path + ".mcdm-part") && !File.Exists(path + ".mcdm"))
                return path;
        }
    }
}
