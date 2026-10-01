using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Pomodoro;
using Floe.Domain.Tasks;

namespace Floe.Application.SystemIntegration;

public sealed record CompletionNotice(string Title, string Message);

/// <summary>A best-effort UI/platform boundary. Implementations must not throw into accounting callbacks.</summary>
public interface ICompletionNotificationSink
{
    void Deliver(CompletionNotice notice, bool showNotification, bool playSound);
}

/// <summary>Only new, committed completions produce reminders; restoring a checkpoint never replays history.</summary>
public sealed class CompletionNotifications : IDisposable
{
    private readonly object gate = new();
    private readonly FocusController focus;
    private readonly TodoController todo;
    private readonly SystemSettingsController settings;
    private readonly ICompletionNotificationSink sink;
    private readonly Dictionary<Guid, PomodoroStageResult> pending = [];
    private readonly HashSet<Guid> delivered = [];
    private readonly Queue<Guid> recent = [];
    private long committedVersion = -1;
    private bool disposed;

    public CompletionNotifications(FocusController focus, TodoController todo,
        SystemSettingsController settings, ICompletionNotificationSink sink)
    {
        this.focus = focus;
        this.todo = todo;
        this.settings = settings;
        this.sink = sink;
        focus.Changed += OnFocusChanged;
        todo.Completed += OnTaskCompleted;
    }

    private void OnFocusChanged(object? sender, FocusUpdate update)
    {
        List<CompletionNotice> notices = [];
        lock (gate)
        {
            if (disposed) return;
            foreach (var stage in update.FinishedStages)
                if (stage.Outcome == StageOutcome.Completed && !delivered.Contains(stage.StageId)) pending[stage.StageId] = stage;
            if (update.Snapshot.PersistenceError is null) committedVersion = Math.Max(committedVersion, update.Snapshot.Version);
            // An older callback may arrive after a newer successful commit.
            if (committedVersion < update.Snapshot.Version) return;
            foreach (var stage in pending.Values.OrderBy(stage => stage.EndedAtUtc))
            {
                delivered.Add(stage.StageId);
                recent.Enqueue(stage.StageId);
                while (recent.Count > 256) delivered.Remove(recent.Dequeue());
                notices.Add(stage.Phase == PomodoroPhase.Focus
                    ? new("专注结束", "已完成一段专注，稍作休息。")
                    : new("休息结束", "准备好了，就开始下一段专注。"));
            }
            pending.Clear();
        }
        foreach (var notice in notices) Deliver(notice);
    }

    private void OnTaskCompleted(object? sender, TodoTask task)
    {
        lock (gate) { if (disposed) return; }
        Deliver(new("任务完成", task.Title));
    }

    public void TestReminder() => Deliver(new("提醒已开启", "专注和休息结束时，Floe 会在这里提醒你。"));
    private void Deliver(CompletionNotice notice)
    {
        var current = settings.Settings;
        if (current.NotificationsEnabled || current.SoundEnabled)
            sink.Deliver(notice, current.NotificationsEnabled, current.SoundEnabled);
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; pending.Clear(); }
        focus.Changed -= OnFocusChanged;
        todo.Completed -= OnTaskCompleted;
    }
}
