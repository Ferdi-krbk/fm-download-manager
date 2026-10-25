using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace McDm.Core;

public sealed class EngineOptions
{
    public int MaxRetries { get; set; } = 6;
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int BufferSize { get; set; } = 256 * 1024;
    /// <summary>Files smaller than this are fetched as a single stream.</summary>
    public long MinSplitBytes { get; set; } = 2 * 1024 * 1024;
    public long MinChunkBytes { get; set; } = 1024 * 1024;
    public long MaxChunkBytes { get; set; } = 64L * 1024 * 1024;
    public TimeSpan ThrottleBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public SpeedLimiter? Limiter { get; set; }
}

public class DownloadException : Exception
{
    public DownloadException(string message, Exception? inner = null) : base(message, inner) { }
}
public sealed class FatalDownloadException : DownloadException
{
    public FatalDownloadException(string message, Exception? inner = null) : base(message, inner) { }
}
internal sealed class TransientException : Exception
{
    public TransientException(string message) : base(message) { }
}
internal sealed class ThrottledException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public ThrottledException(TimeSpan? retryAfter) : base("Server is throttling (429/503)") => RetryAfter = retryAfter;
}

internal sealed class ResumeState
{
    public long Total { get; set; }
    public string? Validator { get; set; }
    public bool Ranges { get; set; }
    public List<Chunk> Chunks { get; set; } = new();
}

/// <summary>
/// Multi-connection HTTP downloader. The file is cut into chunks that a pool of workers pulls from a queue
/// (Range requests, pwrite-style writes into one pre-sized file). 429/503 shrinks the worker pool; chunk
/// progress is persisted so downloads resume after pause, crash or restart.
/// </summary>
public sealed class SegmentedDownloader
{
    private static readonly object PathLock = new();
    private readonly HttpClient _http;
    private readonly EngineOptions _opt;

    public SegmentedDownloader(HttpClient? http = null, EngineOptions? options = null)
    {
        _opt = options ?? new EngineOptions();
        _http = http ?? CreateDefaultClient();
    }

    public static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,                    // redirects are followed manually so cookies can be dropped cross-host
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None, // Range + Content-Encoding do not mix
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 64,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // ───────────────────────── public entry point ─────────────────────────

    public async Task RunAsync(DownloadItem item, int maxConnections, CancellationToken ct)
    {
        maxConnections = Math.Clamp(maxConnections, 1, 32);

        var probe = await RetryAsync(() => ProbeAsync(item, ct), ct);
        item.SupportsRanges = probe.Ranges;
        item.TotalBytes = probe.Total;

        ResumeState? state = string.IsNullOrEmpty(item.SavePath) ? null : ReadState(item.SavePath);
        bool canResume = state != null && probe.Ranges && state.Ranges && state.Total == probe.Total
                         && ValidatorsMatch(state.Validator, probe.Validator) && ValidState(state, item.SavePath);
        List<Chunk> chunks;
        if (canResume)
        {
            chunks = state!.Chunks;
        }
        else
        {
            if (string.IsNullOrEmpty(item.SavePath)) AssignSavePath(item, probe.FileName);
            else { TryDelete(PartPath(item.SavePath)); TryDelete(StatePath(item.SavePath)); }
            chunks = PlanChunks(probe.Total, probe.Ranges, maxConnections);
        }
        item.FileName = Path.GetFileName(item.SavePath);

        var part = PartPath(item.SavePath);
        bool completed = false;

        // Disk space check before preallocating
        if (probe.Total > 0)
        {
            try
            {
                var targetDir = Path.GetFullPath(item.Directory);
                var root = Path.GetPathRoot(targetDir);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    long allocated = canResume ? new FileInfo(PartPath(item.SavePath)).Length : 0;
                    if (drive.IsReady && drive.AvailableFreeSpace < Math.Max(0, probe.Total - allocated))
                    {
                        throw new FatalDownloadException(
                            $"Not enough disk space on drive {root}. Required: {probe.Total} bytes, available: {drive.AvailableFreeSpace} bytes");
                    }
                }
            }
            catch (FatalDownloadException) { throw; }
            catch { /* non-fatal if DriveInfo fails on custom UNC / virtual paths */ }
        }

