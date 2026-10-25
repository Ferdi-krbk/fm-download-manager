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

if (args.Length == 2 && args[0] == "--antivirus-rescan-probe")
{
    string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "rescan-probe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string file = Path.Combine(directory, "scan-probe.txt");
    File.Copy(args[1], file);
    byte[] before = File.ReadAllBytes(file);
    var item = new DownloadItem { Id = "scan-probe", FileName = "scan-probe.txt", Directory = directory, SavePath = file,
        TotalBytes = before.Length, DownloadedBytes = before.Length, Status = DownloadStatus.Failed,
        ScanResult = "attention", Error = "Scan requires attention (exit code 0)" };
    var options = new JsonSerializerOptions(); options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    File.WriteAllText(Path.Combine(directory, "downloads.json"), JsonSerializer.Serialize(new[] { item }, options));
    await using var manager = new DownloadManager(directory, directory);
    await manager.ScanExistingAsync("scan-probe");
    var result = manager.Items.Single();
    bool unchanged = before.SequenceEqual(File.ReadAllBytes(file));
    Console.WriteLine(JsonSerializer.Serialize(new { result.ScanResult, Status = result.Status.ToString(), BytesUnchanged = unchanged }));
    return result.ScanResult == "passed" && result.Status == DownloadStatus.Completed && unchanged ? 0 : 1;
}
if (args.Length == 1 && args[0] == "--antivirus-probe")
{
    Console.WriteLine(JsonSerializer.Serialize(new { Products = AntivirusDetection.InstalledProductNames(), Route = AntivirusDetection.Detect()?.Kind }));
    return 0;
}
if (args.Length == 2 && args[0] == "--antivirus-file") return WindowsAntivirus.RunWorker(args[1]);

// ───────────── test origin server ─────────────
if (Environment.GetEnvironmentVariable("MCDM_TEST_UI_CULTURE") is { Length: > 0 } testCulture)
    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(testCulture);
if (args.Length >= 2 && args[0] == "--scanner-fixture")
{
    if (args[1] == "wait")
    {
        File.WriteAllText(args[2], Environment.ProcessId.ToString());
        await Task.Delay(Timeout.Infinite);
    }
    string output = new('x', 8192);
    for (int n = 0; n < 256; n++) { Console.Out.Write(output); Console.Error.Write(output); }
    return 0;
}
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

// Pace the fixture so a fast CI loopback cannot finish the entire file before
// the downloader's staggered worker startup. Count only real transfer requests.
origin.MapGet("/file", ctx => Serve(ctx, Big, ranges: true, delayMs: 2, track: true));
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

    bool trackedTransfer = track && !(start == 0 && end == 0);
    int now = trackedTransfer ? Interlocked.Increment(ref cur) : 0;
    if (trackedTransfer) { int m; while (now > (m = Volatile.Read(ref maxConc))) Interlocked.CompareExchange(ref maxConc, now, m); }
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
    finally { if (trackedTransfer) Interlocked.Decrement(ref cur); }
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
    var it = m.Add(new DownloadRequest { Url = Base + "/file", FileName = "big.bin", Segments = 4 });
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

// ───────────── bridge helpers ─────────────
async Task<(BridgeServer bridge, string token, string bb, HttpClient http)> StartBridge(DownloadManager m, BridgeHooks? hooks = null)
{
    var tokens = new TokenStore(Path.Combine(Tmp(), "bridge.token"));
    var bridge = new BridgeServer(m, tokens, hooks);
    await bridge.StartAsync(0);
    return (bridge, tokens.Token, $"http://127.0.0.1:{bridge.Port}", new HttpClient());
}

HttpRequestMessage BReq(string bb, HttpMethod method, string path, string? token, string? originHeader = "chrome-extension://abcdef",
                        string? host = null, object? body = null, bool extHeader = false)
{
    var r = new HttpRequestMessage(method, bb + path);
    if (originHeader != null) r.Headers.TryAddWithoutValidation("Origin", originHeader);
    if (token != null) r.Headers.TryAddWithoutValidation("X-MCDM-Token", token);
    if (extHeader) r.Headers.TryAddWithoutValidation("X-MCDM-Extension", "chrome");
    if (host != null) r.Headers.Host = host;
    if (body != null) r.Content = JsonContent.Create(body);
    return r;
}

