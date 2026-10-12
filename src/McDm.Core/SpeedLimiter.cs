using System.Diagnostics;

namespace McDm.Core;

/// <summary>
/// Token-bucket rate limiter that throttles read throughput across workers/downloads.
/// Rate is specified in bytes per second (0 = unlimited).
/// </summary>
public sealed class SpeedLimiter
{
    private readonly object _gate = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastTicks;

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set
        {
            lock (_gate)
            {
                Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
                _tokens = _bytesPerSecond; // refill on change
                _lastTicks = Stopwatch.GetTimestamp();
            }
        }
    }

    public SpeedLimiter(long bytesPerSecond = 0)
    {
        _bytesPerSecond = Math.Max(0, bytesPerSecond);
        _tokens = _bytesPerSecond;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public async Task ThrottleAsync(int bytes, CancellationToken ct = default)
    {
        long limit = BytesPerSecond;
        if (limit <= 0 || bytes <= 0) return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double waitSec = 0;

            lock (_gate)
            {
                limit = _bytesPerSecond;
                if (limit <= 0) return;

                long now = Stopwatch.GetTimestamp();
                double elapsed = (double)(now - _lastTicks) / Stopwatch.Frequency;
                _lastTicks = now;

                _tokens = Math.Min(limit, _tokens + elapsed * limit);

                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }

                // Not enough tokens; calculate required wait duration
                double needed = bytes - _tokens;
                waitSec = needed / limit;
            }

            int waitMs = (int)Math.Clamp(waitSec * 1000.0, 1, 500);
            await Task.Delay(waitMs, ct);
        }
    }
}
