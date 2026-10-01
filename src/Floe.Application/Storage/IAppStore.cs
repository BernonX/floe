using Floe.Application.Timing;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Domain.Tasks;
using Floe.Domain.Widgets;
using Floe.Application.SystemIntegration;

namespace Floe.Application.Storage;

public enum AppTheme { System, Light, Dark }
public sealed record AppSettings(AppTheme Theme = AppTheme.System, bool ReduceTransparency = false, bool ReduceMotion = false);
public sealed record StoredAppState(IReadOnlyList<TodoTask> Tasks, Guid? LastDeletedTaskId,
    IReadOnlyList<FocusSession> Sessions, PomodoroSettings PomodoroSettings, AppSettings AppSettings,
    FocusCheckpoint? Checkpoint, WidgetSettings? WidgetSettings = null, SystemSettings? SystemSettings = null);

public sealed record FocusCheckpoint(long Version, PomodoroCheckpoint Pomodoro, FreeFocusCheckpoint Free,
    Guid? SelectedTaskId, FocusTaskReference? PomodoroTask, FocusTaskReference? FreeTask,
    string StatisticsTimeZoneId, DateTimeOffset SavedAtUtc);

/// <summary>Atomic local writes; the application has no dependency on SQLite or Windows.</summary>
public interface IAppStore : IDisposable
{
    StoredAppState Load();
    void SaveTask(TodoTask task, Guid? lastDeletedTaskId);
    void SaveFocus(FocusCheckpoint checkpoint, IReadOnlyList<FocusSession> changedSessions,
        IReadOnlyList<DailyFocusRecord> changedDays, IReadOnlyList<PomodoroStageResult>? finishedStages = null);
    void SaveAppSettings(AppSettings settings);
    void SaveWidgetSettings(WidgetSettings settings);
    void SaveSystemSettings(SystemSettings settings);
}

public sealed class DataStoreException(string message, Exception? innerException = null) : Exception(message, innerException);
