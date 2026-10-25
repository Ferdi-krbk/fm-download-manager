using System.Text.RegularExpressions;

namespace McDm.Core;

public sealed record MultipartIdentity(string Key, string Label);

public static class MultipartFiles
{
    private static readonly Regex Parts = new(@"^(?<stem>.+?)[._ -]part[._ -]?\d{1,5}\.(?<ext>rar|7z|zip|bin|iso)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Volumes = new(@"^(?<stem>.+)\.(?<ext>rar|7z|zip|iso)\.\d{3,5}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Legacy = new(@"^(?<stem>.+)\.(?<ext>r|z)\d{2,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static MultipartIdentity? Describe(string fileName, string directory)
    {
        foreach (var pattern in new[] { Parts, Volumes, Legacy })
        {
            var match = pattern.Match(fileName);
            if (!match.Success) continue;
            string extension = match.Groups["ext"].Value.ToLowerInvariant();
            extension = extension switch { "r" => "rar", "z" => "zip", _ => extension };
            return Identity(match.Groups["stem"].Value, extension, directory);
        }
        return null;
    }

    // A plain .rar/.zip only joins a group if actual numbered siblings are present.
    public static MultipartIdentity? BaseArchive(string fileName, string directory)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return extension is "rar" or "zip" or "7z" or "iso"
            ? Identity(Path.GetFileNameWithoutExtension(fileName), extension, directory) : null;
    }

    private static MultipartIdentity Identity(string stem, string extension, string directory)
    {
        string folder;
        try { folder = Path.GetFullPath(string.IsNullOrWhiteSpace(directory) ? "." : directory).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { folder = directory; }
        string label = stem + "." + extension;
        return new(folder.ToUpperInvariant() + "\0" + label.ToUpperInvariant(), label);
    }
}
