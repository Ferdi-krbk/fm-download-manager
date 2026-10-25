using System.Dynamic;
using System.Runtime.InteropServices;
using McDm.Core;

namespace McDm.Tests;

public static class AntivirusRefreshTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> check)
    {
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