await Test("bridge: origin/host enforcement and API contract", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var (bridge, tok, bb, http) = await StartBridge(m);
    await using var _b = bridge; using var _h = http;
    HttpRequestMessage Req(HttpMethod method, string path, string? originHeader = "chrome-extension://abcdef", string? host = null, object? body = null)
        => BReq(bb, method, path, tok, originHeader, host, body);

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
    Check((await http.SendAsync(Req(HttpMethod.Post, "/download", body: new { url = Base + "/file", kind = "video" }))).StatusCode == HttpStatusCode.NotImplemented, "video must be 501 while yt-dlp is unavailable");
    Check((await http.SendAsync(Req(HttpMethod.Post, "/action", body: new { id = "nope", action = "pause" }))).StatusCode == HttpStatusCode.BadRequest, "unknown id");

    var dl = await http.SendAsync(Req(HttpMethod.Post, "/download", body: new { url = Base + "/file", filename = "viaBridge.bin", kind = "file", segments = 6 }));
    Check(dl.StatusCode == HttpStatusCode.OK, "download " + dl.StatusCode);
    var id = JsonDocument.Parse(await dl.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    string status = "";
    await WaitFor(() =>
    {
        var s = http.SendAsync(Req(HttpMethod.Get, "/downloads")).Result.Content.ReadAsStringAsync().Result;
        var el = JsonDocument.Parse(s).RootElement.GetProperty("items").EnumerateArray().First(e => e.GetProperty("id").GetString() == id);
        status = el.GetProperty("state").GetString()!;
        return status is "completed" or "failed";
    }, 30, "bridge download");
    Check(status == "completed", "status " + status);
    Check(HashFile(Path.Combine(root, "dl", "viaBridge.bin")) == Hash(Big), "bridge download hash mismatch");
});

await Test("bridge: pairing token is required (missing/wrong -> 401, OPTIONS exempt, /ping minimal)", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var (bridge, tok, bb, http) = await StartBridge(m, new BridgeHooks { YtDlpAvailable = () => true, MinCaptureSizeMB = () => 5 });
    await using var _b = bridge; using var _h = http;

    foreach (var (method, path) in new[] { (HttpMethod.Get, "/downloads"), (HttpMethod.Post, "/download"), (HttpMethod.Post, "/action") })
    {
        var none = await http.SendAsync(BReq(bb, method, path, null, body: method == HttpMethod.Post ? new { url = Base + "/file" } : null));
        Check(none.StatusCode == HttpStatusCode.Unauthorized, $"{path} without token: {none.StatusCode}");
        var wrong = await http.SendAsync(BReq(bb, method, path, new string('0', 64), body: method == HttpMethod.Post ? new { url = Base + "/file" } : null));
        Check(wrong.StatusCode == HttpStatusCode.Unauthorized, $"{path} with wrong token: {wrong.StatusCode}");
    }
    Check(m.Snapshot().Count == 0, "unauthenticated /download must not create a download");

    var pre = await http.SendAsync(BReq(bb, HttpMethod.Options, "/download", null));
    Check(pre.StatusCode == HttpStatusCode.NoContent, "OPTIONS must not need a token");
    var allowed = string.Join(",", pre.Headers.GetValues("Access-Control-Allow-Headers"));
    foreach (var h in new[] { "X-MCDM-Token", "X-MCDM-Extension", "X-MCDM-Extension-Id", "Content-Type" })
        Check(allowed.Contains(h, StringComparison.OrdinalIgnoreCase), "preflight does not allow " + h);

    var anon = JsonDocument.Parse(await (await http.SendAsync(BReq(bb, HttpMethod.Get, "/ping", null))).Content.ReadAsStringAsync()).RootElement;
    var names = anon.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
    Check(string.Join(",", names) == "name,paired,platform,version", "anonymous /ping must expose only name/platform/version/paired, got " + string.Join(",", names));
    Check(!anon.GetProperty("paired").GetBoolean(), "anonymous ping must say paired:false");

    var full = JsonDocument.Parse(await (await http.SendAsync(BReq(bb, HttpMethod.Get, "/ping", tok))).Content.ReadAsStringAsync()).RootElement;
    Check(full.GetProperty("paired").GetBoolean(), "paired ping must say paired:true");
    Check(full.GetProperty("ytDlpAvailable").GetBoolean(), "ytDlpAvailable should follow the hook");
    Check(full.GetProperty("captureExtensions").GetArrayLength() > 10, "captureExtensions missing");
    Check(full.GetProperty("minSizeMB").GetInt32() == 5 && full.TryGetProperty("active", out _) && full.TryGetProperty("version", out _), "ping fields");

    // a wrong token on /ping degrades to the anonymous answer, never to an error that leaks anything
    var bad = JsonDocument.Parse(await (await http.SendAsync(BReq(bb, HttpMethod.Get, "/ping", "deadbeef"))).Content.ReadAsStringAsync()).RootElement;
    Check(!bad.GetProperty("paired").GetBoolean(), "wrong token must not be paired");
});

