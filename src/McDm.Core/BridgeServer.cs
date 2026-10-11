using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McDm.Core;

/// <summary>Callbacks that let the host application feed live data into the bridge answers.</summary>
public sealed class BridgeHooks
{
    public Func<IReadOnlyList<string>> CaptureExtensions { get; set; } = () => AppSettings.DefaultCaptureExtensions;
    public Func<int> MinCaptureSizeMB { get; set; } = () => 0;
    public Func<bool> YtDlpAvailable { get; set; } = () => false;
}

/// <summary>Raised whenever a browser extension reaches the bridge.</summary>
public sealed record ExtensionSeenEventArgs(string Browser, bool Paired);

/// <summary>
/// Loopback HTTP bridge used by the browser extension (same endpoints as the macOS app: /ping, /download,
/// /downloads, /action) plus a pairing token. Security rules, in order:
///  1. Host must be a loopback name (blocks DNS rebinding).
///  2. Origin, when present, must be an extension origin (chrome-/moz-/safari-web-extension://); "null" and
///     web origins are rejected. Chromium omits Origin on some extension GETs, so a request without Origin is
///     accepted only if it carries the X-MCDM-Extension header (web pages cannot add it without a preflight,
///     and preflights are only answered for extension origins).
///  3. Everything except OPTIONS and an unauthenticated /ping needs the X-MCDM-Token header.
/// </summary>
public sealed class BridgeServer : IAsyncDisposable
{
    public const int DefaultPort = 48213;
    public const string TokenHeader = "X-MCDM-Token";
    public const string ExtensionHeader = "X-MCDM-Extension";
    private const int MaxUrlLength = 8192;

    private static readonly string[] AllowedOriginPrefixes =
        { "chrome-extension://", "moz-extension://", "safari-web-extension://" };
    private static readonly HashSet<string> KnownBrowsers = new() { "chrome", "edge", "brave", "firefox", "safari" };

    private readonly DownloadManager _manager;
    private readonly TokenStore _tokens;
    private readonly BridgeHooks _hooks;
    private WebApplication? _app;

    public int Port { get; private set; }
    public DateTime? LastExtensionSeenUtc { get; private set; }
    public string? LastExtensionBrowser { get; private set; }
    public bool LastExtensionPaired { get; private set; }
    public event Action<ExtensionSeenEventArgs>? ExtensionSeen;

