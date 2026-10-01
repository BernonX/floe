namespace Floe.Domain.Focus;

/// <summary>Rebuildable projection of activity intervals; all writes are serialized by the application coordinator.</summary>
public sealed class FocusLedger(TimeZoneInfo statisticsTimeZone)
{
    private readonly Dictionary<Guid, Entry> sessions = [];
    private readonly Dictionary<DateOnly, DailyFocusRecord> days = [];
    private readonly Dictionary<DateOnly, int> completedPomodoros = [];
    private readonly Dictionary<Guid, (TimeSpan Duration, int Completed)> tasks = [];
    private readonly Dictionary<(Guid TaskId, DateOnly Date), TimeSpan> taskDays = [];
    public TimeZoneInfo StatisticsTimeZone { get; } = statisticsTimeZone;

    public DateOnly DateAt(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, StatisticsTimeZone).DateTime);
    public DailyFocusRecord GetDay(DateOnly date) => days.GetValueOrDefault(date) ?? new(date, TimeSpan.Zero, TimeSpan.Zero);
    public IReadOnlyList<FocusSession> GetSessions() => sessions.Values.Select(x => x.Snapshot()).ToArray();
    public FocusSession? GetSession(Guid id) => sessions.GetValueOrDefault(id)?.Snapshot();
    public IEnumerable<DailyFocusRecord> GetDays() => days.Values;
    public int GetCompletedPomodoros(DateOnly date) => completedPomodoros.GetValueOrDefault(date);

    public void Restore(IEnumerable<FocusSession> records)
    {
        if (sessions.Count != 0) throw new InvalidOperationException("Restore requires an empty ledger.");
        foreach (var session in records)
        {
            if (session.Id == Guid.Empty || session.ActualDuration <= TimeSpan.Zero || session.ContributionSequence < 1 ||
                !Enum.IsDefined(session.Type) || (session.EndReason is { } reason && !Enum.IsDefined(reason)) ||
                session.EndReason.HasValue != session.EndedAtUtc.HasValue ||
                session.Intervals.Any(interval => interval.Duration <= TimeSpan.Zero) ||
                session.Intervals.Aggregate(TimeSpan.Zero, (total, interval) => total + interval.Duration) != session.ActualDuration)
                throw new ArgumentException("Invalid stored focus session.", nameof(records));
            var entry = new Entry(new(session.Id, session.Type, 1, session.StartedAtUtc,
                session.ActualDuration, session.TaskId, session.TaskTitleAtStart))
            {
                Sequence = session.ContributionSequence, Duration = session.ActualDuration,
                EndedAtUtc = session.EndedAtUtc, EndReason = session.EndReason
            };
            entry.Intervals.AddRange(session.Intervals);
            sessions.Add(session.Id, entry);
            if (session.Type == FocusType.Pomodoro && session.IsCompleted)
                CountCompletion(session.EndedAtUtc!.Value);
            if (session.TaskId is Guid id)
            {
                var totals = tasks.GetValueOrDefault(id);
                tasks[id] = (totals.Duration + session.ActualDuration,
                    totals.Completed + (session.Type == FocusType.Pomodoro && session.IsCompleted ? 1 : 0));
            }
            foreach (var interval in session.Intervals) AccumulateDays(session.Type, session.TaskId, interval);
        }
    }

    public TaskFocusStatistics GetTaskStatistics(Guid taskId, DateOnly today)
    {
        var totals = tasks.GetValueOrDefault(taskId);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var week = TimeSpan.Zero;
        for (var day = monday; day <= today; day = day.AddDays(1))
            week += taskDays.GetValueOrDefault((taskId, day));
        return new(taskId, totals.Completed, totals.Duration, taskDays.GetValueOrDefault((taskId, today)), week);
    }

    public bool Record(FocusContribution contribution)
    {
        if (contribution.Duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(contribution));
        if (!sessions.TryGetValue(contribution.SessionId, out var entry))
        {
            if (contribution.Sequence != 1) throw new InvalidOperationException("Missing first focus contribution.");
            entry = new Entry(contribution);
            sessions.Add(contribution.SessionId, entry);
        }
        if (contribution.Type != entry.Type) throw new InvalidOperationException("Session type cannot change.");
        if (contribution.TaskId != entry.TaskId || contribution.TaskTitleAtStart != entry.TaskTitleAtStart)
            throw new InvalidOperationException("Session task binding cannot change.");
        if (contribution.Sequence <= entry.Sequence) return false;
        if (contribution.Sequence != entry.Sequence + 1 || entry.EndReason is not null)
            throw new InvalidOperationException("Focus contributions must be delivered in order before completion.");

        // Merge consecutive updates so memory grows with sessions/pauses, not with every UI tick.
        var interval = new FocusInterval(contribution.StartedAtUtc, contribution.Duration);
        if (entry.Intervals.Count > 0 && entry.Intervals[^1].EndedAtUtc == interval.StartedAtUtc)
            entry.Intervals[^1] = entry.Intervals[^1] with { Duration = entry.Intervals[^1].Duration + interval.Duration };
        else entry.Intervals.Add(interval);
        entry.Sequence = contribution.Sequence;
        entry.Duration += contribution.Duration;
        if (entry.TaskId is Guid taskId)
        {
            var totals = tasks.GetValueOrDefault(taskId);
            tasks[taskId] = (totals.Duration + contribution.Duration, totals.Completed);
        }

        AccumulateDays(contribution.Type, entry.TaskId, interval);
        return true;
    }

    private void AccumulateDays(FocusType type, Guid? taskId, FocusInterval interval)
    {
        var cursor = interval.StartedAtUtc;
        var end = interval.EndedAtUtc;
        while (cursor < end)
        {
            var date = DateAt(cursor);
            var boundary = NextDayBoundary(cursor);
            var portion = (end < boundary ? end : boundary) - cursor;
            var day = GetDay(date);
            days[date] = type == FocusType.Pomodoro
                ? day with { PomodoroDuration = day.PomodoroDuration + portion }
                : day with { FreeDuration = day.FreeDuration + portion };
            if (taskId is Guid dailyTaskId)
                taskDays[(dailyTaskId, date)] = taskDays.GetValueOrDefault((dailyTaskId, date)) + portion;
            cursor += portion;
        }
    }

    public Guid? End(Guid sessionId, DateTimeOffset endedAtUtc, FocusEndReason reason)
    {
        if (!sessions.TryGetValue(sessionId, out var entry) || entry.EndReason is not null) return null;
        entry.EndedAtUtc = endedAtUtc;
        entry.EndReason = reason;
        if (entry.Type == FocusType.Pomodoro && reason == FocusEndReason.Completed) CountCompletion(endedAtUtc);
        if (entry.TaskId is Guid taskId && entry.Type == FocusType.Pomodoro && reason == FocusEndReason.Completed)
        {
            var totals = tasks.GetValueOrDefault(taskId);
            tasks[taskId] = (totals.Duration, totals.Completed + 1);
            return taskId;
        }
        return null;
    }

    private void CountCompletion(DateTimeOffset endedAtUtc)
    {
        var date = DateAt(endedAtUtc);
        completedPomodoros[date] = completedPomodoros.GetValueOrDefault(date) + 1;
    }

    public DateTimeOffset NextDayBoundary(DateTimeOffset utc)
    {
        var next = DateAt(utc).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones move forward at midnight, or skip a civil date entirely.
        while (StatisticsTimeZone.IsInvalidTime(next)) next = next.AddMinutes(1);
        var offset = StatisticsTimeZone.IsAmbiguousTime(next)
            ? StatisticsTimeZone.GetAmbiguousTimeOffsets(next).Max()
            : StatisticsTimeZone.GetUtcOffset(next);
        return new DateTimeOffset(next, offset).ToUniversalTime();
    }

    private sealed class Entry(FocusContribution first)
    {
        public Guid Id { get; } = first.SessionId;
        public FocusType Type { get; } = first.Type;
        public Guid? TaskId { get; } = first.TaskId;
        public string? TaskTitleAtStart { get; } = first.TaskTitleAtStart;
        public DateTimeOffset StartedAtUtc { get; } = first.StartedAtUtc;
        public DateTimeOffset? EndedAtUtc { get; set; }
        public FocusEndReason? EndReason { get; set; }
        public long Sequence { get; set; }
        public TimeSpan Duration { get; set; }
        public List<FocusInterval> Intervals { get; } = [];
        public FocusSession Snapshot() => new(Id, Type, StartedAtUtc, EndedAtUtc, Duration, EndReason,
            Intervals.ToArray(), TaskId, TaskTitleAtStart, Sequence);
    }
}