await Test("bridge: rejects web pages, Origin null, odd hosts and non-http(s) schemes even with a valid token", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var (bridge, tok, bb, http) = await StartBridge(m);
    await using var _b = bridge; using var _h = http;

    foreach (var origin in new[] { "null", "https://evil.example", "http://127.0.0.1:48213", "http://localhost", "file://", "chrome-extension://", "ftp://x" })
    {
        var r = await http.SendAsync(BReq(bb, HttpMethod.Post, "/download", tok, origin, body: new { url = Base + "/file" }));
        Check(r.StatusCode == HttpStatusCode.Forbidden, $"Origin '{origin}' got {r.StatusCode}");
        var p = await http.SendAsync(BReq(bb, HttpMethod.Options, "/download", null, origin));
        Check(p.StatusCode == HttpStatusCode.Forbidden, $"preflight for '{origin}' got {p.StatusCode}");
    }
    // no Origin and no extension marker (e.g. a plain form post / curl) is refused
    Check((await http.SendAsync(BReq(bb, HttpMethod.Post, "/download", tok, null, body: new { url = Base + "/file" }))).StatusCode == HttpStatusCode.Forbidden, "no Origin, no marker");
    Check((await http.SendAsync(BReq(bb, HttpMethod.Options, "/download", tok, null, extHeader: true))).StatusCode == HttpStatusCode.Forbidden, "preflight without Origin");
    foreach (var host in new[] { "evil.example", "127.0.0.1.evil.example", "192.168.1.5", "localhost.evil.test:48213" })
        Check((await http.SendAsync(BReq(bb, HttpMethod.Get, "/ping", tok, host: host))).StatusCode == HttpStatusCode.Forbidden, "Host " + host);

    foreach (var url in new[] { "file:///C:/Windows/win.ini", "ftp://example.com/a.bin", "javascript:alert(1)", "data:text/plain,hi", "\\\\server\\share\\a.bin", "C:\\Windows\\win.ini", "//example.com/a" })
    {
        var r = await http.SendAsync(BReq(bb, HttpMethod.Post, "/download", tok, body: new { url }));
        Check(r.StatusCode == HttpStatusCode.BadRequest, $"url '{url}' got {r.StatusCode}");
    }
    Check((await http.SendAsync(BReq(bb, HttpMethod.Post, "/download", tok, body: new { url = "http://example.com/" + new string('a', 9000) }))).StatusCode == HttpStatusCode.BadRequest, "overlong url");
    Check(m.Snapshot().Count == 0, "no download may be created by rejected requests");
});

