using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McDm.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ───────────── test origin server ─────────────
var rnd = new Random(42);
byte[] Big = new byte[12 * 1024 * 1024]; rnd.NextBytes(Big);
byte[] Slow = new byte[6 * 1024 * 1024]; rnd.NextBytes(Slow);
byte[] Small = new byte[3 * 1024 * 1024]; rnd.NextBytes(Small);
byte[] Tiny = new byte[100 * 1024]; rnd.NextBytes(Tiny);

int cur = 0, maxConc = 0; long slowBytesSent = 0, flakyCalls = 0;

var sb = WebApplication.CreateBuilder();
sb.Logging.ClearProviders();
sb.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
var origin = sb.Build();

origin.MapGet("/file", ctx => Serve(ctx, Big, ranges: true, track: true));
origin.MapGet("/slow", ctx => Serve(ctx, Slow, ranges: true, delayMs: 100, countSlow: true));
origin.MapGet("/norange", ctx => Serve(ctx, Small, ranges: false));
origin.MapGet("/unknown", ctx => Serve(ctx, Small, ranges: false, chunked: true));
origin.MapGet("/named", ctx => { ctx.Response.Headers.ContentDisposition = "attachment; filename=\"report.bin\""; return Serve(ctx, Tiny, true); });
origin.MapGet("/flaky", async ctx =>
{
    bool probe = ctx.Request.Headers.Range.ToString() == "bytes=0-0";
    if (!probe)
    {
        long n = Interlocked.Increment(ref flakyCalls);
        if (n <= 4) { ctx.Response.StatusCode = 429; ctx.Response.Headers.RetryAfter = "1"; return; }
    }
    await Serve(ctx, Big, true);
});
int originPort = 0;
origin.MapGet("/redir", ctx => { ctx.Response.StatusCode = 302; ctx.Response.Headers.Location = $"http://localhost:{originPort}/echo-cookie"; return Task.CompletedTask; });
origin.MapGet("/echo-cookie", async ctx =>
{
    ctx.Response.ContentType = "text/plain";
    await ctx.Response.WriteAsync("cookie=" + ctx.Request.Headers.Cookie.ToString());
});

await origin.StartAsync();
originPort = new Uri(origin.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
string Base = $"http://127.0.0.1:{originPort}";

async Task Serve(HttpContext ctx, byte[] data, bool ranges, int delayMs = 0, bool chunked = false, bool track = false, bool countSlow = false)
{
    long start = 0, end = data.Length - 1;
    var rh = ctx.Request.Headers.Range.ToString();
    if (ranges && rh.StartsWith("bytes="))
    {
        var spec = rh[6..].Split('-');
        start = long.Parse(spec[0]);
        end = spec[1].Length > 0 ? Math.Min(long.Parse(spec[1]), data.Length - 1) : data.Length - 1;
        ctx.Response.StatusCode = 206;
        ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{data.Length}";
    }
    if (ranges) ctx.Response.Headers.AcceptRanges = "bytes";
    ctx.Response.ContentType = "application/octet-stream";
    ctx.Response.Headers.ETag = "\"v1\"";
    if (!chunked) ctx.Response.ContentLength = end - start + 1;

    int now = Interlocked.Increment(ref cur);
    if (track) { int m; while (now > (m = Volatile.Read(ref maxConc))) Interlocked.CompareExchange(ref maxConc, now, m); }
    try
    {
        for (long p = start; p <= end; p += 32768)
        {
            int n = (int)Math.Min(32768, end - p + 1);
            await ctx.Response.Body.WriteAsync(data.AsMemory((int)p, n), ctx.RequestAborted);
            if (countSlow) Interlocked.Add(ref slowBytesSent, n);
            if (delayMs > 0) await Task.Delay(delayMs, ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    finally { Interlocked.Decrement(ref cur); }
}

// ───────────── tiny test harness ─────────────
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> body)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await body(); passed++; Console.WriteLine($"PASS  {name}  ({sw.Elapsed.TotalSeconds:F1}s)"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL  {name}: {ex.Message}"); }
}
void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }
string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
string HashFile(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
async Task WaitFor(Func<bool> cond, int seconds, string what)
{
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end) { if (cond()) return; await Task.Delay(50); }
    throw new Exception("timeout waiting for " + what);
}
string Tmp()
{
    var d = Path.Combine(Path.GetTempPath(), "mcdm-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(d); return d;
}
DownloadManager NewManager(string root, string? dl = null) =>
    new(Path.Combine(root, "data"), dl ?? Path.Combine(root, "dl"),
        new SegmentedDownloader(null, new EngineOptions { MinChunkBytes = 256 * 1024, ThrottleBackoff = TimeSpan.FromSeconds(1) }));

// ───────────── tests ─────────────
await Test("multi-connection download is byte-exact", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var it = m.Add(new DownloadRequest { Url = Base + "/file", FileName = "big.bin" });
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 30, "completion");
    Check(it.Status == DownloadStatus.Completed, "status " + it.Status + " " + it.Error);
    Check(HashFile(it.SavePath) == Hash(Big), "hash mismatch");
    Check(Volatile.Read(ref maxConc) >= 2, $"expected parallel connections, saw max {maxConc}");
    Check(!File.Exists(it.SavePath + ".mcdm-part") && !File.Exists(it.SavePath + ".mcdm"), "leftover temp files");
    Check(it.SupportsRanges, "ranges not detected");
});

await Test("server without Range support falls back to one stream", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var it = m.Add(new DownloadRequest { Url = Base + "/norange", FileName = "n.bin" });
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 30, "completion");
    Check(it.Status == DownloadStatus.Completed, it.Status + " " + it.Error);
    Check(HashFile(it.SavePath) == Hash(Small), "hash mismatch");
    Check(!it.SupportsRanges, "should report no range support");
});

