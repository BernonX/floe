using Floe.Domain.Focus;
using Floe.Domain.Tasks;

namespace Floe.Domain.Tests;

public sealed class TaskFocusLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 4, 23, 59, 58, TimeSpan.Zero);

    [Fact]
    public void BoundAndUnboundModesShareDailyTotalsWithoutDoubleCounting()
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var taskId = Guid.NewGuid();
        var contribution = new FocusContribution(Guid.NewGuid(), FocusType.Pomodoro, 1, Start,
            TimeSpan.FromSeconds(2), taskId, "学习");
        Assert.True(ledger.Record(contribution));
        Assert.False(ledger.Record(contribution));
        Assert.Equal(taskId, ledger.End(contribution.SessionId, Start.AddSeconds(2), FocusEndReason.Completed));
        Assert.Null(ledger.End(contribution.SessionId, Start.AddSeconds(2), FocusEndReason.Completed));
        ledger.Record(new(Guid.NewGuid(), FocusType.Free, 1, Start, TimeSpan.FromSeconds(3), taskId, "学习"));
        ledger.Record(new(Guid.NewGuid(), FocusType.Free, 1, Start, TimeSpan.FromSeconds(1)));
        var stats = ledger.GetTaskStatistics(taskId, new(2026, 10, 4));
        Assert.Equal(TimeSpan.FromSeconds(5), stats.TotalDuration);
        Assert.Equal(1, stats.CompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(4), stats.TodayDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), ledger.GetDay(new(2026, 10, 4)).TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(1), ledger.GetDay(new(2026, 10, 5)).TotalDuration);
    }

    [Theory]
    [InlineData(FocusType.Pomodoro, FocusEndReason.Skipped)]
    [InlineData(FocusType.Pomodoro, FocusEndReason.Restarted)]
    [InlineData(FocusType.Free, FocusEndReason.Finished)]
    public void PartialAndFreeSessionsCountTimeButNotCompletedPomodoros(FocusType type, FocusEndReason reason)
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var taskId = Guid.NewGuid();
        var id = Guid.NewGuid();
        ledger.Record(new(id, type, 1, Start, TimeSpan.FromSeconds(10), taskId, "学习"));
        ledger.End(id, Start.AddSeconds(10), reason);
        var stats = ledger.GetTaskStatistics(taskId, new(2026, 10, 5));
        Assert.Equal(TimeSpan.FromSeconds(10), stats.TotalDuration);
        Assert.Equal(0, stats.CompletedPomodoros);
    }

    [Fact]
    public void SessionIdentityAndTitleCannotBeReattributed()
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var first = new FocusContribution(Guid.NewGuid(), FocusType.Free, 1, Start, TimeSpan.FromSeconds(1), Guid.NewGuid(), "原名");
        ledger.Record(first);
        Assert.Throws<InvalidOperationException>(() => ledger.Record(first with { Sequence = 2, TaskId = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => ledger.Record(first with { Sequence = 2, TaskTitleAtStart = "新名" }));
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(ledger.GetSessions()).ActualDuration);
    }

    [Fact]
    public void LocalMidnightAlsoStartsANewMondayWeek()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var ledger = new FocusLedger(zone);
        var id = Guid.NewGuid();
        ledger.Record(new(Guid.NewGuid(), FocusType.Free, 1, Start.AddHours(-8), TimeSpan.FromSeconds(5), id, "学习"));
        var sunday = ledger.GetTaskStatistics(id, new(2026, 10, 4));
        var monday = ledger.GetTaskStatistics(id, new(2026, 10, 5));
        Assert.Equal(TimeSpan.FromSeconds(2), sunday.ThisWeekDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), monday.ThisWeekDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), monday.TotalDuration);
    }

    [Fact]
    public void DerivedProgressDoesNotChangeTaskLifecycle()
    {
        var task = TodoTask.Create("学习", 3, Start).SetCompleted(true, Start.AddHours(1));
        var projected = task.WithFocusProgress(5, TimeSpan.FromHours(2));
        Assert.Equal(task.Id, projected.Id);
        Assert.Equal(task.UpdatedAtUtc, projected.UpdatedAtUtc);
        Assert.Equal(task.CompletedAtUtc, projected.CompletedAtUtc);
        Assert.Equal(0, task.CompletedPomodoros);
        Assert.Equal(5, projected.CompletedPomodoros); // Estimates are a target, never a cap.
    }
}