await Test("bridge: replays the exact requests the extension sends (Chrome GET without Origin, POST with Origin)", async () =>
{
    var root = Tmp(); using var m = NewManager(root);
    var (bridge, tok, bb, http) = await StartBridge(m);
    await using var _b = bridge; using var _h = http;
    string? seenBrowser = null; bool? seenPaired = null;
    bridge.ExtensionSeen += e => { seenBrowser = e.Browser; seenPaired = e.Paired; };

    // GET /ping?browser=chrome — headers exactly as background.js sets them, no Origin (Chrome drops it for host-permission GETs)
    var ping = new HttpRequestMessage(HttpMethod.Get, bb + "/ping?browser=chrome");
    ping.Headers.TryAddWithoutValidation("X-MCDM-Extension", "chrome");
    ping.Headers.TryAddWithoutValidation("X-MCDM-Extension-Id", "abcdefghijklmnopabcdefghijklmnop");
    var pr = await http.SendAsync(ping);
    Check(pr.StatusCode == HttpStatusCode.OK, "ping w/o Origin " + pr.StatusCode);
    Check(seenBrowser == "chrome" && seenPaired == false, $"wizard hook: {seenBrowser}/{seenPaired}");
    ping = new HttpRequestMessage(HttpMethod.Get, bb + "/ping?browser=edge");
    ping.Headers.TryAddWithoutValidation("X-MCDM-Extension", "edge");
    ping.Headers.TryAddWithoutValidation("X-MCDM-Token", tok);
    await http.SendAsync(ping);
    Check(seenBrowser == "edge" && seenPaired == true && bridge.LastExtensionPaired, "paired ping must be reported");
    ping = new HttpRequestMessage(HttpMethod.Get, bb + "/ping?browser=<script>");
    ping.Headers.TryAddWithoutValidation("X-MCDM-Extension", "x");
    await http.SendAsync(ping);
    Check(seenBrowser == "unknown", "browser name must be whitelisted");

    // preflight for the POST, as Chrome would send it
    var pre = new HttpRequestMessage(HttpMethod.Options, bb + "/download");
    pre.Headers.TryAddWithoutValidation("Origin", "chrome-extension://abcdefghijklmnopabcdefghijklmnop");
    pre.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
    pre.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type,x-mcdm-extension,x-mcdm-extension-id,x-mcdm-token");
    var prr = await http.SendAsync(pre);
    Check(prr.StatusCode == HttpStatusCode.NoContent, "preflight " + prr.StatusCode);
    Check(prr.Headers.GetValues("Access-Control-Allow-Methods").Single().Contains("POST"), "allow methods");

    // POST /download with the payload shape of sendToApp()
    const string payload = """
    {"url":"__U__/file","filename":"contract.bin","referer":"https://example.test/page","kind":"file",
     "userAgent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130","cookieHeader":"sid=abc123; pref=x",
     "cookies":[{"name":"sid","value":"abc123","domain":"127.0.0.1","path":"/","secure":false,"expirationDate":1900000000},
                {"name":"pref","value":"x","domain":"127.0.0.1","path":"/","secure":false,"expirationDate":0}]}
    """;
    var post = new HttpRequestMessage(HttpMethod.Post, bb + "/download")
    { Content = new StringContent(payload.Replace("__U__", Base), Encoding.UTF8, "application/json") };
    post.Headers.TryAddWithoutValidation("Origin", "chrome-extension://abcdefghijklmnopabcdefghijklmnop");
    post.Headers.TryAddWithoutValidation("X-MCDM-Extension", "chrome");
    post.Headers.TryAddWithoutValidation("X-MCDM-Extension-Id", "abcdefghijklmnopabcdefghijklmnop");
    post.Headers.TryAddWithoutValidation("X-MCDM-Token", tok);
    var resp = await http.SendAsync(post);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    Check(resp.StatusCode == HttpStatusCode.OK && body.GetProperty("ok").GetBoolean(), "extension requires {ok:true}: " + body);
    var item = m.Find(body.GetProperty("id").GetString()!)!;
    Check(item.Request.Referer == "https://example.test/page" && item.Request.UserAgent!.Contains("Chrome/130") && item.Request.CookieHeader == "sid=abc123; pref=x", "request fields not carried over");
    Check(item.Request.Cookies?.Count == 2 && item.Request.Cookies[0].ExpirationDate == 1900000000, "cookie list not carried over");

    var list = JsonDocument.Parse(await (await http.SendAsync(BReq(bb, HttpMethod.Get, "/downloads", tok))).Content.ReadAsStringAsync()).RootElement.GetProperty("items");
    var row = list.EnumerateArray().First();
    foreach (var f in new[] { "id", "name", "state", "total", "received", "speed" }) Check(row.TryGetProperty(f, out _), "downloads item lacks '" + f + "' (popup.js reads it)");
    Check(row.GetProperty("state").GetString() is "probing" or "downloading" or "completed" or "queued", "state vocabulary: " + row.GetProperty("state"));
    await WaitFor(() => item.Status == DownloadStatus.Completed, 30, "contract download");

    var act = await http.SendAsync(BReq(bb, HttpMethod.Post, "/action", tok, body: new { id = item.Id, action = "remove" }));
    Check(act.StatusCode == HttpStatusCode.OK && (await act.Content.ReadAsStringAsync()).Contains("\"ok\":true"), "remove action");
});

string? FindRepoFile(string rel)
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, rel)) || Directory.Exists(Path.Combine(d.FullName, rel))) return Path.Combine(d.FullName, rel);
    return null;
}

