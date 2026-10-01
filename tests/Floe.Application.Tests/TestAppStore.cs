using Floe.Application.Storage;
using Floe.Domain.Focus;
using Floe.Domain.Tasks;
using Floe.Domain.Pomodoro;
using Floe.Domain.Widgets;
using Floe.Application.SystemIntegration;

namespace Floe.Application.Tests;

internal sealed class TestAppStore : IAppStore
{
    private readonly Dictionary<Guid, TodoTask> tasks = [];
    private readonly Dictionary<Guid, FocusSession> sessions = [];
    private Guid? lastDeletedId;
    private FocusCheckpoint? checkpoint;
    private AppSettings settings = new();
    private WidgetSettings widget = new();
    private SystemSettings system = new();
    public bool FailWrites { get; set; }
    public int FocusWriteCount { get; private set; }
    public StoredAppState Load() => new(tasks.Values.ToArray(), lastDeletedId, sessions.Values.ToArray(),
        checkpoint?.Pomodoro.ConfiguredSettings ?? new(), settings, checkpoint, widget, system);
    public void SaveTask(TodoTask task, Guid? lastDeletedTaskId)
    {
        CheckWrite();
        tasks[task.Id] = task;
        lastDeletedId = lastDeletedTaskId;
    }
    public void SaveFocus(FocusCheckpoint value, IReadOnlyList<FocusSession> changedSessions,
        IReadOnlyList<DailyFocusRecord> changedDays, IReadOnlyList<PomodoroStageResult>? finishedStages = null)
    {
        CheckWrite();
        foreach (var session in changedSessions) sessions[session.Id] = session;
        checkpoint = value;
        FocusWriteCount++;
    }
    public void SaveAppSettings(AppSettings value) { CheckWrite(); settings = value; }
    public void SaveWidgetSettings(WidgetSettings value) { CheckWrite(); widget = value; }
    public void SaveSystemSettings(SystemSettings value) { CheckWrite(); system = value; }
    private void CheckWrite() { if (FailWrites) throw new DataStoreException("测试：磁盘无法写入。"); }
    public void Dispose() { }
}
