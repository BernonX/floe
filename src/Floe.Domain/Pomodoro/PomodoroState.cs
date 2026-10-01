namespace Floe.Domain.Pomodoro;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }
public enum PomodoroState { Ready, Running, Paused, AwaitingNext }
public enum PomodoroAction { Start, Pause, Resume, Skip, Restart, EnterNext }
public enum StageOutcome { Completed, Skipped, Restarted }
public enum PauseReason { None, User, SystemSleep, AppRestart, StorageFailure }
