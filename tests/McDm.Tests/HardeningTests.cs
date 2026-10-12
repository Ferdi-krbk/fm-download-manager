using System.Diagnostics;
using System.Net;
using System.Text;
using McDm.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McDm.Tests;

public static class HardeningTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> Test, Action<bool, string> Check, Func<string> Tmp)
    {
        await Test("hardening: speed limiter throttles download rate", async () =>
        {
            var data = new byte[3 * 1024 * 1024];
            Random.Shared.NextBytes(data);

            var sb = WebApplication.CreateBuilder();
            sb.Logging.ClearProviders();
            sb.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
            var app = sb.Build();
            app.MapGet("/limit-test", async ctx =>
            {
                ctx.Response.Headers.ContentLength = data.Length;
                ctx.Response.Headers.AcceptRanges = "bytes";
                var rh = ctx.Request.Headers.Range.ToString();
                if (rh.StartsWith("bytes="))
                {
                    var seg = rh["bytes=".Length..].Split('-');
                    long start = long.Parse(seg[0]);
                    long end = string.IsNullOrEmpty(seg[1]) ? data.Length - 1 : long.Parse(seg[1]);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{data.Length}";
                    await ctx.Response.Body.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
                }
                else
                {
                    await ctx.Response.Body.WriteAsync(data);
                }
            });
            await app.StartAsync();
            int port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

            var limiter = new SpeedLimiter(1024 * 1024); // 1 MB/s
            var engine = new SegmentedDownloader(options: new EngineOptions { Limiter = limiter, MinSplitBytes = 1024 * 1024 });

            var item = new DownloadItem
            {
                Request = new DownloadRequest { Url = $"http://127.0.0.1:{port}/limit-test" },
                Directory = Tmp(),
            };

            var sw = Stopwatch.StartNew();
            await engine.RunAsync(item, 2, CancellationToken.None);
            sw.Stop();

            await app.StopAsync();

            Check(item.DownloadedBytes == data.Length, "downloaded size matches");
            // 3 MB at 1 MB/s should take at least ~1.5 - 2.0s
            Check(sw.Elapsed.TotalSeconds >= 1.5, $"download should be throttled (took {sw.Elapsed.TotalSeconds:0.##}s)");
        });

        await Test("hardening: queue reordering (MoveUp / MoveDown)", () =>
        {
            var dir = Tmp();
            using var m = new DownloadManager(dir);
            // pause concurrency so items stay queued
            m.MaxConcurrentDownloads = 0;

            var itm1 = m.Add(new DownloadRequest { Url = "http://127.0.0.1/1.bin" });
            var itm2 = m.Add(new DownloadRequest { Url = "http://127.0.0.1/2.bin" });
            var itm3 = m.Add(new DownloadRequest { Url = "http://127.0.0.1/3.bin" });

            // Items.Insert(0, item) means newest is at index 0: itm3, itm2, itm1
            Check(m.Items[0].Id == itm3.Id && m.Items[1].Id == itm2.Id && m.Items[2].Id == itm1.Id, "initial order");

            // Move itm2 up (index 1 -> index 0)
            bool up = m.MoveUp(itm2.Id);
            Check(up, "MoveUp returned true");
            Check(m.Items[0].Id == itm2.Id && m.Items[1].Id == itm3.Id, "itm2 moved to top");

            // Move itm2 down (index 0 -> index 1)
            bool down = m.MoveDown(itm2.Id);
            Check(down, "MoveDown returned true");
            Check(m.Items[0].Id == itm3.Id && m.Items[1].Id == itm2.Id, "itm2 moved down");

            return Task.CompletedTask;
        });

        await Test("hardening: ETag change on resume invalidates state and restarts cleanly", async () =>
        {
            string etag = "\"v1\"";
            byte[] v1 = Encoding.UTF8.GetBytes("Old content v1 - exactly 32 bytes!");
            byte[] v2 = Encoding.UTF8.GetBytes("New content v2 - exactly 32 bytes!");

            var sb = WebApplication.CreateBuilder();
            sb.Logging.ClearProviders();
            sb.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
            var app = sb.Build();
            app.MapGet("/etag-file", async ctx =>
            {
                byte[] current = etag == "\"v1\"" ? v1 : v2;
                ctx.Response.Headers.ETag = etag;
                ctx.Response.Headers.AcceptRanges = "bytes";
                ctx.Response.Headers.ContentLength = current.Length;

                var rh = ctx.Request.Headers.Range.ToString();
                if (rh.StartsWith("bytes="))
                {
                    var seg = rh["bytes=".Length..].Split('-');
                    long start = long.Parse(seg[0]);
                    long end = string.IsNullOrEmpty(seg[1]) ? current.Length - 1 : long.Parse(seg[1]);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{current.Length}";
                    await ctx.Response.Body.WriteAsync(current.AsMemory((int)start, (int)(end - start + 1)));
                }
                else
                {
                    await ctx.Response.Body.WriteAsync(current);
                }
            });
            await app.StartAsync();
            int port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

            var dir = Tmp();
            var engine = new SegmentedDownloader(options: new EngineOptions { MinSplitBytes = 10 });
            var item = new DownloadItem
            {
                Request = new DownloadRequest { Url = $"http://127.0.0.1:{port}/etag-file" },
                Directory = dir,
            };

            // First run: downloads v1
            await engine.RunAsync(item, 1, CancellationToken.None);
            Check(File.ReadAllBytes(item.SavePath).SequenceEqual(v1), "first download matches v1");

            // Simulate partial download state with old ETag
            var part = item.SavePath + ".mcdm-part";
            var state = item.SavePath + ".mcdm";
            File.WriteAllBytes(part, v1[..10]);
            File.WriteAllText(state, $"{{\"Total\":32,\"Validator\":\"\\\"v1\\\"\",\"Ranges\":true,\"Chunks\":[{{\"Start\":0,\"End\":31,\"Done\":10}}]}}");

            // Server changes ETag to v2
            etag = "\"v2\"";

            // Run again on same save path
            var resumeItem = new DownloadItem
            {
                Request = new DownloadRequest { Url = $"http://127.0.0.1:{port}/etag-file" },
                Directory = dir,
                SavePath = item.SavePath,
            };
            await engine.RunAsync(resumeItem, 1, CancellationToken.None);

            await app.StopAsync();

            // Should have discarded old state and cleanly downloaded v2
            Check(File.ReadAllBytes(resumeItem.SavePath).SequenceEqual(v2), "download restarted and matches v2 after ETag change");
        });

        await Test("hardening: URL with spaces, unicode and query strings", async () =>
        {
            var data = Encoding.UTF8.GetBytes("Unicode URL Content 👍 Türkçe karakterler şçöğü");
            var sb = WebApplication.CreateBuilder();
            sb.Logging.ClearProviders();
            sb.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
            var app = sb.Build();
            app.MapGet("/dosya müzik/şarkı.mp3", async ctx =>
            {
                ctx.Response.Headers.ContentLength = data.Length;
                await ctx.Response.Body.WriteAsync(data);
            });
            await app.StartAsync();
            int port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

            var dir = Tmp();
            var engine = new SegmentedDownloader();
            var rawUrl = $"http://127.0.0.1:{port}/dosya%20m%C3%BCzik/%C5%9Fark%C4%B1.mp3?token=xyz#hash";
            var item = new DownloadItem
            {
                Request = new DownloadRequest { Url = rawUrl },
                Directory = dir,
            };

            await engine.RunAsync(item, 1, CancellationToken.None);
            await app.StopAsync();

            Check(item.DownloadedBytes == data.Length, "downloaded size matches unicode file");
            Check(File.ReadAllBytes(item.SavePath).SequenceEqual(data), "file contents match");
            Check(item.FileName.Contains("şarkı") || item.FileName.EndsWith(".mp3"), "filename sanitized and preserved: " + item.FileName);
        });
    }
}
