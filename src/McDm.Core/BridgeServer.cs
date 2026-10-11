using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McDm.Core;

/// <summary>
/// Loopback HTTP bridge used by the browser extension (same contract as the macOS app:
/// /ping, /download, /downloads, /action). Only extension origins and loopback Host headers are accepted.
/// </summary>
public sealed class BridgeServer : IAsyncDisposable
{
    public const int DefaultPort = 48213;
    private static readonly string[] AllowedOriginPrefixes =
        { "chrome-extension://", "moz-extension://", "safari-web-extension://" };

    private readonly DownloadManager _manager;
    private WebApplication? _app;
    public int Port { get; private set; }

    public BridgeServer(DownloadManager manager) => _manager = manager;

    public async Task StartAsync(int port = DefaultPort)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, port));
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var host = ctx.Request.Host.Host;
            bool loopbackHost = host is "127.0.0.1" or "localhost" or "[::1]" or "::1";
            string? origin = ctx.Request.Headers.Origin.FirstOrDefault();
            bool extensionOrigin = origin != null && AllowedOriginPrefixes.Any(p => origin.StartsWith(p, StringComparison.Ordinal));

            if (!loopbackHost || !extensionOrigin)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("forbidden");
                return;
            }
            ctx.Response.Headers["Access-Control-Allow-Origin"] = origin!;
            ctx.Response.Headers["Vary"] = "Origin";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next();
        });

        app.MapGet("/ping", () => Results.Json(new
        {
            name = "MC Download Manager",
            platform = "windows",
            version = typeof(BridgeServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            ytDlpAvailable = false,
            active = _manager.ActiveCount,
        }));

        app.MapGet("/downloads", () => Results.Json(_manager.Snapshot(50).Select(ToDto)));

        app.MapPost("/download", (DownloadRequest? req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Url)) return Results.BadRequest(new { error = "url required" });
            try
            {
                var item = _manager.Add(req);
                return Results.Json(new { id = item.Id, status = item.Status });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return Results.Json(new { error = ex.Message }, statusCode: 501); }
        });

        app.MapPost("/action", (ActionRequest? req) =>
        {
            if (req == null || string.IsNullOrEmpty(req.Id)) return Results.BadRequest(new { error = "id required" });
            bool ok = req.Action?.ToLowerInvariant() switch
            {
                "pause" => _manager.Pause(req.Id),
                "resume" => _manager.Resume(req.Id),
                "cancel" => _manager.Cancel(req.Id),
                "remove" => _manager.Remove(req.Id),
                _ => false,
            };
            return ok ? Results.Json(new { ok = true }) : Results.BadRequest(new { error = "unknown id/action or invalid state" });
        });

        await app.StartAsync();
        var addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(addr).Port;
        _app = app;
    }

    private static object ToDto(DownloadItem i) => new
    {
        id = i.Id, fileName = i.FileName, url = i.Request.Url, status = i.Status,
        totalBytes = i.TotalBytes, downloadedBytes = i.DownloadedBytes,
        speed = i.Speed, error = i.Error,
    };

    private sealed record ActionRequest(string Id, string? Action);

    public async ValueTask DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
    }
}
