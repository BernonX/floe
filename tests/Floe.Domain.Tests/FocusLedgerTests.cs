using Floe.Domain.Focus;

namespace Floe.Domain.Tests;

public sealed class FocusLedgerTests
{
    [Theory]
    [InlineData(FocusType.Pomodoro, FocusEndReason.Completed, 1)]
    [InlineData(FocusType.Pomodoro, FocusEndReason.Skipped, 0)]
    [InlineData(FocusType.Pomodoro, FocusEndReason.Restarted, 0)]
    [InlineData(FocusType.Free, FocusEndReason.Finished, 0)]
    public void CompletionProjectionIsIdempotentAndRebuildable(FocusType type, FocusEndReason reason, int expected)
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var start = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);
        var id = Guid.NewGuid();
        ledger.Record(new(id, type, 1, start, TimeSpan.FromSeconds(1)));
        ledger.End(id, start.AddSeconds(1), reason);
        ledger.End(id, start.AddSeconds(1), reason);
        Assert.Equal(0, ledger.GetCompletedPomodoros(new(2026, 9, 30)));
        Assert.Equal(expected, ledger.GetCompletedPomodoros(new(2026, 10, 1)));
        var restored = new FocusLedger(TimeZoneInfo.Utc);
        restored.Restore(ledger.GetSessions());
        Assert.Equal(expected, restored.GetCompletedPomodoros(new(2026, 10, 1)));
        Assert.Equal(TimeSpan.FromSeconds(1), restored.GetDay(new(2026, 9, 30)).TotalDuration);
    }

    [Fact]
    public void SplitsAtLocalMidnightAndDeduplicatesContributions()
    {
        var ledger = new FocusLedger(TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8"));
        var contribution = new FocusContribution(Guid.NewGuid(), FocusType.Free, 1,
            new DateTimeOffset(2026, 10, 1, 15, 59, 58, TimeSpan.Zero), TimeSpan.FromSeconds(5));
        ledger.Record(contribution);
        ledger.Record(contribution);
        Assert.Equal(TimeSpan.FromSeconds(2), ledger.GetDay(new(2026, 10, 1)).FreeDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), ledger.GetDay(new(2026, 10, 2)).FreeDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(ledger.GetSessions()).ActualDuration);
    }

    [Fact]
    public void ContiguousTicksMergeButPausesRetainDistinctIntervals()
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var id = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3600; i++) ledger.Record(new(id, FocusType.Free, i + 1, start.AddSeconds(i), TimeSpan.FromSeconds(1)));
        Assert.Single(Assert.Single(ledger.GetSessions()).Intervals);
        ledger.Record(new(id, FocusType.Free, 3601, start.AddHours(2), TimeSpan.FromMinutes(5)));
        ledger.End(id, start.AddHours(2).AddMinutes(5), FocusEndReason.Finished);
        var session = Assert.Single(ledger.GetSessions());
        Assert.Equal(2, session.Intervals.Count);
        Assert.Equal(TimeSpan.FromMinutes(65), session.ActualDuration);
        Assert.Equal(FocusEndReason.Finished, session.EndReason);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public void OutOfOrderContributionsAreRejectedInsteadOfSilentlyLosingTime()
    {
        var ledger = new FocusLedger(TimeZoneInfo.Utc);
        var first = new FocusContribution(Guid.NewGuid(), FocusType.Pomodoro, 1, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(() => ledger.Record(first with { Sequence = 2 }));
        ledger.Record(first);
        Assert.Throws<InvalidOperationException>(() => ledger.Record(first with { Sequence = 3 }));
        ledger.End(first.SessionId, first.StartedAtUtc.AddSeconds(1), FocusEndReason.Completed);
        Assert.Throws<InvalidOperationException>(() => ledger.Record(first with { Sequence = 2 }));
        Assert.True(Assert.Single(ledger.GetSessions()).IsCompleted);
    }

    [Theory]
    [InlineData(3, 8, 5, 23)]
    [InlineData(11, 1, 4, 25)]
    public void DstDaysUseTheirActualDuration(int month, int day, int startHourUtc, int hours)
    {
        var ledger = new FocusLedger(TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
        var start = new DateTimeOffset(2026, month, day, startHourUtc, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(hours), ledger.NextDayBoundary(start) - start);
        ledger.Record(new(Guid.NewGuid(), FocusType.Free, 1, start, TimeSpan.FromHours(hours + 1)));
        var date = new DateOnly(2026, month, day);
        Assert.Equal(TimeSpan.FromHours(hours), ledger.GetDay(date).TotalDuration);
        Assert.Equal(TimeSpan.FromHours(1), ledger.GetDay(date.AddDays(1)).TotalDuration);
    }
}
