using Floe.Application.Timing;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class FocusAccountingTests
{
    [Fact]
    public void PartialPomodoroAndFreeTimeShareOneLedgerWithoutDoubleCounting()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock);
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(12));
        controller.Execute(PomodoroAction.Restart, controller.Current.StageId);
        Assert.Equal(TimeSpan.FromSeconds(12), controller.Snapshot.Today.TotalDuration);
        Assert.Equal(0, controller.Current.CompletedFocusesThisRun);
        var freeId = controller.Snapshot.Free.SessionId;
        controller.ExecuteFree(FreeFocusAction.Start, freeId);
        clock.Advance(TimeSpan.FromSeconds(8));
        controller.ExecuteFree(FreeFocusAction.Pause, freeId);
        clock.Advance(TimeSpan.FromHours(1));
        controller.Refresh();
        controller.ExecuteFree(FreeFocusAction.Resume, freeId);
        clock.Advance(TimeSpan.FromSeconds(5));
        controller.ExecuteFree(FreeFocusAction.Finish, freeId);
        controller.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(25), controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(13), controller.Snapshot.Today.FreeDuration);
        Assert.Equal(2, controller.GetSessions().Count);
        Assert.Equal(FocusEndReason.Restarted, controller.GetSessions().Single(x => x.Type == FocusType.Pomodoro).EndReason);
        Assert.Equal(2, controller.GetSessions().Single(x => x.Type == FocusType.Free).Intervals.Count);
    }

    [Fact]
    public void NaturalCompletionIsCreditedOnceAndBreaksNeverCount()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock, new() { FocusDuration = TimeSpan.FromSeconds(10), AutoStartBreak = true });
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(3));
        controller.Refresh();
        clock.Advance(TimeSpan.FromMinutes(10));
        controller.Refresh();
        Assert.Equal(1, controller.Current.CompletedFocusesThisRun);
        Assert.Equal(TimeSpan.FromSeconds(10), controller.Snapshot.Today.TotalDuration);
        clock.Advance(TimeSpan.FromSeconds(30));
        controller.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(10), controller.Snapshot.Today.TotalDuration);
        Assert.True(Assert.Single(controller.GetSessions()).IsCompleted);
    }

    [Fact]
    public void BothModesCannotRunTogetherEvenThroughConcurrentCommands()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock);
        var pId = controller.Current.StageId;
        var fId = controller.Snapshot.Free.SessionId;
        Parallel.Invoke(() => controller.Execute(PomodoroAction.Start, pId),
            () => controller.ExecuteFree(FreeFocusAction.Start, fId));
        Assert.NotEqual(controller.Current.State == PomodoroState.Running, controller.Snapshot.Free.State == FreeFocusState.Running);
        clock.Advance(TimeSpan.FromMinutes(1));
        controller.Refresh();
        Assert.Equal(TimeSpan.FromMinutes(1), controller.Snapshot.Today.TotalDuration);
        Assert.Single(controller.GetSessions());
    }

    [Fact]
    public void FreeModeBlocksSkippingIntoAnAutomaticPomodoro()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock, new() { AutoStartFocus = true });
        controller.Execute(PomodoroAction.Skip, controller.Current.StageId);
        Assert.Equal(PomodoroPhase.ShortBreak, controller.Current.Phase);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        controller.Execute(PomodoroAction.Skip, controller.Current.StageId);
        Assert.Equal(PomodoroPhase.ShortBreak, controller.Current.Phase);
        Assert.Equal(PomodoroState.Ready, controller.Current.State);
    }

    [Fact]
    public void BothModesCanRetainPausedProgressWithoutOverlappingActiveTime()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(2));
        controller.ExecuteFree(FreeFocusAction.Pause, controller.Snapshot.Free.SessionId);
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(3));
        controller.ExecuteFree(FreeFocusAction.Resume, controller.Snapshot.Free.SessionId);
        Assert.Equal(FreeFocusState.Paused, controller.Snapshot.Free.State);
        controller.Execute(PomodoroAction.Pause, controller.Current.StageId);
        controller.ExecuteFree(FreeFocusAction.Resume, controller.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(4));
        controller.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(9), controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(6), controller.Snapshot.Free.Elapsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MidnightSplitsBothSourcesWithoutResettingTheirSession(bool free)
    {
        var clock = At(new(2026, 10, 1, 23, 59, 58, TimeSpan.Zero));
        using var controller = Create(clock);
        if (free) controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        else controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(clock.Timers).Fire();
        Assert.Equal(new DateOnly(2026, 10, 2), controller.Snapshot.Today.Date);
        Assert.Equal(TimeSpan.FromSeconds(3), controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), controller.GetDay(new(2026, 10, 1)).TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(controller.GetSessions()).ActualDuration);
        if (free) Assert.Equal(TimeSpan.FromSeconds(5), controller.Snapshot.Free.Elapsed);
    }

    [Fact]
    public void PausedMidnightRefreshChangesTodayWithoutPerSecondPolling()
    {
        var clock = At(new(2026, 10, 1, 23, 59, 50, TimeSpan.Zero));
        using var controller = Create(clock);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(4));
        controller.ExecuteFree(FreeFocusAction.Pause, controller.Snapshot.Free.SessionId);
        var timer = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromSeconds(6), timer.DueTime);
        clock.Advance(TimeSpan.FromSeconds(6));
        timer.Fire();
        Assert.Equal(TimeSpan.Zero, controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), controller.Snapshot.Free.Elapsed);
        Assert.Equal(TimeSpan.FromDays(1), timer.DueTime);
    }

    [Fact]
    public void SleepCrossingMidnightDoesNotAddFocusAndWakeRefreshesTheDate()
    {
        var clock = At(new(2026, 10, 1, 23, 59, 50, TimeSpan.Zero));
        using var controller = Create(clock);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(4));
        controller.Suspend();
        Assert.Equal(Timeout.InfiniteTimeSpan, Assert.Single(clock.Timers).DueTime);
        clock.Advance(TimeSpan.FromHours(8));
        controller.ResumeFromSleep();
        Assert.Equal(FreeFocusState.Paused, controller.Snapshot.Free.State);
        Assert.Equal(PauseReason.SystemSleep, controller.Snapshot.Free.PauseReason);
        Assert.Equal(new DateOnly(2026, 10, 2), controller.Snapshot.Today.Date);
        Assert.Equal(TimeSpan.Zero, controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), controller.Snapshot.Free.Elapsed);
    }

    [Fact]
    public void ClockCorrectionChangesCurrentDateButDoesNotRewriteCreditedHistory()
    {
        var clock = At(new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var controller = Create(clock);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(10));
        controller.Refresh();
        clock.AdjustUtc(TimeSpan.FromDays(1));
        clock.Advance(TimeSpan.FromSeconds(5));
        controller.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(15), controller.Snapshot.Free.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(5), controller.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), controller.GetDay(new(2026, 10, 1)).TotalDuration);
    }

    [Fact]
    public void LedgerIsUpdatedBeforeAnObserverSeesTheSnapshot()
    {
        var clock = new ManualTimeProvider();
        using var controller = Create(clock);
        controller.ExecuteFree(FreeFocusAction.Start, controller.Snapshot.Free.SessionId);
        controller.Changed += (_, update) => Assert.Equal(update.Snapshot.Today.TotalDuration,
            controller.GetSessions().Aggregate(TimeSpan.Zero, (sum, session) => sum + session.ActualDuration));
        clock.Advance(TimeSpan.FromSeconds(5));
        controller.Refresh();
    }

    private static FocusController Create(ManualTimeProvider clock, PomodoroSettings? settings = null) => new(clock, settings ?? new(), TimeZoneInfo.Utc);
    private static ManualTimeProvider At(DateTimeOffset time)
    {
        var clock = new ManualTimeProvider();
        clock.AdjustUtc(time - clock.GetUtcNow());
        return clock;
    }
}
