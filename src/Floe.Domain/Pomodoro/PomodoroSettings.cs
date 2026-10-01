namespace Floe.Domain.Pomodoro;

/// <summary>Immutable configuration; each cycle keeps a validated snapshot.</summary>
public sealed record PomodoroSettings
{
    public TimeSpan FocusDuration { get; init; } = TimeSpan.FromMinutes(25);
    public TimeSpan ShortBreakDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan LongBreakDuration { get; init; } = TimeSpan.FromMinutes(15);
    public int FocusesPerCycle { get; init; } = 4;
    public bool AutoAdvance { get; init; } = true;
    public bool AutoStartBreak { get; init; }
    public bool AutoStartFocus { get; init; }

    public void Validate()
    {
        ValidateDuration(FocusDuration, TimeSpan.FromHours(3), nameof(FocusDuration));
        ValidateDuration(ShortBreakDuration, TimeSpan.FromHours(1), nameof(ShortBreakDuration));
        ValidateDuration(LongBreakDuration, TimeSpan.FromHours(2), nameof(LongBreakDuration));
        if (FocusesPerCycle is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(FocusesPerCycle));
    }

    public TimeSpan DurationFor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => FocusDuration,
        PomodoroPhase.ShortBreak => ShortBreakDuration,
        PomodoroPhase.LongBreak => LongBreakDuration,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    private static void ValidateDuration(TimeSpan value, TimeSpan maximum, string name)
    {
        if (value <= TimeSpan.Zero || value > maximum) throw new ArgumentOutOfRangeException(name);
    }
}
