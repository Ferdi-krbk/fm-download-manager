using System.Diagnostics;

namespace McDm.Core;

public sealed record AntivirusRunResult(int ExitCode, bool ThreatReported, bool PolicyBlocked = false, bool ScanCompleted = false);

public static class AntivirusScanner
{
    /// <summary>Runs an external scanner without buffering its output or leaving child processes behind.</summary>
    public static async Task<int> RunAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancel) =>
        (await RunDetailedAsync(start, timeout, cancel).ConfigureAwait(false)).ExitCode;

    public static bool ReportsThreat(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"(?im)^\s*;?\s*(?:Threats detected|Detected threats|Detected objects|Detected viruses|Total detected|Infected(?: objects| files)?|Detected)\s*[:=]\s*[1-9][0-9]*\b");

    public static bool KasperskyScanCompleted(string text)
    {
        long? Count(string label)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text,
                @"(?im)^\s*;?\s*" + System.Text.RegularExpressions.Regex.Escape(label) + @"\s*:\s*(\d+)\s*$");
            return match.Success && long.TryParse(match.Groups[1].Value, out var count) ? count : null;
        }
        return Count("Processed objects") > 0 && Count("Total detected").HasValue && Count("Suspicions") == 0
            && Count("Errors") == 0 && Count("Total skipped") == 0
            && Count("Password protected") == 0 && Count("Corrupted") == 0
            && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)\bScan_Objects\$\S+\s+completed\b");
    }

    private static async Task<string> TailAsync(StreamReader reader)
    {
        var tail = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 16384) tail.Remove(0, tail.Length - 16384);
        }
        return tail.ToString();
    }

    public static async Task<AntivirusRunResult> RunDetailedAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancel)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new IOException("Scanner could not be started");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        var stdout = TailAsync(process.StandardOutput);
        var stderr = TailAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(deadline.Token).ConfigureAwait(false);
            string output = stdout.Result + "\n" + stderr.Result;
            return new(process.ExitCode, ReportsThreat(output), output.Contains("FM_AV_POLICY_BLOCK", StringComparison.Ordinal),
                KasperskyScanCompleted(output) || output.Contains("FM_AV_SCAN_COMPLETED", StringComparison.Ordinal));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
            if (cancel.IsCancellationRequested) throw;
            throw new TimeoutException("Antivirus scan timed out");
        }
    }
}
