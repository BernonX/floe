using Floe.Domain.Pomodoro;

namespace Floe.Domain.Focus;

public enum FreeFocusState { Ready, Running, Paused }
public enum FreeFocusAction { Start, Pause, Resume, Finish }
public sealed record FreeFocusSnapshot(Guid SessionId, FreeFocusState State, TimeSpan Elapsed, PauseReason PauseReason);
public sealed record FreeFocusUpdate(FreeFocusSnapshot Snapshot, FocusContribution? Contribution,
    Guid? FinishedSessionId, DateTimeOffset ObservedAtUtc);
public sealed record FreeFocusCheckpoint(FreeFocusSnapshot Snapshot, long ContributionSequence);

/// <summary>A stopwatch without a UI timer. Pausing ends an activity interval, not a focus session.</summary>
public sealed class FreeFocusEngine(TimeProvider clock)
{
    private Guid sessionId = Guid.NewGuid();
    private FreeFocusState state;
    private PauseReason pauseReason;
    private TimeSpan elapsed;
    private TimeSpan accountedAtAnchor;
    private long anchorTimestamp;
    private DateTimeOffset anchorUtc;
    private long sequence;

    public FreeFocusUpdate Tick() => Process(null, null, false);
    public FreeFocusUpdate Execute(FreeFocusAction action, Guid expectedSessionId) => Process(action, expectedSessionId, false);
    public FreeFocusUpdate Suspend() => Process(null, null, true);
    public FreeFocusUpdate Suspend(PauseReason reason)
    {
        var update = Suspend();
        if (state == FreeFocusState.Paused && update.Snapshot.PauseReason == PauseReason.SystemSleep)
            pauseReason = reason;
        return update with { Snapshot = update.Snapshot with { PauseReason = pauseReason } };
    }
    public FreeFocusCheckpoint Capture(FreeFocusSnapshot settled) => new(settled, sequence);

    public void Restore(FreeFocusCheckpoint checkpoint)
    {
        var value = checkpoint.Snapshot;
        if (value.SessionId == Guid.Empty || value.Elapsed < TimeSpan.Zero || checkpoint.ContributionSequence < 0 ||
            (value.State == FreeFocusState.Ready && (value.Elapsed != TimeSpan.Zero || checkpoint.ContributionSequence != 0)) ||
            !Enum.IsDefined(value.State) || !Enum.IsDefined(value.PauseReason))
            throw new ArgumentException("Invalid free-focus checkpoint.", nameof(checkpoint));
        sessionId = value.SessionId;
        state = value.State == FreeFocusState.Running ? FreeFocusState.Paused : value.State;
        pauseReason = value.State == FreeFocusState.Running ? PauseReason.AppRestart : value.PauseReason;
        elapsed = value.Elapsed;
        sequence = checkpoint.ContributionSequence;
        accountedAtAnchor = TimeSpan.Zero;
    }

    private FreeFocusUpdate Process(FreeFocusAction? action, Guid? expectedId, bool suspend)
    {
        var now = clock.GetUtcNow();
        var timestamp = clock.GetTimestamp();
        FocusContribution? contribution = null;
        Guid? finished = null;
        if (state == FreeFocusState.Running)
        {
            var sinceAnchor = clock.GetElapsedTime(anchorTimestamp, timestamp);
            if ((now - (anchorUtc + sinceAnchor)).Duration() > TimeSpan.FromSeconds(1)) anchorUtc = now - sinceAnchor;
            var delta = sinceAnchor - accountedAtAnchor;
            if (delta > TimeSpan.Zero)
            {
                contribution = new(sessionId, FocusType.Free, ++sequence, anchorUtc + accountedAtAnchor, delta);
                elapsed += delta;
                accountedAtAnchor = sinceAnchor;
            }
        }
        if (suspend && state == FreeFocusState.Running)
        {
            state = FreeFocusState.Paused;
            pauseReason = PauseReason.SystemSleep;
        }
        else if (expectedId == sessionId)
        {
            switch (action)
            {
                case FreeFocusAction.Start when state == FreeFocusState.Ready:
                case FreeFocusAction.Resume when state == FreeFocusState.Paused:
                    state = FreeFocusState.Running;
                    pauseReason = PauseReason.None;
                    anchorTimestamp = timestamp;
                    anchorUtc = now;
                    accountedAtAnchor = TimeSpan.Zero;
                    break;
                case FreeFocusAction.Pause when state == FreeFocusState.Running:
                    state = FreeFocusState.Paused;
                    pauseReason = PauseReason.User;
                    break;
                case FreeFocusAction.Finish when state != FreeFocusState.Ready:
                    finished = sessionId;
                    sessionId = Guid.NewGuid();
                    state = FreeFocusState.Ready;
                    pauseReason = PauseReason.None;
                    elapsed = TimeSpan.Zero;
                    sequence = 0;
                    break;
            }
        }
        return new(new(sessionId, state, elapsed, pauseReason), contribution, finished, now);
    }
}
