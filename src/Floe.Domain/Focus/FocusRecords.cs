namespace Floe.Domain.Focus;

public enum FocusType { Pomodoro, Free }
public enum FocusEndReason { Completed, Skipped, Restarted, Finished }

/// <summary>One sequential, idempotent contribution. Duration is measured by a monotonic clock.</summary>
public sealed record FocusContribution(Guid SessionId, FocusType Type, long Sequence,
    DateTimeOffset StartedAtUtc, TimeSpan Duration, Guid? TaskId = null, string? TaskTitleAtStart = null);

public sealed record FocusInterval(DateTimeOffset StartedAtUtc, TimeSpan Duration)
{
    public DateTimeOffset EndedAtUtc => StartedAtUtc + Duration;
}

public sealed record FocusSession(Guid Id, FocusType Type, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, TimeSpan ActualDuration, FocusEndReason? EndReason,
    IReadOnlyList<FocusInterval> Intervals, Guid? TaskId = null, string? TaskTitleAtStart = null,
    long ContributionSequence = 0)
{
    public bool IsCompleted => EndReason == FocusEndReason.Completed;
}

public sealed record TaskFocusStatistics(Guid TaskId, int CompletedPomodoros, TimeSpan TotalDuration,
    TimeSpan TodayDuration, TimeSpan ThisWeekDuration);

public sealed record DailyFocusRecord(DateOnly Date, TimeSpan PomodoroDuration, TimeSpan FreeDuration)
{
    public TimeSpan TotalDuration => PomodoroDuration + FreeDuration;
}
