using Floe.Domain.Focus;

namespace Floe.Domain.Pomodoro;

/// <summary>
/// Deterministic state machine. Callers serialize access. UI ticks never decrement a counter:
/// remaining time is projected from a monotonic timestamp and a duration anchor.
/// </summary>
public sealed class PomodoroEngine
{
    private readonly TimeProvider clock;
    private PomodoroSettings settings;
    private PomodoroSettings configuredSettings;
    private Guid stageId = Guid.NewGuid();
    private Guid cycleId = Guid.NewGuid();
    private int cycleNumber = 1;
    private int stageIndex;
    private int attemptNumber = 1;
    private int completedInCycle;
    private int completedThisRun;
    private bool cycleTouched;
    private PomodoroState state = PomodoroState.Ready;
    private PauseReason pauseReason;
    private TimeSpan remainingAtAnchor;
    private long anchorTimestamp;
    private DateTimeOffset anchorUtc;
    private DateTimeOffset? targetEndUtc;
    private DateTimeOffset? startedAtUtc;
    private PomodoroStageResult? lastResult;
    private long version;
    private TimeSpan accountedAtAnchor;
    private long contributionSequence;
    private FocusContribution? contribution;

    public PomodoroEngine(TimeProvider clock, PomodoroSettings settings)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        this.clock = clock;
        this.settings = configuredSettings = settings;
        remainingAtAnchor = settings.FocusDuration;
    }

    public PomodoroUpdate Tick() => Process(null, null);

    public PomodoroSettings ConfiguredSettings => configuredSettings;
    public PomodoroCheckpoint Capture(PomodoroSnapshot settled) =>
        new(settled, configuredSettings, cycleTouched, startedAtUtc, contributionSequence);

    public void Restore(PomodoroCheckpoint checkpoint)
    {
        var value = checkpoint.Snapshot;
        value.CycleSettings.Validate();
        checkpoint.ConfiguredSettings.Validate();
        if (value.StageId == Guid.Empty || value.CycleId == Guid.Empty || value.CycleNumber < 1 || value.AttemptNumber < 1 ||
            value.StageIndex < 0 || value.StageIndex >= value.CycleSettings.FocusesPerCycle * 2 ||
            value.Remaining < TimeSpan.Zero || value.Remaining > value.PlannedDuration || checkpoint.ContributionSequence < 0 ||
            value.CompletedFocusesInCycle < 0 || value.CompletedFocusesInCycle > value.CycleSettings.FocusesPerCycle ||
            value.CompletedFocusesThisRun < 0 || value.Version < 0 ||
            !Enum.IsDefined(value.State) || !Enum.IsDefined(value.PauseReason))
            throw new ArgumentException("Invalid Pomodoro checkpoint.", nameof(checkpoint));
        var expectedPhase = value.StageIndex % 2 == 0 ? PomodoroPhase.Focus :
            value.StageIndex == value.CycleSettings.FocusesPerCycle * 2 - 1 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        if (value.Phase != expectedPhase || value.PlannedDuration != value.CycleSettings.DurationFor(expectedPhase) ||
            (value.State == PomodoroState.AwaitingNext ? value.Remaining != TimeSpan.Zero : value.Remaining <= TimeSpan.Zero) ||
            (value.State == PomodoroState.Ready && (value.Remaining != value.PlannedDuration || checkpoint.ContributionSequence != 0)) ||
            (expectedPhase != PomodoroPhase.Focus && checkpoint.ContributionSequence != 0))
            throw new ArgumentException("Pomodoro checkpoint phase or duration is inconsistent.", nameof(checkpoint));
        settings = value.CycleSettings;
        configuredSettings = checkpoint.ConfiguredSettings;
        stageId = value.StageId;
        cycleId = value.CycleId;
        cycleNumber = value.CycleNumber;
        stageIndex = value.StageIndex;
        attemptNumber = value.AttemptNumber;
        completedInCycle = value.CompletedFocusesInCycle;
        // The home counter still means this launch; task/history completion counts live in the ledger.
        completedThisRun = 0;
        cycleTouched = checkpoint.CycleTouched;
        state = value.State == PomodoroState.Running ? PomodoroState.Paused : value.State;
        pauseReason = value.State == PomodoroState.Running ? PauseReason.AppRestart : value.PauseReason;
        remainingAtAnchor = value.Remaining;
        targetEndUtc = null;
        startedAtUtc = checkpoint.StartedAtUtc;
        lastResult = value.LastResult;
        version = value.Version;
        contributionSequence = checkpoint.ContributionSequence;
        accountedAtAnchor = TimeSpan.Zero;
        contribution = null;
    }

    public PomodoroUpdate Execute(PomodoroAction action, Guid expectedStageId) => Process(action, expectedStageId);

    public PomodoroUpdate Configure(PomodoroSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        configuredSettings = value;
        // Before the first operation of a cycle, its initial Ready stage can be configured immediately.
        if (!cycleTouched && state == PomodoroState.Ready)
        {
            settings = value;
            remainingAtAnchor = settings.FocusDuration;
            stageId = Guid.NewGuid();
        }
        return Process(null, null);
    }

    public PomodoroUpdate Suspend(PauseReason reason = PauseReason.SystemSleep)
    {
        var now = ReadClock();
        var finished = new List<PomodoroStageResult>(1);
        Reconcile(now, finished, allowAutomaticAdvance: false);
        if (state == PomodoroState.Running) Pause(now, reason);
        return CreateUpdate(now, finished);
    }

    private PomodoroUpdate Process(PomodoroAction? action, Guid? expectedStageId)
    {
        var now = ReadClock();
        var finished = new List<PomodoroStageResult>(1);
        var endedDuringReconciliation = Reconcile(now, finished, allowAutomaticAdvance: true);
        // A stale click must never skip/pause/restart a newly entered stage, even at the exact deadline.
        if (action is not null && expectedStageId == stageId && !endedDuringReconciliation)
        {
            switch (action)
            {
                case PomodoroAction.Start when state == PomodoroState.Ready:
                case PomodoroAction.Resume when state == PomodoroState.Paused:
                    Start(now);
                    break;
                case PomodoroAction.Pause when state == PomodoroState.Running:
                    Pause(now, PauseReason.User);
                    break;
                case PomodoroAction.Skip when state != PomodoroState.AwaitingNext:
                    Finish(StageOutcome.Skipped, now, finished);
                    if (settings.AutoAdvance) EnterNext(now, allowAutoStart: true);
                    break;
                case PomodoroAction.Restart when state is PomodoroState.Running or PomodoroState.Paused:
                    Finish(StageOutcome.Restarted, now, finished);
                    attemptNumber++;
                    PrepareStage();
                    break;
                case PomodoroAction.EnterNext when state == PomodoroState.AwaitingNext:
                    EnterNext(now, allowAutoStart: false);
                    break;
            }
        }
        return CreateUpdate(now, finished);
    }

    private bool Reconcile(ClockReading now, List<PomodoroStageResult> finished, bool allowAutomaticAdvance)
    {
        contribution = null;
        if (state != PomodoroState.Running) return false;
        var elapsed = ElapsedSinceAnchor(now);
        // Keep a UTC deadline for future persistence, but don't let wall-clock corrections alter duration.
        if ((now.Utc - (anchorUtc + elapsed)).Duration() > TimeSpan.FromSeconds(1))
        {
            anchorUtc = now.Utc - elapsed;
            targetEndUtc = anchorUtc + remainingAtAnchor;
        }
        var accountable = elapsed < remainingAtAnchor ? elapsed : remainingAtAnchor;
        if (Phase == PomodoroPhase.Focus && accountable > accountedAtAnchor)
        {
            contribution = new(stageId, FocusType.Pomodoro, ++contributionSequence,
                anchorUtc + accountedAtAnchor, accountable - accountedAtAnchor);
            accountedAtAnchor = accountable;
        }
        if (elapsed < remainingAtAnchor) return false;

        var deadline = new ClockReading(targetEndUtc ?? now.Utc, now.Timestamp);
        Finish(StageOutcome.Completed, deadline, finished);
        // Do not replay missed cycles after a long callback delay. The next stage starts at observation time.
        if (allowAutomaticAdvance && settings.AutoAdvance) EnterNext(now, allowAutoStart: true);
        return true;
    }

    private void Start(ClockReading now)
    {
        cycleTouched = true;
        startedAtUtc ??= now.Utc;
        anchorTimestamp = now.Timestamp;
        anchorUtc = now.Utc;
        accountedAtAnchor = TimeSpan.Zero;
        targetEndUtc = now.Utc + remainingAtAnchor;
        state = PomodoroState.Running;
        pauseReason = PauseReason.None;
    }

    private void Pause(ClockReading now, PauseReason reason)
    {
        remainingAtAnchor = Remaining(now);
        targetEndUtc = null;
        state = PomodoroState.Paused;
        pauseReason = reason;
    }

    private void Finish(StageOutcome outcome, ClockReading now, List<PomodoroStageResult> finished)
    {
        var duration = settings.DurationFor(Phase);
        var actual = outcome == StageOutcome.Completed ? duration : duration - Remaining(now);
        lastResult = new PomodoroStageResult(stageId, cycleId, stageIndex, attemptNumber, Phase,
            outcome, duration, actual, startedAtUtc, now.Utc);
        finished.Add(lastResult);
        if (outcome == StageOutcome.Completed && Phase == PomodoroPhase.Focus)
        {
            completedInCycle++;
            completedThisRun++;
        }
        cycleTouched = true;
        remainingAtAnchor = TimeSpan.Zero;
        targetEndUtc = null;
        state = PomodoroState.AwaitingNext;
        pauseReason = PauseReason.None;
    }

    private void EnterNext(ClockReading now, bool allowAutoStart)
    {
        stageIndex++;
        if (stageIndex == settings.FocusesPerCycle * 2)
        {
            cycleNumber++;
            cycleId = Guid.NewGuid();
            stageIndex = 0;
            completedInCycle = 0;
            cycleTouched = false;
            settings = configuredSettings;
        }
        attemptNumber = 1;
        PrepareStage();
        if (allowAutoStart && (Phase == PomodoroPhase.Focus ? settings.AutoStartFocus : settings.AutoStartBreak)) Start(now);
    }

    private void PrepareStage()
    {
        contributionSequence = 0;
        stageId = Guid.NewGuid();
        startedAtUtc = null;
        targetEndUtc = null;
        remainingAtAnchor = settings.DurationFor(Phase);
        state = PomodoroState.Ready;
        pauseReason = PauseReason.None;
    }

    private PomodoroPhase Phase => stageIndex % 2 == 0 ? PomodoroPhase.Focus :
        stageIndex == settings.FocusesPerCycle * 2 - 1 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;

    private PomodoroPhase NextPhase => stageIndex % 2 != 0 ? PomodoroPhase.Focus :
        stageIndex / 2 == settings.FocusesPerCycle - 1 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;

    private TimeSpan ElapsedSinceAnchor(ClockReading now) => clock.GetElapsedTime(anchorTimestamp, now.Timestamp);
    private TimeSpan Remaining(ClockReading now) => state == PomodoroState.Running
        ? TimeSpan.FromTicks(Math.Max(0, (remainingAtAnchor - ElapsedSinceAnchor(now)).Ticks)) : remainingAtAnchor;
    private ClockReading ReadClock() => new(clock.GetUtcNow(), clock.GetTimestamp());

    private PomodoroUpdate CreateUpdate(ClockReading now, List<PomodoroStageResult> finished) => new(
        new PomodoroSnapshot(++version, stageId, cycleId, cycleNumber, stageIndex, attemptNumber, Phase, NextPhase,
            state, pauseReason, settings.DurationFor(Phase), Remaining(now), targetEndUtc, completedInCycle,
            completedThisRun, settings, settings != configuredSettings, lastResult), finished.AsReadOnly(), contribution);

    private readonly record struct ClockReading(DateTimeOffset Utc, long Timestamp);
}
