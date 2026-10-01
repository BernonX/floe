using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Domain.Tests;

public sealed class CheckpointValidationTests
{
    [Fact]
    public void InconsistentPhaseAndReadyDurationCannotBeRestored()
    {
        var engine = new PomodoroEngine(new ManualTimeProvider(), new());
        var checkpoint = engine.Capture(engine.Tick().Snapshot);
        Assert.Throws<ArgumentException>(() => engine.Restore(checkpoint with
        { Snapshot = checkpoint.Snapshot with { Phase = PomodoroPhase.ShortBreak } }));
        Assert.Throws<ArgumentException>(() => engine.Restore(checkpoint with
        { Snapshot = checkpoint.Snapshot with { Remaining = TimeSpan.FromSeconds(1) } }));
    }

    [Fact]
    public void ReadyFreeSessionCannotCarryUnfinishedTime()
    {
        var engine = new FreeFocusEngine(new ManualTimeProvider());
        var checkpoint = engine.Capture(engine.Tick().Snapshot);
        Assert.Throws<ArgumentException>(() => engine.Restore(checkpoint with
        { Snapshot = checkpoint.Snapshot with { Elapsed = TimeSpan.FromSeconds(1) } }));
    }
}
