namespace Floe.TestSupport;

/// <summary>UTC and elapsed time can move independently. Timers only fire when explicitly requested.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset utc = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => utc;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public List<ManualTimer> Timers { get; } = [];

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        utc += elapsed;
        Interlocked.Add(ref timestamp, elapsed.Ticks);
    }

    public void AdjustUtc(TimeSpan change) => utc += change;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        timer.Change(dueTime, period);
        Timers.Add(timer);
        return timer;
    }

    internal sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan DueTime { get; private set; }
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed) return false;
            DueTime = dueTime;
            return true;
        }

        public void Fire()
        {
            if (IsDisposed || DueTime == Timeout.InfiniteTimeSpan) return;
            DueTime = Timeout.InfiniteTimeSpan;
            callback(state);
        }

        public void Dispose() { IsDisposed = true; DueTime = Timeout.InfiniteTimeSpan; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
