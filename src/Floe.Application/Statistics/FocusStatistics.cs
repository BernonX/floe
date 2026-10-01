using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Focus;

namespace Floe.Application.Statistics;

public sealed record FocusStatisticsWindow(DateOnly Today, string TimeZoneId,
    IReadOnlyList<DailyFocusRecord> Days, int TodayCompletedPomodoros)
{
    public TimeSpan TotalDuration => Days.Aggregate(TimeSpan.Zero, (sum, day) => sum + day.TotalDuration);
    // Zero-activity calendar days belong to the denominator too.
    public TimeSpan DailyAverage => TimeSpan.FromTicks(TotalDuration.Ticks / Days.Count);
}

public sealed record StatisticsSnapshot(FocusStatisticsWindow Focus, int TodayCompletedTasks);

/// <summary>Small, bounded queries over the shared ledger. Task counts refresh only on edits or a new date.</summary>
public sealed class StatisticsService(FocusController focus, TodoController todo)
{
    private readonly object gate = new();
    private long taskVersion = -1;
    private DateOnly taskDate;
    private int completedTasks;
    private readonly TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(focus.Snapshot.StatisticsTimeZoneId);

    public StatisticsSnapshot GetSnapshot(int dayCount)
    {
        var window = focus.GetStatistics(dayCount);
        lock (gate)
        {
            var version = todo.Snapshot.Version;
            if (version != taskVersion || taskDate != window.Today)
            {
                completedTasks = todo.CountCompletedOn(window.Today, timeZone);
                taskVersion = version;
                taskDate = window.Today;
            }
            return new(window, completedTasks);
        }
    }
}