await Test("unknown Content-Length (chunked) works", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var it = m.Add(new DownloadRequest { Url = Base + "/unknown", FileName = "u.bin" });
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 30, "completion");
    Check(it.Status == DownloadStatus.Completed, it.Status + " " + it.Error);
    Check(HashFile(it.SavePath) == Hash(Small), "hash mismatch");
    Check(it.TotalBytes == Small.Length, "total not fixed up after completion");
});

await Test("429 throttling is survived (pool shrinks, retries)", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var it = m.Add(new DownloadRequest { Url = Base + "/flaky", FileName = "f.bin" });
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 60, "completion");
    Check(it.Status == DownloadStatus.Completed, it.Status + " " + it.Error);
    Check(HashFile(it.SavePath) == Hash(Big), "hash mismatch");
});

await Test("pause then resume continues instead of restarting", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    Interlocked.Exchange(ref slowBytesSent, 0);
    m.DefaultConnections = 4;
    var it = m.Add(new DownloadRequest { Url = Base + "/slow", FileName = "s.bin" });
    await WaitFor(() => it.DownloadedBytes > 600_000, 20, "some progress");
    Check(m.Pause(it.Id), "pause refused");
    await WaitFor(() => m.ActiveCount == 0 && it.ActiveConnections == 0, 10, "engine to stop");
    await Task.Delay(300);
    Check(it.Status == DownloadStatus.Paused, "status " + it.Status);
    Check(File.Exists(it.SavePath + ".mcdm"), "resume state missing");
    Check(File.Exists(it.SavePath + ".mcdm-part"), "partial file missing");
    long atPause = it.DownloadedBytes;
    Check(atPause < Slow.Length, "already finished before pause");
    Check(m.Resume(it.Id), "resume refused");
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 40, "completion");
    Check(it.Status == DownloadStatus.Completed, it.Status + " " + it.Error);
    Check(HashFile(it.SavePath) == Hash(Slow), "hash mismatch after resume");
    long sent = Interlocked.Read(ref slowBytesSent);
    Check(sent < Slow.Length * 1.20, $"re-downloaded too much: sent {sent} of {Slow.Length}");
});

await Test("download survives app restart (state reloaded from disk)", async () =>
{
    var root = Tmp();
    string id;
    long before;
    using (var m = NewManager(root))
    {
        m.DefaultConnections = 4;
        var it = m.Add(new DownloadRequest { Url = Base + "/slow", FileName = "r.bin" });
        await WaitFor(() => it.DownloadedBytes > 600_000, 20, "some progress");
        id = it.Id;
        m.Dispose(); // simulates quitting the app mid-download
        await Task.Delay(500);
        before = it.DownloadedBytes;
    }
    using var m2 = NewManager(root);
    var again = m2.Find(id);
    Check(again != null, "item not restored");
    Check(again!.Status == DownloadStatus.Paused, "restored status " + again.Status);
    Check(m2.Resume(id), "resume refused");
    await WaitFor(() => again.Status is DownloadStatus.Completed or DownloadStatus.Failed, 40, "completion");
    Check(again.Status == DownloadStatus.Completed, again.Status + " " + again.Error);
    Check(HashFile(again.SavePath) == Hash(Slow), "hash mismatch after restart");
});

await Test("cookies are sent same-host but dropped on cross-host redirect", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var direct = m.Add(new DownloadRequest { Url = Base + "/echo-cookie", FileName = "direct.txt", CookieHeader = "secret=1" });
    var redir = m.Add(new DownloadRequest { Url = Base + "/redir", FileName = "redir.txt", CookieHeader = "secret=1" });
    await WaitFor(() => direct.Status == DownloadStatus.Completed && redir.Status == DownloadStatus.Completed, 20, "both");
    Check(File.ReadAllText(direct.SavePath) == "cookie=secret=1", "same-host cookie missing: " + File.ReadAllText(direct.SavePath));
    Check(File.ReadAllText(redir.SavePath) == "cookie=", "cookie leaked cross-host: " + File.ReadAllText(redir.SavePath));
});

