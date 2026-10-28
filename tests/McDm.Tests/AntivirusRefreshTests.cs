using System.Dynamic;
using System.Runtime.InteropServices;
using McDm.Core;

namespace McDm.Tests;

public static class AntivirusRefreshTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check)
    {
        foreach (string mode in new[] { "scan-clean", "scan-failure", "scan-threat", "scan-policy" })
        await test($"antivirus: fallback policy respects third-party result ({mode})", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "mcdm-antivirus-policy", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "harmless.txt"), log = Path.Combine(directory, "calls.log");
            File.WriteAllText(file, "Harmless scanner process fixture");
            AntivirusProfile Profile(string name, string result) => new(name, "dotnet", "fixture",
                $"\"{typeof(AntivirusRefreshTests).Assembly.Location}\" --scanner-fixture {result} \"{log}\"");
            var item = new DownloadItem { SavePath = file };
            try
            {
                await DownloadManager.RunVirusScanAsync(item, null, null, CancellationToken.None,
                    new[] { Profile("Third-party scanner", mode), Profile("Microsoft Defender", "scan-clean") });
                check(mode is "scan-clean" or "scan-failure", "threat/policy result was ignored");
                check(item.ScanResult == "passed", "successful candidate was not recorded");
                check(item.Stage!.Contains(mode == "scan-failure" ? "Microsoft Defender" : "Third-party scanner"), "wrong successful scanner was displayed");
            }
            catch (FatalDownloadException)
            { check(mode is "scan-threat" or "scan-policy", "ordinary scanner failure became a threat"); check(item.ScanResult == "attention", "unsafe result did not request attention"); }
            check(File.ReadAllLines(log).Length == (mode == "scan-failure" ? 2 : 1), "Defender was called before needed or after a threat/policy block");
            check(File.ReadAllText(file) == "Harmless scanner process fixture", "fixture bytes were changed");
        });
        await test("antivirus: every third-party provider precedes all Defender routes", () =>
        {
            var native = new AntivirusProfile("Kaspersky", "avp.com", "kaspersky");
            var defender = new AntivirusProfile("Microsoft Defender", "MpCmdRun.exe", "defender");
            var providers = new[] {
                new WindowsAntivirus.RegisteredProvider(Guid.NewGuid(), "Microsoft Defender / AMSI", true),
                new WindowsAntivirus.RegisteredProvider(Guid.NewGuid(), "Bitdefender / AMSI", false),
                new WindowsAntivirus.RegisteredProvider(Guid.NewGuid(), "Avast / AMSI", false),
                new WindowsAntivirus.RegisteredProvider(Guid.NewGuid(), "ESET / AMSI", false)
            };
            var ordered = AntivirusDetection.BuildCandidates(native, providers, "fixture-host.exe", defender);
            check(ordered.Select(profile => profile.Kind).SequenceEqual(new[] { "kaspersky", "amsi", "amsi", "amsi", "defender", "amsi-microsoft" }), "Microsoft route preceded a third-party scanner");
            var noNative = AntivirusDetection.BuildCandidates(null, providers, "fixture-host.exe", defender);
            check(noNative[0].Name.StartsWith("Bitdefender"), "priority depended on having Kaspersky installed");
            var command = noNative[0].ForFile(Path.GetFullPath("fixture.txt"));
            check(command.ArgumentList.SequenceEqual(new[] { "--antivirus-provider", providers[1].Id.ToString("D"), Path.GetFullPath("fixture.txt") }), "worker did not pin the selected provider identity");
            check(WindowsAntivirus.RunProviderWorker(Guid.NewGuid().ToString("D"), "missing") == 20, "unregistered COM provider was accepted");
            return Task.CompletedTask;
        });
        await test("antivirus: Kaspersky completion requires actual scanned objects and complete statistics", () =>
        {
            const string clean = "2026-10-28 15:35:45\tScan_Objects$16443 completed\n; Processed objects: 1\n; Total detected: 0\n; Suspicions: 0\n; Total skipped: 0\n; Password protected: 0\n; Corrupted: 0\n; Errors: 0\n";
            check(AntivirusScanner.KasperskyScanCompleted(clean), "real completed Kaspersky statistics were rejected");
            check(!AntivirusScanner.ReportsThreat(clean), "zero detected objects became a threat");
            check(AntivirusScanner.ReportsThreat(clean.Replace("Total detected: 0", "Total detected: 1")), "Kaspersky semicolon-prefixed detection was ignored");
            foreach (string label in new[] { "Suspicions", "Total skipped", "Password protected", "Corrupted", "Errors" })
                check(!AntivirusScanner.KasperskyScanCompleted(clean.Replace(label + ": 0", label + ": 1")), "incomplete scan was accepted: " + label);
            check(!AntivirusScanner.KasperskyScanCompleted(clean.Replace("Processed objects: 1", "Processed objects: 0")), "zero scanned objects were accepted");
            check(!AntivirusScanner.KasperskyScanCompleted("Unknown command\nExit code 0"), "zero exit without a scan was accepted");
            check(!AntivirusScanner.KasperskyScanCompleted(clean.Replace("completed", "running")), "unfinished scan was accepted");
            return Task.CompletedTask;
        });
        await test("antivirus: WMI product names use per-instance property lookup", () =>
        {
            foreach (string name in new[] { "Kaspersky", "Windows Defender", "Bitdefender", "Kaspersky" })
                check(AntivirusDetection.ReadProductDisplayName(new WmiProduct(name)) == name,
                    "WMI instance name was read through a stale synthetic dispatch property");
            return Task.CompletedTask;
        });
        await test("antivirus: registered product names survive repeated worker-thread refresh", async () =>
        {
            var baseline = await Task.Run(AntivirusDetection.InstalledProductNames);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var names = await Task.Run(AntivirusDetection.InstalledProductNames);
                check(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(baseline.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
                    "registered products changed or vanished during repeated detection");
            }
            if (baseline.Count == 0) Console.WriteLine("NOTE no Security Center products registered on this test host; live vendor-name coverage requires a desktop Windows installation.");
        });
    }

    // SWbem synthetic properties can have different dispatch IDs per instance.
    // Model the observed E_FAIL while keeping the documented property collection usable.
    public sealed class WmiProduct(string name) : DynamicObject
    {
        public PropertySet Properties_ { get; } = new(name);
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            result = null;
            if (binder.Name == "displayName") throw new COMException("Stale WMI dispatch property", unchecked((int)0x80004005));
            return false;
        }
    }
    public sealed class PropertySet(string name)
    {
        public WmiProperty Item(string property, int flags)
            => property == "displayName" && flags == 0 ? new(name) : throw new ArgumentException("Unexpected WMI property lookup");
    }
    public sealed record WmiProperty(string Value);
}
