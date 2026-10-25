// Compiled against the Windows .NET Framework, independent of the app's .NET runtime.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class LegacyUpdateBootstrap
{
    [STAThread]
    private static int Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "FM-Update-" + Guid.NewGuid().ToString("N"));
        string installer = Path.Combine(folder, "FMDownloadManager-Setup-1.0.0.exe");
        try
        {
            Directory.CreateDirectory(folder);
            var assembly = Assembly.GetExecutingAssembly();
            using (var source = assembly.GetManifestResourceStream("installer.exe"))
            using (var destination = File.Create(installer)) source.CopyTo(destination);
            string expected;
            using (var reader = new StreamReader(assembly.GetManifestResourceStream("installer.sha256")))
                expected = reader.ReadToEnd().Trim();
            string actual;
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(installer))
                actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException("The embedded installer checksum did not match.");
            // Artifact validation must never install or alter an existing application.
            if (args.Length == 1 && args[0] == "--verify-only") return 0;

            // The old updater launches us immediately before shutting down.
            foreach (var process in Process.GetProcessesByName("FMDownloadManager"))
                using (process) if (!process.WaitForExit(30000))
                    throw new InvalidOperationException("FM is still running. Exit FM from the system tray and retry the update.");

            string installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FM Download Manager");
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D37D0B92-F9F1-4F2A-A618-971FE2F6F304}_is1"))
            {
                var existing = key == null ? null : key.GetValue("InstallLocation") as string;
                if (!String.IsNullOrWhiteSpace(existing) && Path.IsPathRooted(existing)) installPath = existing.TrimEnd('\\');
            }
            var start = new ProcessStartInfo(installer,
                "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /NORESTART /DIR=\"" + installPath + "\"")
                { UseShellExecute = true };
            using (var setup = Process.Start(start))
            {
                setup.WaitForExit();
                if (setup.ExitCode != 0) throw new InvalidOperationException("Setup returned code " + setup.ExitCode + ". Your existing download history was preserved.");
            }
            Process.Start(new ProcessStartInfo(Path.Combine(installPath, "FMDownloadManager.exe")) { UseShellExecute = true });
            return 0;
        }
        catch (Exception error)
        {
            if (args.Length == 1 && args[0] == "--verify-only") return 1;
            MessageBox.Show(error.Message, "FM Download Manager update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            try { File.Delete(installer); Directory.Delete(folder, false); } catch { }
        }
    }
}