        var handle = File.OpenHandle(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous);
        var ctx = new RunCtx
        {
            Item = item, Url = probe.Url, UseRange = probe.Ranges, Total = probe.Total,
            Validator = probe.Validator, Chunks = chunks, Handle = handle,
        };
        using var stop = new CancellationTokenSource();
        Task? progress = null;
        try
        {
            if (probe.Total > 0 && RandomAccess.GetLength(handle) != probe.Total)
                RandomAccess.SetLength(handle, probe.Total);

            for (int i = 0; i < chunks.Count; i++) if (!chunks[i].IsComplete) ctx.Queue.Enqueue(i);
            int workers = Math.Min(maxConnections, Math.Max(1, ctx.Queue.Count));
            ctx.Active = workers;
            ctx.Target = workers;

            Persist(ctx, flush: false);
            progress = ProgressLoopAsync(ctx, stop.Token);

            using var wcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = Enumerable.Range(0, workers).Select(workerIdx => Task.Run(async () =>
            {
                try
                {
                    if (workerIdx > 0) await Task.Delay(Math.Min(250, workerIdx * 15), wcts.Token);
                    await WorkerAsync(ctx, wcts.Token);
                }
                catch (OperationCanceledException) when (wcts.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref ctx.Failure, ex, null);
                    wcts.Cancel();
                }
            })).ToArray();
            await Task.WhenAll(tasks);

            if (ctx.Failure != null) ExceptionDispatchInfo.Capture(ctx.Failure).Throw();
            ct.ThrowIfCancellationRequested();