await Test("extension: real background.js intercepts, cancels browser download and hands it over (needs node)", async () =>
{
    var harness = FindRepoFile(Path.Combine("tests", "extension-harness", "harness.js"));
    var extDir = FindRepoFile("extension");
    Check(harness != null && extDir != null, "harness or extension folder not found");
    string? node = null;
    foreach (var cand in new[] { "node", "node.exe", @"C:\Program Files\nodejs\node.exe", "/usr/bin/node", "/usr/local/bin/node" })
    {
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cand, "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
            probe.WaitForExit(5000); if (probe.ExitCode == 0) { node = cand; break; }
        }
        catch { }
    }
    if (node == null) { Console.WriteLine("      (node not found: harness skipped)"); return; }

    var root = Tmp(); using var m = NewManager(root);
    var (bridge, tok, bb, http) = await StartBridge(m);
    await using var _b = bridge; using var _h = http;

    var psi = new System.Diagnostics.ProcessStartInfo(node) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    psi.ArgumentList.Add(harness!); psi.ArgumentList.Add(bridge.Port.ToString()); psi.ArgumentList.Add(tok); psi.ArgumentList.Add(extDir!); psi.ArgumentList.Add(Base + "/file");
    using var proc = System.Diagnostics.Process.Start(psi)!;
    var outTask = proc.StandardOutput.ReadToEndAsync(); var errTask = proc.StandardError.ReadToEndAsync();
    if (!proc.WaitForExit(60000)) { proc.Kill(true); throw new Exception("harness timed out"); }
    var stdout = await outTask;
    var line = stdout.Split('\n').FirstOrDefault(l => l.StartsWith("REPORT:"));
    Check(line != null, "no report from harness: " + stdout + await errTask);
    var rep = JsonDocument.Parse(line!["REPORT:".Length..]).RootElement;

    var st0 = rep.GetProperty("statusUnpaired");
    Check(st0.GetProperty("reachable").GetBoolean() && !st0.GetProperty("paired").GetBoolean() && !st0.GetProperty("online").GetBoolean(), "unpaired status: " + st0);
    Check(!rep.GetProperty("interceptUnpaired").GetProperty("taken").GetBoolean() && rep.GetProperty("interceptUnpaired").GetProperty("suggested").GetBoolean(),
          "an unpaired extension must leave the download to the browser");
    Check(!rep.GetProperty("pairBad").GetProperty("ok").GetBoolean() && !rep.GetProperty("pairWrong").GetProperty("ok").GetBoolean(), "bad codes must not pair");
    Check(rep.GetProperty("pairGood").GetProperty("ok").GetBoolean(), "good code must pair: " + rep.GetProperty("pairGood"));
    Check(rep.GetProperty("statusPaired").GetProperty("online").GetBoolean(), "paired status must be online");
    var took = rep.GetProperty("interceptPaired");
    Check(took.GetProperty("taken").GetBoolean() && !took.GetProperty("suggested").GetBoolean(), "paired extension must take the download: " + took);
    Check(rep.GetProperty("cancel").GetArrayLength() == 1 && rep.GetProperty("erase").GetArrayLength() == 1, "browser download must be cancelled and erased once");

    var items = m.Snapshot();
    Check(items.Count == 1, "expected exactly one download in the app, got " + items.Count);
    var it = items[0];
    Check(it.Request.Url == Base + "/file", "url");
    Check(it.Request.Referer == "https://example.test/page", "referer");
    Check(it.Request.UserAgent!.Contains("Chrome/130"), "user agent");
    Check(it.Request.CookieHeader == "sid=abc123; pref=x" && it.Request.Cookies!.Count == 2, "cookies");
    Check(it.Request.FileName == "contract.bin", "file name must be the basename, got " + it.Request.FileName);
    await WaitFor(() => it.Status is DownloadStatus.Completed or DownloadStatus.Failed, 30, "intercepted download");
    Check(it.Status == DownloadStatus.Completed && HashFile(it.SavePath) == Hash(Big), "intercepted download content");
});

await McDm.Tests.VideoTests.RunAsync(Test, Check, Tmp);
await McDm.Tests.HardeningTests.RunAsync(Test, Check, Tmp);
await McDm.Tests.StressAndBoundaryTests.RunAsync(Test, Check, Tmp);
await McDm.Tests.RepairRegressionTests.RunAsync(Test, Check, Tmp);

Console.WriteLine($"\n{passed} passed, {failed} failed");
await origin.StopAsync();
return failed == 0 ? 0 : 1;
