using Floe.Domain.Focus;

namespace Floe.Domain.Pomodoro;

public sealed record PomodoroSnapshot(
    long Version,
    Guid StageId,
    Guid CycleId,
    int CycleNumber,
    int StageIndex,
    int AttemptNumber,
    PomodoroPhase Phase,
    PomodoroPhase NextPhase,
    PomodoroState State,
    PauseReason PauseReason,
    TimeSpan PlannedDuration,
    TimeSpan Remaining,
    DateTimeOffset? TargetEndUtc,
    int CompletedFocusesInCycle,
    int CompletedFocusesThisRun,
    PomodoroSettings CycleSettings,
    bool HasPendingSettings,
    PomodoroStageResult? LastResult);

/// <summary>A stage outcome, not a persisted FocusSession. ActualElapsed never includes pauses.</summary>
public sealed record PomodoroStageResult(
    Guid StageId,
    Guid CycleId,
    int StageIndex,
    int AttemptNumber,
    PomodoroPhase Phase,
    StageOutcome Outcome,
    TimeSpan PlannedDuration,
    TimeSpan ActualElapsed,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset EndedAtUtc);

public sealed record PomodoroUpdate(PomodoroSnapshot Snapshot, IReadOnlyList<PomodoroStageResult> FinishedStages,
    FocusContribution? Contribution);

public sealed record PomodoroCheckpoint(PomodoroSnapshot Snapshot, PomodoroSettings ConfiguredSettings,
    bool CycleTouched, DateTimeOffset? StartedAtUtc, long ContributionSequence);