            completed = chunks.All(c => c.IsComplete);
            if (!completed) throw new FatalDownloadException("Download ended with missing segments");
        }
        finally
        {
            stop.Cancel();
            if (progress != null) { try { await progress; } catch { } }
            UpdateProgress(ctx, 0);
            if (!completed) Persist(ctx, flush: true);
            handle.Dispose();
        }

        // finished: close handle (done above), move into place with antivirus/lock retry, drop state
        long finalSize = chunks.Sum(c => c.Done);
        item.TotalBytes = finalSize;
        item.DownloadedBytes = finalSize;
        item.ActiveConnections = 0;
        item.Speed = 0;

        await MoveFileWithRetryAsync(part, item);
        DownloadOrigin.MarkFile(item.SavePath);
        TryDelete(StatePath(item.SavePath));
        TryDelete(StatePath(Path.ChangeExtension(part, null) ?? ""));
    }

    private static async Task MoveFileWithRetryAsync(string sourcePart, DownloadItem item)
    {
        // Antivirus scanners or indexers may briefly lock the file after handle disposal.
        // Retry with backoff before falling back to unique name.
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Move(sourcePart, item.SavePath, overwrite: false);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(attempt * 100);
            }
            catch (IOException)
            {
                lock (PathLock)
                {
                    item.SavePath = FileNames.Unique(item.Directory, Path.GetFileName(item.SavePath));
                    File.Move(sourcePart, item.SavePath, overwrite: false);
                }
                item.FileName = Path.GetFileName(item.SavePath);
                return;
            }
        }
    }

    // ───────────────────────── probing ─────────────────────────

    private sealed record Probe(Uri Url, long Total, bool Ranges, string? Validator, string FileName);

    private async Task<Probe> ProbeAsync(DownloadItem item, CancellationToken ct)
    {
        var (resp, url) = await SendAsync(item, new Uri(item.Request.Url), 0, 0, ct);
        using (resp)
        {
            long total;
            bool ranges;
            switch (resp.StatusCode)
            {
                case HttpStatusCode.PartialContent:
                    var len = resp.Content.Headers.ContentRange?.Length;
                    ranges = len.HasValue;
                    total = len ?? -1;
                    break;
                case HttpStatusCode.RequestedRangeNotSatisfiable: // empty file
                    if (resp.Content.Headers.ContentRange?.Length != 0)
                        throw new FatalDownloadException("Server rejected the initial byte range");
                    ranges = false;
                    total = resp.Content.Headers.ContentRange?.Length ?? 0;
                    break;
                case HttpStatusCode.OK:
                    ranges = false;
                    total = resp.Content.Headers.ContentLength ?? -1;
                    break;
                default:
                    ThrowForStatus(resp);
                    throw new InvalidOperationException();
            }

            var cd = resp.Content.Headers.ContentDisposition;
            string? name = (cd?.FileNameStar ?? cd?.FileName)?.Trim('"');
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath)); } catch { name = null; }
            }
            string validator = ResponseValidator(resp) ?? "";
            return new Probe(url, total, ranges, validator.Length == 0 ? null : validator, FileNames.Sanitize(name));
        }
    }

    public static bool ValidatorsMatch(string? v1, string? v2)
    {
        if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2)) return false;
        if (v1.StartsWith("W/", StringComparison.Ordinal) || v2.StartsWith("W/", StringComparison.Ordinal)) return false;
        return string.Equals(v1.Trim(), v2.Trim(), StringComparison.Ordinal);
    }

    private static string? ResponseValidator(HttpResponseMessage response) =>
        response.Headers.ETag is { IsWeak: false } etag ? etag.ToString()
            : response.Content.Headers.LastModified?.ToString("R");

    private static bool ValidState(ResumeState state, string path)
    {
        if (!File.Exists(PartPath(path)) || new FileInfo(PartPath(path)).Length != state.Total || state.Chunks == null)
            return false;
        long next = 0;
        foreach (var chunk in state.Chunks.OrderBy(c => c.Start))
        {
            if (chunk.Start != next || chunk.End < chunk.Start || chunk.End >= state.Total || chunk.Done < 0 || chunk.Done > chunk.Length)
                return false;
            next = chunk.End + 1;
        }
        return next == state.Total;
    }

    // ───────────────────────── chunk planning ─────────────────────────

    private List<Chunk> PlanChunks(long total, bool ranges, int connections)
    {
        if (total == 0) return new();
        if (total < 0) return new() { new Chunk { Start = 0, End = -1 } };
        if (!ranges || total < _opt.MinSplitBytes) return new() { new Chunk { Start = 0, End = total - 1 } };

        long size = (long)Math.Ceiling(total / (connections * 4.0));
        size = Math.Clamp(size, _opt.MinChunkBytes, _opt.MaxChunkBytes);
        var list = new List<Chunk>();
        for (long start = 0; start < total; start += size)
            list.Add(new Chunk { Start = start, End = Math.Min(start + size, total) - 1 });
        return list;
    }

    // ───────────────────────── workers ─────────────────────────

    private sealed class RunCtx
    {
        public required DownloadItem Item;
        public required Uri Url;
        public required bool UseRange;
        public required long Total;
        public required string? Validator;
        public required List<Chunk> Chunks;
        public required SafeFileHandle Handle;
        public readonly object StateLock = new();
        public ConcurrentQueue<int> Queue = new();
        public ConcurrentDictionary<int, int> Throttles = new();
        public int Active;
        public int Target;
        public Exception? Failure;
        private long _lastReduce;
        private readonly object _reduceLock = new();

        public bool TryRetire()
        {
            while (true)
            {
                int a = Volatile.Read(ref Active);
                if (a <= Volatile.Read(ref Target)) return false;
                if (Interlocked.CompareExchange(ref Active, a - 1, a) == a) return true;
            }
        }

        public void ReduceTarget()
        {
            lock (_reduceLock)
            {
                long now = Environment.TickCount64;
                if (now - _lastReduce < 2000) return;
                _lastReduce = now;
                Volatile.Write(ref Target, Math.Max(1, Volatile.Read(ref Target) / 2));
            }
        }
    }

    private async Task WorkerAsync(RunCtx c, CancellationToken ct)
    {
        bool counted = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (c.TryRetire()) { counted = false; return; }
                if (!c.Queue.TryDequeue(out int idx))
                {
                    // Dynamic re-segmentation: steal the trailing half of the largest chunk
                    if (!c.UseRange || !TrySplitLargestChunk(c, out idx))
                        return;
                }
                try
                {
                    await DownloadChunkAsync(c, idx, ct);
                }
                catch (ThrottledException tex)
                {
                    if (c.Throttles.AddOrUpdate(idx, 1, (_, v) => v + 1) > _opt.MaxRetries * 2)
                        throw new FatalDownloadException("Server keeps rejecting requests (429/503)");
                    c.ReduceTarget();
                    c.Queue.Enqueue(idx);
                    await Task.Delay(tex.RetryAfter ?? _opt.ThrottleBackoff, ct);
                }
            }
        }
        finally
        {
            if (counted) Interlocked.Decrement(ref c.Active);
        }
    }

    private static bool TrySplitLargestChunk(RunCtx c, out int newIdx)
    {
        newIdx = -1;
        if (!c.UseRange) return false;

        lock (c.StateLock)
        {
            int bestIdx = -1;
            long maxRemaining = 4 * 1024 * 1024; // At least 4 MB remaining to justify a split

            for (int i = 0; i < c.Chunks.Count; i++)
            {
                var ch = c.Chunks[i];
                if (ch.End < 0) continue;

                long remaining = ch.Length - ch.Done;
                if (remaining > maxRemaining)
                {
                    maxRemaining = remaining;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                var target = c.Chunks[bestIdx];
                long remaining = target.Length - target.Done;
                long half = remaining / 2;
                long splitPoint = target.Start + target.Done + half;

                var newChunk = new Chunk
                {
                    Start = splitPoint,
                    End = target.End,
                    Done = 0
                };

                target.End = splitPoint - 1;
                newIdx = c.Chunks.Count;
                c.Chunks.Add(newChunk);
                return true;
            }
        }

        return false;
    }

    private async Task DownloadChunkAsync(RunCtx c, int idx, CancellationToken ct)
    {
        Chunk chunk;
        lock (c.StateLock) chunk = c.Chunks[idx];
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long before = chunk.Done;
            try
            {
                await TransferAsync(c, chunk, ct);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TransientException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                if (c.UseRange && chunk.Done > before) attempt = 0;
                if (++attempt > _opt.MaxRetries)
                    throw new FatalDownloadException($"Segment failed after {_opt.MaxRetries} retries: {ex.Message}", ex);
                await Task.Delay(Backoff(attempt), ct);
            }
        }
    }

    private async Task TransferAsync(RunCtx c, Chunk chunk, CancellationToken ct)
    {
        if (!c.UseRange)
        {
            chunk.Done = 0;
            RandomAccess.SetLength(c.Handle, Math.Max(0, c.Total));
        }
        long pos = chunk.Start + chunk.Done;
        long? rangeEnd = c.UseRange && chunk.End >= 0 ? chunk.End : null;
        var (resp, _) = await SendAsync(c.Item, c.Url, c.UseRange ? pos : null, rangeEnd, ct, c.UseRange ? c.Validator : null);
        long? responseEnd = null;
        using (resp)
        {
            if (!resp.IsSuccessStatusCode) ThrowForStatus(resp);
            if (c.UseRange)
            {
                if (resp.StatusCode != HttpStatusCode.PartialContent)
                    throw new FatalDownloadException("Server stopped honoring Range requests");
                var cr = resp.Content.Headers.ContentRange;
                // Servers may cap a requested range or return its entire remaining tail.
                // The starting offset and entity identity must still be trustworthy.
                if (cr is not { HasRange: true, From: { } crFrom, To: { } crTo }
                    || !string.Equals(cr.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
                    || crFrom != pos || crTo < pos
                    || (cr.Length is { } crLen && c.Total > 0 && crLen != c.Total))
                    throw new FatalDownloadException("Server returned an unexpected byte range");
                responseEnd = crTo;
                if (c.Validator != null && !ValidatorsMatch(c.Validator, ResponseValidator(resp)))
                    throw new FatalDownloadException("Remote file changed during download; restart the download");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buf = ArrayPool<byte>.Shared.Rent(_opt.BufferSize);
            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (true)
                {
                    int want = buf.Length;
                    if (chunk.End >= 0) want = (int)Math.Min(want, chunk.Length - chunk.Done);
                    if (responseEnd.HasValue) want = (int)Math.Min(want, responseEnd.Value - (chunk.Start + chunk.Done) + 1);
                    if (want <= 0) break;

                    readCts.CancelAfter(_opt.ReadTimeout);
                    int n = await stream.ReadAsync(buf.AsMemory(0, want), readCts.Token);
                    if (n == 0) break;

                    if (_opt.Limiter != null) await _opt.Limiter.ThrottleAsync(n, ct);

                    try
                    {
                        // Splitting, writing and checkpointing share this lock: a worker must never
                        // write past a boundary moved by another worker while its read was pending.
                        lock (c.StateLock)
                        {
                            if (chunk.End >= 0) n = (int)Math.Min(n, chunk.Length - chunk.Done);
                            if (n <= 0) break;
                            ct.ThrowIfCancellationRequested();
                            RandomAccess.Write(c.Handle, buf.AsSpan(0, n), chunk.Start + chunk.Done);
                            chunk.Done += n;
                        }
                    }
                    catch (IOException ex) { throw new FatalDownloadException($"Disk write failed: {ex.Message}", ex); }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }

        if (chunk.End >= 0 && !chunk.IsComplete) throw new TransientException("Connection closed early");
        if (chunk.End < 0) chunk.End = chunk.Start + chunk.Done - 1; // unknown-length stream finished
    }

    // ───────────────────────── HTTP ─────────────────────────

    private async Task<(HttpResponseMessage, Uri)> SendAsync(DownloadItem item, Uri url, long? from, long? to, CancellationToken ct, string? validator = null)
    {
        var origin = new Uri(item.Request.Url);
        for (int hop = 0; hop < 10; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            req.Headers.TryAddWithoutValidation("User-Agent",
                item.Request.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MCDownloadManager/0.1");
            if (!string.IsNullOrWhiteSpace(item.Request.Referer))
                req.Headers.TryAddWithoutValidation("Referer", item.Request.Referer);

            bool sameSite = string.Equals(url.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
                            && !(origin.Scheme == "https" && url.Scheme == "http");
            if (sameSite && BuildCookie(item.Request, url) is { } cookie)
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (from.HasValue) req.Headers.Range = new RangeHeaderValue(from, to);
            if (from.HasValue && validator != null) req.Headers.TryAddWithoutValidation("If-Range", validator);

            using var hc = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hc.CancelAfter(_opt.ReadTimeout);
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, hc.Token);

            if (resp.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                && resp.Headers.Location is { } loc)
            {
                url = loc.IsAbsoluteUri ? loc : new Uri(url, loc);
                resp.Dispose();
                if (url.Scheme is not ("http" or "https")) throw new FatalDownloadException("Redirect to unsupported scheme");
                continue;
            }
            return (resp, url);
        }
        throw new FatalDownloadException("Too many redirects");
    }

    private static string? BuildCookie(DownloadRequest r, Uri url)
    {
        return RequestCookies.HeaderFor(r, url);
    }

    private static void ThrowForStatus(HttpResponseMessage r)
    {
        int code = (int)r.StatusCode;
        if (code is 429 or 503)
        {
            TimeSpan? after = r.Headers.RetryAfter?.Delta
                              ?? (r.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
            if (after is { } a) after = a < TimeSpan.Zero ? TimeSpan.Zero : (a > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : a);
            throw new ThrottledException(after);
        }
        if (code is 408 or >= 500) throw new TransientException($"HTTP {code}");
        throw new FatalDownloadException($"HTTP {code} {r.ReasonPhrase}");
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> op, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await op(); }
            catch (ThrottledException tex) when (attempt <= _opt.MaxRetries)
            {
                await Task.Delay(tex.RetryAfter ?? _opt.ThrottleBackoff, ct);
            }
            catch (Exception ex) when (attempt <= _opt.MaxRetries &&
                                       (ex is HttpRequestException or TransientException
                                        || (ex is OperationCanceledException && !ct.IsCancellationRequested)))
            {
                await Task.Delay(Backoff(attempt), ct);
            }
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(15, Math.Pow(2, attempt - 1)));

    // ───────────────────────── progress & persistence ─────────────────────────

    private static long SumDone(RunCtx c)
    {
        lock (c.StateLock) return c.Chunks.Sum(x => x.Done);
    }

    private async Task ProgressLoopAsync(RunCtx c, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();
        long initialBytes = SumDone(c);
        long lastBytes = initialBytes;
        double lastT = 0, speed = 0;
        int ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                long bytes = SumDone(c);
                double now = clock.Elapsed.TotalSeconds;
                double inst = (bytes - lastBytes) / Math.Max(0.001, now - lastT);

                // Monotonic warmup ramp in first 1.5s prevents fake spikes and socket handshake dips
                if (now < 1.5)
                {
                    double cumulative = (bytes - initialBytes) / Math.Max(0.05, now);
                    speed = Math.Max(speed, cumulative);
                }
                else
                {
                    speed = speed <= 0 ? inst : (speed * 0.75) + (inst * 0.25);
                }

                lastBytes = bytes; lastT = now;
                UpdateProgress(c, speed);
                if (++ticks % 10 == 0) Persist(c, flush: true);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void UpdateProgress(RunCtx c, double speed)
    {
        c.Item.DownloadedBytes = SumDone(c);
        c.Item.ActiveConnections = speed > 0 ? Volatile.Read(ref c.Active) : 0;
        c.Item.Speed = speed;
    }

    private static string PartPath(string savePath) => savePath + ".mcdm-part";
    private static string StatePath(string savePath) => savePath + ".mcdm";

    private static void Persist(RunCtx c, bool flush)
    {
        if (!c.UseRange || c.Total <= 0) return; // not resumable
        try
        {
            List<Chunk> chunksSnapshot;
            lock (c.StateLock)
            {
                if (flush) RandomAccess.FlushToDisk(c.Handle);
                chunksSnapshot = c.Chunks.Select(ch => new Chunk { Start = ch.Start, End = ch.End, Done = ch.Done }).ToList();
            }
            var state = new ResumeState { Total = c.Total, Validator = c.Validator, Ranges = true, Chunks = chunksSnapshot };
            var path = StatePath(c.Item.SavePath);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* best effort */ }
    }

    private static ResumeState? ReadState(string savePath)
    {
        try
        {
            var p = StatePath(savePath);
            return File.Exists(p) ? JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(p)) : null;
        }
        catch { return null; }
    }

    private static void AssignSavePath(DownloadItem item, string probedName)
    {
        lock (PathLock)
        {
            string chosenName;
            var reqName = item.Request.FileName?.Trim();

            // Only use request filename if it is non-empty and NOT a generic meaningless placeholder
            if (!string.IsNullOrWhiteSpace(reqName) && !FileNames.IsGenericFileName(reqName))
            {
                chosenName = reqName;
            }
            else if (!string.IsNullOrWhiteSpace(probedName) && !FileNames.IsGenericFileName(probedName))
            {
                chosenName = probedName;
            }
            else
            {
                chosenName = !string.IsNullOrWhiteSpace(probedName) ? probedName : (!string.IsNullOrWhiteSpace(reqName) ? reqName : "download");
            }

            if (string.IsNullOrWhiteSpace(chosenName))
            {
                chosenName = !string.IsNullOrWhiteSpace(probedName) ? probedName : "download";
            }

            // CRITICAL: NEVER strip or lose the file extension!
            // If chosenName lacks an extension, but probedName has a valid extension, preserve it!
            var chosenExt = Path.GetExtension(chosenName);
            var probedExt = Path.GetExtension(probedName);
            if (string.IsNullOrEmpty(chosenExt) && !string.IsNullOrEmpty(probedExt))
            {
                chosenName += probedExt;
            }

            var name = FileNames.Sanitize(chosenName);
            var dir = string.IsNullOrEmpty(item.Directory) ? Directory.GetCurrentDirectory() : item.Directory;
            Directory.CreateDirectory(dir);
            item.Directory = dir;
            item.SavePath = FileNames.Unique(dir, name);
            using (File.Create(PartPath(item.SavePath))) { } // reserve the name
        }
    }

    public static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }
}
