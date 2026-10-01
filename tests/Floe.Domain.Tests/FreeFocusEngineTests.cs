using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Domain.Tests;

public sealed class FreeFocusEngineTests
{
    [Fact]
    public void PauseResumeAccumulatesOnlyActiveIntervalsAndFinishCreatesANewSession()
    {
        var clock = new ManualTimeProvider();
        var engine = new FreeFocusEngine(clock);
        var id = engine.Tick().Snapshot.SessionId;
        engine.Execute(FreeFocusAction.Start, id);
        clock.Advance(TimeSpan.FromSeconds(7));
        var pause = engine.Execute(FreeFocusAction.Pause, id);
        Assert.Equal(TimeSpan.FromSeconds(7), pause.Contribution!.Duration);
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Null(engine.Tick().Contribution);
        engine.Execute(FreeFocusAction.Resume, id);
        clock.Advance(TimeSpan.FromSeconds(3));
        var running = engine.Tick();
        Assert.Equal(TimeSpan.FromSeconds(10), running.Snapshot.Elapsed);
        Assert.Equal(2, running.Contribution!.Sequence);
        var end = engine.Execute(FreeFocusAction.Finish, id);
        Assert.Equal(id, end.FinishedSessionId);
        Assert.NotEqual(id, end.Snapshot.SessionId);
        Assert.Equal(TimeSpan.Zero, end.Snapshot.Elapsed);
        Assert.Equal(FreeFocusState.Ready, end.Snapshot.State);
        Assert.Null(engine.Execute(FreeFocusAction.Finish, id).FinishedSessionId);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(-24)]
    public void WallClockCorrectionsDoNotChangeAccumulatedTime(int hours)
    {
        var clock = new ManualTimeProvider();
        var engine = new FreeFocusEngine(clock);
        engine.Execute(FreeFocusAction.Start, engine.Tick().Snapshot.SessionId);
        clock.Advance(TimeSpan.FromSeconds(10));
        engine.Tick();
        clock.AdjustUtc(TimeSpan.FromHours(hours));
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = engine.Tick();
        Assert.Equal(TimeSpan.FromSeconds(15), result.Snapshot.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Contribution!.Duration);
        Assert.Equal(clock.GetUtcNow(), result.Contribution.StartedAtUtc + result.Contribution.Duration);
    }

    [Fact]
    public void SleepPausesAndRepeatedCommandsDoNotResetTheAnchor()
    {
        var clock = new ManualTimeProvider();
        var engine = new FreeFocusEngine(clock);
        var id = engine.Tick().Snapshot.SessionId;
        engine.Execute(FreeFocusAction.Start, id);
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Execute(FreeFocusAction.Start, id);
        clock.Advance(TimeSpan.FromSeconds(3));
        var suspended = engine.Suspend();
        Assert.Equal(TimeSpan.FromSeconds(5), suspended.Snapshot.Elapsed);
        Assert.Equal(PauseReason.SystemSleep, suspended.Snapshot.PauseReason);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(TimeSpan.FromSeconds(5), engine.Tick().Snapshot.Elapsed);
    }
}