await Test("file names: Content-Disposition, uniqueness, path traversal", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var a = m.Add(new DownloadRequest { Url = Base + "/named" });
    await WaitFor(() => a.Status == DownloadStatus.Completed, 20, "a");
    var b = m.Add(new DownloadRequest { Url = Base + "/named" });
    await WaitFor(() => b.Status == DownloadStatus.Completed, 20, "b");
    var c = m.Add(new DownloadRequest { Url = Base + "/named", FileName = "..\\..\\evil.txt" });
    await WaitFor(() => c.Status == DownloadStatus.Completed, 20, "c");
    Check(Path.GetFileName(a.SavePath) == "report.bin", "a: " + a.SavePath);
    Check(Path.GetFileName(b.SavePath) == "report (1).bin", "b: " + b.SavePath);
    Check(Path.GetFileName(c.SavePath) == "evil.txt" && Path.GetDirectoryName(c.SavePath) == Path.Combine(root, "dl"), "c escaped: " + c.SavePath);
    Check(FileNames.Sanitize("CON.txt") == "_CON.txt", "reserved name not handled");
});

await Test("bridge: origin/host enforcement and API contract", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    await using var bridge = new BridgeServer(m);
    await bridge.StartAsync(0);
    var bb = $"http://127.0.0.1:{bridge.Port}";
    using var http = new HttpClient();

    HttpRequestMessage Req(HttpMethod method, string path, string? originHeader = "chrome-extension://abcdef", string? host = null, object? body = null)
    {
        var r = new HttpRequestMessage(method, bb + path);
        if (originHeader != null) r.Headers.TryAddWithoutValidation("Origin", originHeader);
        if (host != null) r.Headers.Host = host;
        if (body != null) r.Content = JsonContent.Create(body);
        return r;
    }

    Check((await http.SendAsync(Req(HttpMethod.Get, "/ping", null))).StatusCode == HttpStatusCode.Forbidden, "no Origin must be rejected");
    Check((await http.SendAsync(Req(HttpMethod.Get, "/ping", "https://evil.example"))).StatusCode == HttpStatusCode.Forbidden, "web origin must be rejected");
    Check((await http.SendAsync(Req(HttpMethod.Get, "/ping", "null"))).StatusCode == HttpStatusCode.Forbidden, "null origin must be rejected");
    Check((await http.SendAsync(Req(HttpMethod.Get, "/ping", host: "evil.example"))).StatusCode == HttpStatusCode.Forbidden, "non-loopback Host must be rejected");

    var ping = await http.SendAsync(Req(HttpMethod.Get, "/ping"));
    Check(ping.StatusCode == HttpStatusCode.OK, "ping failed " + ping.StatusCode);
    Check(ping.Headers.GetValues("Access-Control-Allow-Origin").Single() == "chrome-extension://abcdef", "CORS header missing");
    Check((await ping.Content.ReadAsStringAsync()).Contains("\"platform\":\"windows\""), "ping body");

    var pre = await http.SendAsync(Req(HttpMethod.Options, "/download"));
    Check(pre.StatusCode == HttpStatusCode.NoContent, "preflight " + pre.StatusCode);

    Check((await http.SendAsync(Req(HttpMethod.Post, "/download", body: new { url = "file:///C:/Windows/win.ini" }))).StatusCode == HttpStatusCode.BadRequest, "file:// must be rejected");
    Check((await http.SendAsync(Req(HttpMethod.Post, "/download", body: new { url = Base + "/file", kind = "video" }))).StatusCode == HttpStatusCode.NotImplemented, "video should be 501 for now");
    Check((await http.SendAsync(Req(HttpMethod.Post, "/action", body: new { id = "nope", action = "pause" }))).StatusCode == HttpStatusCode.BadRequest, "unknown id");

    var dl = await http.SendAsync(Req(HttpMethod.Post, "/download", body: new { url = Base + "/file", filename = "viaBridge.bin", kind = "file", segments = 6 }));
    Check(dl.StatusCode == HttpStatusCode.OK, "download " + dl.StatusCode);
    var id = JsonDocument.Parse(await dl.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    string status = "";
    await WaitFor(() =>
    {
        var s = http.SendAsync(Req(HttpMethod.Get, "/downloads")).Result.Content.ReadAsStringAsync().Result;
        var el = JsonDocument.Parse(s).RootElement.EnumerateArray().First(e => e.GetProperty("id").GetString() == id);
        status = el.GetProperty("status").GetString()!;
        return status is "completed" or "failed";
    }, 30, "bridge download");
    Check(status == "completed", "status " + status);
    Check(HashFile(Path.Combine(root, "dl", "viaBridge.bin")) == Hash(Big), "bridge download hash mismatch");
});

Console.WriteLine($"\n{passed} passed, {failed} failed");
await origin.StopAsync();
return failed == 0 ? 0 : 1;
