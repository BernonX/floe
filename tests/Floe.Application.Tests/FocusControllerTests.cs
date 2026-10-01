using System.Collections.Concurrent;
using Floe.Application.Timing;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class FocusControllerTests
{
    [Fact]
    public void TimerUsesMidnightWakeWhileIdleAndSecondsWhileRunning()
    {
        var clock = new ManualTimeProvider();
        using var controller = new FocusController(clock, new PomodoroSettings());
        var timer = Assert.Single(clock.Timers);
        Assert.True(timer.DueTime > TimeSpan.FromSeconds(1));
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.DueTime);
        clock.Advance(TimeSpan.FromSeconds(7));
        timer.Fire();
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(7), controller.Current.Remaining);
        controller.Execute(PomodoroAction.Pause, controller.Current.StageId);
        Assert.True(timer.DueTime > TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void FinalFractionSchedulesTheDeadlineInsteadOfAnotherWholeSecond()
    {
        var clock = new ManualTimeProvider();
        using var controller = new FocusController(clock, new PomodoroSettings { FocusDuration = TimeSpan.FromMilliseconds(1250) });
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(1));
        var timer = Assert.Single(clock.Timers);
        timer.Fire();
        Assert.Equal(TimeSpan.FromMilliseconds(250), timer.DueTime);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        timer.Fire();
        Assert.Equal(1, controller.Current.CompletedFocusesThisRun);
        Assert.True(timer.DueTime > TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ConcurrentStaleOperationsOnlyCompleteTheOldStageOnce()
    {
        var clock = new ManualTimeProvider();
        using var controller = new FocusController(clock, new PomodoroSettings { FocusDuration = TimeSpan.FromSeconds(1), AutoStartBreak = true });
        var results = new ConcurrentBag<PomodoroStageResult>();
        controller.Changed += (_, update) => { foreach (var result in update.FinishedStages) results.Add(result); };
        var originalId = controller.Current.StageId;
        Parallel.For(0, 32, _ => controller.Execute(PomodoroAction.Start, originalId));
        clock.Advance(TimeSpan.FromSeconds(1));
        Parallel.For(0, 32, _ => controller.Execute(PomodoroAction.Skip, originalId));
        Assert.Equal(StageOutcome.Completed, Assert.Single(results).Outcome);
        Assert.Equal(PomodoroPhase.ShortBreak, controller.Current.Phase);
        Assert.Equal(1, controller.Current.CompletedFocusesThisRun);
    }

    [Fact]
    public void SuspendStopsSchedulingAndRequiresAnExplicitResumeAfterWake()
    {
        var clock = new ManualTimeProvider();
        using var controller = new FocusController(clock, new PomodoroSettings());
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        clock.Advance(TimeSpan.FromMinutes(2));
        controller.Suspend();
        clock.Advance(TimeSpan.FromHours(8));
        controller.Execute(PomodoroAction.Resume, controller.Current.StageId);
        Assert.Equal(PomodoroState.Paused, controller.Current.State);
        controller.ResumeFromSleep();
        Assert.Equal(PomodoroState.Paused, controller.Current.State);
        Assert.Equal(TimeSpan.FromMinutes(23), controller.Current.Remaining);
        controller.Execute(PomodoroAction.Resume, controller.Current.StageId);
        Assert.Equal(PomodoroState.Running, controller.Current.State);
    }

    [Fact]
    public void DisposeDisarmsTimerAndIgnoresLateOperations()
    {
        var clock = new ManualTimeProvider();
        var controller = new FocusController(clock, new PomodoroSettings());
        controller.Execute(PomodoroAction.Start, controller.Current.StageId);
        controller.Dispose();
        var version = controller.Current.Version;
        Assert.True(Assert.Single(clock.Timers).IsDisposed);
        clock.Advance(TimeSpan.FromHours(1));
        clock.Timers[0].Fire();
        controller.Execute(PomodoroAction.Skip, controller.Current.StageId);
        Assert.Equal(version, controller.Current.Version);
    }
}
