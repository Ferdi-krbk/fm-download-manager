using System.Diagnostics;

namespace McDm.Core;

public sealed record AntivirusProfile(string Name, string Executable, string Kind, string? CustomArguments = null)
{
    public ProcessStartInfo ForFile(string file)
    {
        var start = new ProcessStartInfo(Executable);
        if (Kind == "windows")
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(Executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
            start.ArgumentList.Add("--antivirus-file");
            start.ArgumentList.Add(Path.GetFullPath(file));
        }
        else if (Kind == "kaspersky")
        {
            foreach (string argument in new[] { "SCAN", file, "/i0", "/fa" }) start.ArgumentList.Add(argument);
        }
        else if (Kind == "defender")
        {
            foreach (string argument in new[] { "-Scan", "-ScanType", "3", "-File", file }) start.ArgumentList.Add(argument);
        }
        else
        {
            string template = string.IsNullOrWhiteSpace(CustomArguments) ? "\"{file}\"" : CustomArguments.Trim();
            start.Arguments = template.Contains("{file}", StringComparison.OrdinalIgnoreCase)
                ? template.Replace("{file}", file, StringComparison.OrdinalIgnoreCase) : $"{template} \"{file}\"";
        }
        return start;
    }
}

public static class AntivirusDetection
{
    public static IReadOnlyList<string> InstalledProductNames()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
        object? locator = null, services = null, results = null;
        var names = new List<string>();
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type == null) return names;
            locator = Activator.CreateInstance(type);
            services = ((dynamic)locator!).ConnectServer(".", @"root\SecurityCenter2");
            results = ((dynamic)services!).ExecQuery("SELECT displayName FROM AntiVirusProduct");
            foreach (object product in (System.Collections.IEnumerable)results!)
            {
                try
                {
                    string? name = ReadProductDisplayName(product);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                finally { ReleaseCom(product); }
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally
        {
            foreach (var item in new[] { results, services, locator })
                ReleaseCom(item);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? ReadProductDisplayName(object product)
    {
        object? properties = null, property = null;
        try
        {
            // WMI's synthetic displayName dispatch ID belongs to an individual instance.
            // C# dynamic can cache it across products/queries and subsequently fail with
            // E_FAIL. Look the name up through the stable SWbem property collection instead.
            properties = ((dynamic)product).Properties_;
            property = ((dynamic)properties).Item("displayName", 0);
            return ((dynamic)property).Value as string;
        }
        finally { ReleaseCom(property); ReleaseCom(properties); }
    }

    private static void ReleaseCom(object? item)
    {
        if (OperatingSystem.IsWindows() && item != null && System.Runtime.InteropServices.Marshal.IsComObject(item))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(item);
    }

    public static AntivirusProfile? Detect(string? previousPath = null, string? previousArguments = null, IEnumerable<string>? installRoots = null, bool allowWindowsIntegration = true)
    {
        if (allowWindowsIntegration && installRoots == null && WindowsAntivirus.IsAvailable
            && System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name is "FMDownloadManager" or "McDm.Tests" or "McDm.Acceptance"
            && Environment.ProcessPath is { } host)
            return new("Windows", host, "windows");
        // Legacy settings often point at Kaspersky's GUI. Never run that as a scanner.
        if (!string.IsNullOrWhiteSpace(previousPath))
        {
            string name = Path.GetFileName(previousPath).ToLowerInvariant();
            if (name is "avpui.exe" or "avp.exe" or "avp.com")
            {
                string console = Path.Combine(Path.GetDirectoryName(previousPath) ?? "", "avp.com");
                if (File.Exists(console)) return new("Kaspersky", console, "kaspersky");
            }
            else if (name == "mpcmdrun.exe" && File.Exists(previousPath))
                return new("Microsoft Defender", previousPath, "defender");
        }

        var roots = installRoots?.ToArray() ?? new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (string root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (string vendor in new[] { "Kaspersky Lab", "Kaspersky" })
        {
            string folder = Path.Combine(root, vendor);
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (string product in Directory.EnumerateDirectories(folder).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase).Prepend(folder))
                {
                    string console = Path.Combine(product, "avp.com");
                    if (File.Exists(console)) return new("Kaspersky", console, "kaspersky");
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        // Tests can pass explicit roots without accidentally selecting the host's antivirus.
        if (installRoots != null || !OperatingSystem.IsWindows()) return null;
        string platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
        try
        {
            if (Directory.Exists(platform))
                foreach (string version in Directory.EnumerateDirectories(platform).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string console = Path.Combine(version, "MpCmdRun.exe");
                    if (File.Exists(console)) return new("Microsoft Defender", console, "defender");
                }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        string direct = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        return File.Exists(direct) ? new("Microsoft Defender", direct, "defender") : null;
    }
}
