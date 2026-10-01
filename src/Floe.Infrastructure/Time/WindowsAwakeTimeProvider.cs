using System.Runtime.InteropServices;

namespace Floe.Infrastructure.Time;

/// <summary>Monotonic Windows awake time. Sleep and hibernation never consume a focus duration.</summary>
public sealed class WindowsAwakeTimeProvider : TimeProvider
{
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        QueryUnbiasedInterruptTimePrecise(out var ticks);
        return checked((long)ticks);
    }

    // Use the Windows 10+ API contract: not every build forwards this export from kernel32.
    [DllImport("api-ms-win-core-realtime-l1-1-1.dll", ExactSpelling = true)]
    private static extern void QueryUnbiasedInterruptTimePrecise(out ulong unbiasedTime);
}
