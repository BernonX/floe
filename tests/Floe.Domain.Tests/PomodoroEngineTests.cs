using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Domain.Tests;

public sealed class PomodoroEngineTests
{
    private readonly ManualTimeProvider clock = new();
    private static PomodoroSettings ShortSettings => new()
    {
        FocusDuration = TimeSpan.FromSeconds(10), ShortBreakDuration = TimeSpan.FromSeconds(3),
        LongBreakDuration = TimeSpan.FromSeconds(7), FocusesPerCycle = 2
    };

    [Fact]
    public void DelayedTicksUseElapsedTimeRatherThanNumberOfCallbacks()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        var started = Act(engine, PomodoroAction.Start);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(10), started.Snapshot.TargetEndUtc);
        clock.Advance(TimeSpan.FromMilliseconds(5375));
        Assert.Equal(TimeSpan.FromMilliseconds(4625), engine.Tick().Snapshot.Remaining);
        Assert.Equal(TimeSpan.FromMilliseconds(4625), engine.Tick().Snapshot.Remaining);
    }

    [Fact]
    public void RepeatedPauseAndResumeExcludePausedTimePrecisely()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromMilliseconds(1250));
        var paused = Act(engine, PomodoroAction.Pause);
        Assert.Null(paused.Snapshot.TargetEndUtc);
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(TimeSpan.FromMilliseconds(8750), engine.Tick().Snapshot.Remaining);
        Act(engine, PomodoroAction.Resume);
        clock.Advance(TimeSpan.FromMilliseconds(2250));
        Act(engine, PomodoroAction.Pause);
        clock.Advance(TimeSpan.FromHours(2));
        Act(engine, PomodoroAction.Resume);
        clock.Advance(TimeSpan.FromMilliseconds(6500));
        var result = engine.Tick();
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.FinishedStages).ActualElapsed);
        Assert.Equal(1, result.Snapshot.CompletedFocusesThisRun);
    }

    [Theory]
    [InlineData(3600)]
    [InlineData(-7200)]
    public void WallClockCorrectionDoesNotCompleteOrExtendStage(int correctionSeconds)
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.AdjustUtc(TimeSpan.FromSeconds(correctionSeconds));
        var update = engine.Tick();
        Assert.Equal(TimeSpan.FromSeconds(8), update.Snapshot.Remaining);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(8), update.Snapshot.TargetEndUtc);
        Assert.Empty(update.FinishedStages);
    }

    [Fact]
    public void DefaultFourFocusCycleHasThreeShortBreaksThenLongBreak()
    {
        var settings = new PomodoroSettings { AutoStartBreak = true, AutoStartFocus = true };
        var engine = new PomodoroEngine(clock, settings);
        var snapshot = Act(engine, PomodoroAction.Start).Snapshot;
        var cycle = snapshot.CycleId;
        PomodoroPhase[] expected = [PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus,
            PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.LongBreak];
        foreach (var phase in expected)
        {
            Assert.Equal(phase, snapshot.Phase);
            Assert.Equal(PomodoroState.Running, snapshot.State);
            clock.Advance(snapshot.Remaining);
            snapshot = engine.Tick().Snapshot;
        }
        Assert.Equal(4, snapshot.CompletedFocusesThisRun);
        Assert.Equal(0, snapshot.CompletedFocusesInCycle);
        Assert.Equal(2, snapshot.CycleNumber);
        Assert.NotEqual(cycle, snapshot.CycleId);
        Assert.Equal(PomodoroPhase.Focus, snapshot.Phase);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void AutoAdvanceAndAutoStartAreIndependent(bool advance, bool startBreak, bool startFocus)
    {
        var engine = new PomodoroEngine(clock, ShortSettings with
        { AutoAdvance = advance, AutoStartBreak = startBreak, AutoStartFocus = startFocus });
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(10));
        var update = engine.Tick();
        if (!advance)
        {
            Assert.Equal(PomodoroState.AwaitingNext, update.Snapshot.State);
            update = Act(engine, PomodoroAction.EnterNext);
            Assert.Equal(PomodoroState.Ready, update.Snapshot.State);
        }
        else Assert.Equal(startBreak ? PomodoroState.Running : PomodoroState.Ready, update.Snapshot.State);

        Assert.Equal(PomodoroPhase.ShortBreak, update.Snapshot.Phase);
        if (update.Snapshot.State == PomodoroState.Ready) Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(3));
        update = engine.Tick();
        Assert.Equal(!advance ? PomodoroState.AwaitingNext : startFocus ? PomodoroState.Running : PomodoroState.Ready, update.Snapshot.State);
    }

    [Fact]
    public void LongDelayCompletesOnlyOneStageAndStartsNextAtObservationTime()
    {
        var engine = new PomodoroEngine(clock, ShortSettings with { AutoStartBreak = true, AutoStartFocus = true });
        var started = Act(engine, PomodoroAction.Start).Snapshot;
        clock.Advance(TimeSpan.FromDays(3));
        var update = engine.Tick();
        var result = Assert.Single(update.FinishedStages);
        Assert.Equal(started.TargetEndUtc, result.EndedAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(10), result.ActualElapsed);
        Assert.Equal(1, update.Snapshot.CompletedFocusesThisRun);
        Assert.Equal(TimeSpan.FromSeconds(3), update.Snapshot.Remaining);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(3), update.Snapshot.TargetEndUtc);
    }

    [Fact]
    public void SkipKeepsActualTimeButDoesNotEarnACompletedPomodoro()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromMilliseconds(1700));
        var update = Act(engine, PomodoroAction.Skip);
        var result = Assert.Single(update.FinishedStages);
        Assert.Equal(StageOutcome.Skipped, result.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(1700), result.ActualElapsed);
        Assert.Equal(0, update.Snapshot.CompletedFocusesThisRun);
        Assert.Equal(PomodoroPhase.ShortBreak, update.Snapshot.Phase);
    }

    [Fact]
    public void SkipsConsumePlannedPositionsIncludingTheLongBreak()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Assert.Equal(PomodoroPhase.ShortBreak, Act(engine, PomodoroAction.Skip).Snapshot.Phase);
        Assert.Equal(PomodoroPhase.Focus, Act(engine, PomodoroAction.Skip).Snapshot.Phase);
        var update = Act(engine, PomodoroAction.Skip);
        Assert.Equal(PomodoroPhase.LongBreak, update.Snapshot.Phase);
        Assert.Null(Assert.Single(update.FinishedStages).StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, update.FinishedStages[0].ActualElapsed);
        Assert.Equal(0, update.Snapshot.CompletedFocusesThisRun);
    }

    [Fact]
    public void RestartKeepsSlotAndCreatesNewAttemptWithoutDeletingPriorOutcome()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        var started = Act(engine, PomodoroAction.Start).Snapshot;
        clock.Advance(TimeSpan.FromSeconds(2));
        Act(engine, PomodoroAction.Pause);
        clock.Advance(TimeSpan.FromHours(1));
        var restarted = Act(engine, PomodoroAction.Restart);
        Assert.Equal(StageOutcome.Restarted, Assert.Single(restarted.FinishedStages).Outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), restarted.FinishedStages[0].ActualElapsed);
        Assert.Equal(started.StageIndex, restarted.Snapshot.StageIndex);
        Assert.NotEqual(started.StageId, restarted.Snapshot.StageId);
        Assert.Equal(2, restarted.Snapshot.AttemptNumber);
        Assert.Equal(PomodoroState.Ready, restarted.Snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(10), restarted.Snapshot.Remaining);
    }

    [Theory]
    [InlineData(PomodoroAction.Skip)] [InlineData(PomodoroAction.Pause)] [InlineData(PomodoroAction.Restart)]
    public void OperationAtDeadlineCannotAffectNextStage(PomodoroAction action)
    {
        var engine = new PomodoroEngine(clock, ShortSettings with { AutoStartBreak = true });
        var oldId = Act(engine, PomodoroAction.Start).Snapshot.StageId;
        clock.Advance(TimeSpan.FromSeconds(10));
        var update = engine.Execute(action, oldId);
        Assert.Equal(StageOutcome.Completed, Assert.Single(update.FinishedStages).Outcome);
        Assert.Equal(PomodoroPhase.ShortBreak, update.Snapshot.Phase);
        Assert.Equal(PomodoroState.Running, update.Snapshot.State);
        Assert.Empty(engine.Execute(action, oldId).FinishedStages);
    }

    [Fact]
    public void DuplicateStartDoesNotMoveTheDeadline()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        var started = Act(engine, PomodoroAction.Start).Snapshot;
        clock.Advance(TimeSpan.FromSeconds(4));
        var repeated = engine.Execute(PomodoroAction.Start, started.StageId).Snapshot;
        Assert.Equal(started.TargetEndUtc, repeated.TargetEndUtc);
        Assert.Equal(TimeSpan.FromSeconds(6), repeated.Remaining);
    }

    [Fact]
    public void SleepPausesAndNeverAutomaticallyResumes()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(2));
        var suspended = engine.Suspend();
        Assert.Equal(PauseReason.SystemSleep, suspended.Snapshot.PauseReason);
        clock.AdjustUtc(TimeSpan.FromHours(8));
        Assert.Equal(TimeSpan.FromSeconds(8), engine.Tick().Snapshot.Remaining);
        Assert.Equal(PomodoroState.Paused, engine.Tick().Snapshot.State);
    }

    [Fact]
    public void SleepAtDeadlineDoesNotAutoStartAnotherStage()
    {
        var engine = new PomodoroEngine(clock, ShortSettings with { AutoStartBreak = true });
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(PomodoroState.AwaitingNext, engine.Suspend().Snapshot.State);
    }

    [Fact]
    public void NewSettingsApplyAtNextCycleAndNeverChangeCurrentAttempt()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Act(engine, PomodoroAction.Start);
        clock.Advance(TimeSpan.FromSeconds(2));
        var next = ShortSettings with { FocusDuration = TimeSpan.FromSeconds(30), FocusesPerCycle = 1 };
        var configured = engine.Configure(next).Snapshot;
        Assert.True(configured.HasPendingSettings);
        Assert.Equal(TimeSpan.FromSeconds(8), configured.Remaining);
        for (var i = 0; i < 4; i++) Act(engine, PomodoroAction.Skip);
        var snapshot = engine.Tick().Snapshot;
        Assert.Equal(2, snapshot.CycleNumber);
        Assert.Equal(next, snapshot.CycleSettings);
        Assert.False(snapshot.HasPendingSettings);
        Assert.Equal(TimeSpan.FromSeconds(30), snapshot.Remaining);
    }

    [Fact]
    public void ReadyUntouchedCycleCanBeConfiguredImmediately()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        var update = engine.Configure(ShortSettings with { FocusDuration = TimeSpan.FromMinutes(1) });
        Assert.Equal(TimeSpan.FromMinutes(1), update.Snapshot.Remaining);
        Assert.False(update.Snapshot.HasPendingSettings);
    }

    [Fact]
    public void InvalidConfigurationsAreRejectedWithoutChangingState()
    {
        var engine = new PomodoroEngine(clock, ShortSettings);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Configure(ShortSettings with { FocusDuration = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Configure(ShortSettings with { ShortBreakDuration = TimeSpan.FromHours(2) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Configure(ShortSettings with { LongBreakDuration = TimeSpan.FromHours(3) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Configure(ShortSettings with { FocusesPerCycle = 13 }));
        Assert.Equal(ShortSettings, engine.Tick().Snapshot.CycleSettings);
    }

    private static PomodoroUpdate Act(PomodoroEngine engine, PomodoroAction action) => engine.Execute(action, engine.Tick().Snapshot.StageId);
}