    public static string Version =>
        typeof(BridgeServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public BridgeServer(DownloadManager manager, TokenStore tokens, BridgeHooks? hooks = null)
    {
        _manager = manager;
        _tokens = tokens;
        _hooks = hooks ?? new BridgeHooks();
    }

    public async Task StartAsync(int port = DefaultPort)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();   // never log URLs, cookies or tokens
        builder.WebHost.UseKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, port);
            o.AddServerHeader = false;
            o.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
        });
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var h = ctx.Request.Headers;
            string host = ctx.Request.Host.Host;
            bool loopbackHost = host is "127.0.0.1" or "localhost" or "[::1]" or "::1";
            string? origin = h.Origin.FirstOrDefault();
            bool isOptions = HttpMethods.IsOptions(ctx.Request.Method);

            bool originOk;
            if (origin != null)
                originOk = AllowedOriginPrefixes.Any(p => origin.Length > p.Length && origin.StartsWith(p, StringComparison.Ordinal));
            else
                originOk = !isOptions && h.ContainsKey(ExtensionHeader);

            if (!loopbackHost || !originOk)
            {
                await Reject(ctx, StatusCodes.Status403Forbidden, "forbidden");
                return;
            }

            if (origin != null)
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
                ctx.Response.Headers["Vary"] = "Origin";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["Access-Control-Allow-Headers"] =
                    $"Content-Type, {TokenHeader}, {ExtensionHeader}, X-MCDM-Extension-Id";
                ctx.Response.Headers["Access-Control-Max-Age"] = "600";
            }
            if (isOptions)
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            bool paired = _tokens.Matches(h[TokenHeader].FirstOrDefault());
            ctx.Items["paired"] = paired;
            bool isPing = ctx.Request.Path.Equals("/ping", StringComparison.Ordinal);
            if (isPing) NoteExtension(ctx.Request.Query["browser"].FirstOrDefault(), paired);
            if (!paired && !isPing)
            {
                await Reject(ctx, StatusCodes.Status401Unauthorized, "unauthorized: pair the extension with the app first");
                return;
            }
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await next();
        });

        app.MapGet("/ping", (HttpContext ctx) =>
        {
            if (ctx.Items["paired"] is not true)
                return Results.Json(new { name = "MC Download Manager", platform = "windows", version = Version, paired = false });
            return Results.Json(new
            {
                name = "MC Download Manager",
                platform = "windows",
                version = Version,
                paired = true,
                ytDlpAvailable = _hooks.YtDlpAvailable(),
                active = _manager.ActiveCount,
                captureExtensions = _hooks.CaptureExtensions(),
                minSizeMB = _hooks.MinCaptureSizeMB(),
            });
        });

        app.MapGet("/downloads", () => Results.Json(new { items = _manager.Snapshot(50).Select(ToDto) }));

        app.MapPost("/download", (DownloadRequest? req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Url))
                return Results.Json(new { ok = false, error = "url required" }, statusCode: 400);
            if (req.Url.Length > MaxUrlLength)
                return Results.Json(new { ok = false, error = "url too long" }, statusCode: 400);
            try
            {
                if (req.Kind == DownloadKind.Video && !_hooks.YtDlpAvailable())
                    return Results.Json(new { ok = false, error = "yt-dlp is not available" }, statusCode: 501);
                var item = _manager.Add(req);
                return Results.Json(new { ok = true, id = item.Id, status = StateOf(item) });
            }
            catch (ArgumentException ex) { return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400); }
            catch (NotSupportedException ex) { return Results.Json(new { ok = false, error = ex.Message }, statusCode: 501); }
        });

        app.MapPost("/action", (ActionRequest? req) =>
        {
            if (req == null || string.IsNullOrEmpty(req.Id))
                return Results.Json(new { ok = false, error = "id required" }, statusCode: 400);
            bool ok = req.Action?.ToLowerInvariant() switch
            {
                "pause" => _manager.Pause(req.Id),
                "resume" => _manager.Resume(req.Id),
                "cancel" => _manager.Cancel(req.Id),
                "remove" => _manager.Remove(req.Id),
                _ => false,
            };
            return ok ? Results.Json(new { ok = true })
                      : Results.Json(new { ok = false, error = "unknown id/action or invalid state" }, statusCode: 400);
        });

        await app.StartAsync();
        var addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(addr).Port;
        _app = app;
    }

    private static Task Reject(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, error = message }));
    }

    private void NoteExtension(string? browser, bool paired)
    {
        browser = browser?.ToLowerInvariant();
        if (browser == null || !KnownBrowsers.Contains(browser)) browser = "unknown";
        LastExtensionSeenUtc = DateTime.UtcNow;
        LastExtensionBrowser = browser;
        LastExtensionPaired = paired;
        try { ExtensionSeen?.Invoke(new ExtensionSeenEventArgs(browser, paired)); } catch { }
    }

    /// <summary>The state vocabulary the extension's popup understands.</summary>
    internal static string StateOf(DownloadItem i) => i.Status switch
    {
        DownloadStatus.Downloading when i.TotalBytes < 0 && i.DownloadedBytes == 0 => "probing",
        DownloadStatus.Downloading => "downloading",
        DownloadStatus.Queued => "queued",
        DownloadStatus.Paused => "paused",
        DownloadStatus.Completed => "completed",
        DownloadStatus.Failed => "failed",
        _ => "cancelled",
    };

    private static object ToDto(DownloadItem i) => new
    {
        id = i.Id,
        name = string.IsNullOrEmpty(i.FileName) ? SafeHostName(i.Request.Url) : i.FileName,
        fileName = i.FileName,
        state = StateOf(i),
        total = i.TotalBytes,
        received = i.DownloadedBytes,
        speed = i.Speed,
        stage = i.Stage,
        error = i.Error,
    };

    private static string SafeHostName(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "download";

    private sealed record ActionRequest(string Id, string? Action);

    public async ValueTask DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); _app = null; }
    }
}
