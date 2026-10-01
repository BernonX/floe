using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Application.Tasks;
using Floe.Application.Storage;
using Floe.Application.Statistics;

namespace Floe.Application.Timing;

public sealed record FocusTaskReference(Guid Id, string TitleAtStart);
public sealed record FocusSnapshot(long Version, PomodoroSnapshot Pomodoro, FreeFocusSnapshot Free,
    DailyFocusRecord Today, string StatisticsTimeZoneId, FocusTaskReference? SelectedTask,
    FocusTaskReference? PomodoroTask, FocusTaskReference? FreeTask, bool CanChangeTask, string? PersistenceError);
public sealed record FocusUpdate(FocusSnapshot Snapshot, IReadOnlyList<PomodoroStageResult> FinishedStages,
    IReadOnlyList<Guid> ChangedTaskIds);

/// <summary>One lock and timer coordinate both modes and the ledger, before notifying any UI consumers.</summary>
public sealed class FocusController : IDisposable
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly PomodoroEngine pomodoro;
    private readonly FreeFocusEngine free;
    private readonly FocusLedger ledger;
    private readonly ITimer timer;
    private readonly TodoController? todo;
    private readonly IAppStore? store;
    private readonly HashSet<Guid> dirtySessions = [];
    private readonly HashSet<DateOnly> dirtyDays = [];
    private readonly Dictionary<Guid, PomodoroStageResult> dirtyStages = [];
    private long lastSaveTimestamp;
    private string? persistenceError;
    private Guid? selectedTaskId;
    private SessionBinding? pomodoroBinding;
    private SessionBinding? freeBinding;
    private sealed record SessionBinding(Guid SessionId, FocusTaskReference? Task);
    private FocusSnapshot snapshot;
    private long version;
    private bool sleeping;
    private bool disposed;

    public FocusController(TimeProvider clock, PomodoroSettings settings, TimeZoneInfo? statisticsTimeZone = null,
        TodoController? todo = null, IAppStore? store = null, StoredAppState? restored = null)
    {
        this.clock = clock;
        this.todo = todo;
        this.store = store;
        pomodoro = new(clock, settings);
        free = new(clock);
        var checkpoint = restored?.Checkpoint;
        if (checkpoint is null && restored?.Sessions.Count > 0)
            throw new DataStoreException("已有专注历史缺少对应的恢复检查点，原数据已保留。");
        ledger = new(checkpoint is null ? statisticsTimeZone ?? clock.LocalTimeZone :
            TimeZoneInfo.FindSystemTimeZoneById(checkpoint.StatisticsTimeZoneId));
        if (checkpoint is not null)
        {
            pomodoro.Restore(checkpoint.Pomodoro);
            free.Restore(checkpoint.Free);
            version = checkpoint.Version;
            selectedTaskId = checkpoint.SelectedTaskId;
            var p = checkpoint.Pomodoro.Snapshot;
            if (p.Phase == PomodoroPhase.Focus && p.State is PomodoroState.Running or PomodoroState.Paused)
                pomodoroBinding = new(p.StageId, checkpoint.PomodoroTask);
            if (checkpoint.Free.Snapshot.State != FreeFocusState.Ready)
                freeBinding = new(checkpoint.Free.Snapshot.SessionId, checkpoint.FreeTask);
        }
        if (restored is not null) ledger.Restore(restored.Sessions);
        if (checkpoint is not null) ValidateRestoredState(checkpoint);
        snapshot = CreateSnapshot(pomodoro.Tick().Snapshot, free.Tick().Snapshot);
        lastSaveTimestamp = clock.GetTimestamp();
        if (store is not null)
        {
            foreach (var day in ledger.GetDays()) dirtyDays.Add(day.Date);
            Persist(snapshot.Pomodoro, snapshot.Free); // Commit paused recovery before accepting user commands.
        }
        timer = clock.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Schedule();
        if (todo is not null) todo.Changed += OnTasksChanged;
    }

    public PomodoroSnapshot Current { get { lock (gate) return snapshot.Pomodoro; } }
    public FocusSnapshot Snapshot { get { lock (gate) return snapshot; } }
    public event EventHandler<FocusUpdate>? Changed;
    public PomodoroSettings ConfiguredSettings { get { lock (gate) return pomodoro.ConfiguredSettings; } }
    public void RetrySave() => Process(forceSave: true);
    public bool PrepareForShutdown()
    {
        Process(shutdown: true, forceSave: true);
        return Snapshot.PersistenceError is null;
    }

    public IReadOnlyList<FocusSession> GetSessions() { lock (gate) return ledger.GetSessions(); }
    public DailyFocusRecord GetDay(DateOnly date) { lock (gate) return ledger.GetDay(date); }
    public FocusStatisticsWindow GetStatistics(int dayCount)
    {
        if (dayCount is not (7 or 30)) throw new ArgumentOutOfRangeException(nameof(dayCount));
        lock (gate)
        {
            var today = ledger.DateAt(clock.GetUtcNow());
            var records = Enumerable.Range(0, dayCount)
                .Select(index => ledger.GetDay(today.AddDays(index - dayCount + 1))).ToArray();
            return new(today, ledger.StatisticsTimeZone.Id, Array.AsReadOnly(records), ledger.GetCompletedPomodoros(today));
        }
    }
    public TaskFocusStatistics GetTaskStatistics(Guid taskId)
    {
        lock (gate) return ledger.GetTaskStatistics(taskId, ledger.DateAt(clock.GetUtcNow()));
    }
    public bool SelectTask(Guid? taskId) => Process(selectTask: true, taskId: taskId);
    public void Refresh() => Process();
    public void Execute(PomodoroAction action, Guid expectedStageId) => Process(action, expectedStageId);
    public void ExecuteFree(FreeFocusAction action, Guid expectedSessionId) => Process(freeAction: action, expectedId: expectedSessionId);
    public void Configure(PomodoroSettings settings) => Process(settings: settings);

    private bool Process(PomodoroAction? action = null, Guid? expectedId = null,
        FreeFocusAction? freeAction = null, PomodoroSettings? settings = null, bool suspend = false, bool resume = false,
        bool selectTask = false, Guid? taskId = null, bool forceSave = false, bool shutdown = false)
    {
        FocusUpdate update;
        var accepted = false;
        lock (gate)
        {
            if (disposed || (sleeping && !resume && !shutdown && !forceSave)) return false;
            var outcomes = new List<PomodoroStageResult>();
            var changedTasks = new HashSet<Guid>();
            var p = shutdown ? pomodoro.Suspend(PauseReason.AppRestart) : suspend || resume ? pomodoro.Suspend() : pomodoro.Tick();
            var f = shutdown ? free.Suspend(PauseReason.AppRestart) : suspend || resume ? free.Suspend() : free.Tick();
            Record(p, outcomes, changedTasks);
            Record(f, changedTasks);

            if (persistenceError is null && selectTask && CanChangeTask(p.Snapshot, f.Snapshot) &&
                (taskId is null || ResolveSelectable(taskId) is not null))
            {
                selectedTaskId = taskId;
                accepted = true;
            }

            // Reject conflicting commands centrally: UI, future tray and widget share this rule.
            if (persistenceError is null && action.HasValue && f.Snapshot.State != FreeFocusState.Running)
            {
                // A natural transition just observed must consume the old click, even without AutoAdvance.
                if (p.FinishedStages.Count == 0)
                {
                    p = pomodoro.Execute(action.Value, expectedId!.Value);
                    Record(p, outcomes, changedTasks);
                }
            }
            if (persistenceError is null && freeAction.HasValue && (p.Snapshot.State != PomodoroState.Running ||
                freeAction is FreeFocusAction.Pause or FreeFocusAction.Finish))
            {
                f = free.Execute(freeAction.Value, expectedId!.Value);
                Record(f, changedTasks);
            }
            if (settings is not null) { p = pomodoro.Configure(settings); Record(p, outcomes, changedTasks); }
            if (suspend) sleeping = true;
            if (resume) sleeping = false;
            var previousDate = snapshot.Today.Date;
            snapshot = CreateSnapshot(p.Snapshot, f.Snapshot);
            if (store is not null && (forceSave || shutdown || suspend || resume || action.HasValue || freeAction.HasValue ||
                settings is not null || selectTask || outcomes.Count > 0 || snapshot.Today.Date != previousDate ||
                clock.GetElapsedTime(lastSaveTimestamp, clock.GetTimestamp()) >= TimeSpan.FromSeconds(5)))
            {
                try
                {
                    Persist(p.Snapshot, f.Snapshot);
                    persistenceError = null;
                    snapshot = snapshot with { PersistenceError = null, CanChangeTask = CanChangeTask(p.Snapshot, f.Snapshot) };
                }
                catch (DataStoreException error)
                {
                    persistenceError = error.Message;
                    p = pomodoro.Suspend(PauseReason.StorageFailure);
                    f = free.Suspend(PauseReason.StorageFailure);
                    Record(p, outcomes, changedTasks);
                    Record(f, changedTasks);
                    snapshot = CreateSnapshot(p.Snapshot, f.Snapshot);
                }
            }
            Schedule();
            update = new(snapshot, outcomes.AsReadOnly(), changedTasks.ToArray());
        }
        // Snapshot consumers may coalesce versions. Task consumers must retain affected IDs and read current totals.
        Changed?.Invoke(this, update);
        return accepted;
    }

    private void Record(PomodoroUpdate update, List<PomodoroStageResult> outcomes, HashSet<Guid> changedTasks)
    {
        // A completion update can already expose the next stage: settle the OLD binding first.
        if (update.Contribution is not null) RecordContribution(update.Contribution, pomodoroBinding, changedTasks);
        foreach (var result in update.FinishedStages)
        {
            outcomes.Add(result);
            dirtyStages[result.StageId] = result;
            if (result.Phase != PomodoroPhase.Focus) continue;
            var changed = ledger.End(result.StageId, result.EndedAtUtc, result.Outcome switch
            {
                StageOutcome.Completed => FocusEndReason.Completed,
                StageOutcome.Skipped => FocusEndReason.Skipped,
                _ => FocusEndReason.Restarted
            });
            if (ledger.GetSession(result.StageId) is not null) dirtySessions.Add(result.StageId);
            if (changed is Guid id) changedTasks.Add(id);
        }
        var p = update.Snapshot;
        if (p.Phase != PomodoroPhase.Focus || p.State is not (PomodoroState.Running or PomodoroState.Paused))
            pomodoroBinding = null;
        else if (pomodoroBinding?.SessionId != p.StageId)
            pomodoroBinding = new(p.StageId, ResolveSelectable(selectedTaskId));
    }

    private void Record(FreeFocusUpdate update, HashSet<Guid> changedTasks)
    {
        if (update.Contribution is not null) RecordContribution(update.Contribution, freeBinding, changedTasks);
        if (update.FinishedSessionId is Guid id)
        {
            ledger.End(id, update.ObservedAtUtc, FocusEndReason.Finished);
            if (ledger.GetSession(id) is not null) dirtySessions.Add(id);
        }
        if (update.Snapshot.State == FreeFocusState.Ready) freeBinding = null;
        else if (freeBinding?.SessionId != update.Snapshot.SessionId)
            freeBinding = new(update.Snapshot.SessionId, ResolveSelectable(selectedTaskId));
    }

    private void RecordContribution(FocusContribution contribution, SessionBinding? binding, HashSet<Guid> changedTasks)
    {
        if (binding?.SessionId != contribution.SessionId)
            throw new InvalidOperationException("Focus contribution has no matching session binding.");
        var attributed = contribution with { TaskId = binding.Task?.Id, TaskTitleAtStart = binding.Task?.TitleAtStart };
        if (!ledger.Record(attributed)) return;
        if (attributed.TaskId is Guid id) changedTasks.Add(id);
        dirtySessions.Add(attributed.SessionId);
        var cursor = attributed.StartedAtUtc;
        var end = cursor + attributed.Duration;
        while (cursor < end)
        {
            dirtyDays.Add(ledger.DateAt(cursor));
            var boundary = ledger.NextDayBoundary(cursor);
            cursor = boundary < end ? boundary : end;
        }
    }

    private FocusTaskReference? ResolveSelectable(Guid? id)
    {
        var task = id is Guid value ? todo?.Find(value) : null;
        return task is { IsDeleted: false, IsCompleted: false } ? new(task.Id, task.Title) : null;
    }

    private static bool CanChangeTask(PomodoroSnapshot p, FreeFocusSnapshot f) =>
        !(p.Phase == PomodoroPhase.Focus && p.State is PomodoroState.Running or PomodoroState.Paused) &&
        f.State == FreeFocusState.Ready;

    private FocusSnapshot CreateSnapshot(PomodoroSnapshot p, FreeFocusSnapshot f)
    {
        var selected = ResolveSelectable(selectedTaskId);
        if (selected is null) selectedTaskId = null;
        return new(++version, p, f, ledger.GetDay(ledger.DateAt(clock.GetUtcNow())), ledger.StatisticsTimeZone.Id,
            selected, pomodoroBinding?.Task, freeBinding?.Task, persistenceError is null && CanChangeTask(p, f), persistenceError);
    }

    private void OnTasksChanged(object? sender, TodoSnapshot update) => Process(forceSave: true);

    private void Persist(PomodoroSnapshot p, FreeFocusSnapshot f)
    {
        if (store is null) return;
        var checkpoint = new FocusCheckpoint(snapshot.Version, pomodoro.Capture(p), free.Capture(f), selectedTaskId,
            pomodoroBinding?.Task, freeBinding?.Task, ledger.StatisticsTimeZone.Id, clock.GetUtcNow());
        store.SaveFocus(checkpoint, dirtySessions.Select(ledger.GetSession).OfType<FocusSession>().ToArray(),
            dirtyDays.Select(ledger.GetDay).ToArray(), dirtyStages.Values.ToArray());
        dirtySessions.Clear();
        dirtyDays.Clear();
        dirtyStages.Clear();
        lastSaveTimestamp = clock.GetTimestamp();
    }

    private void ValidateRestoredState(FocusCheckpoint checkpoint)
    {
        if (checkpoint.Version < 0 || (checkpoint.Pomodoro.Snapshot.State == PomodoroState.Running &&
            checkpoint.Free.Snapshot.State == FreeFocusState.Running))
            throw new DataStoreException("专注检查点的版本或运行状态不一致。");
        foreach (var binding in new[] { pomodoroBinding, freeBinding }.OfType<SessionBinding>())
            if (binding.Task is { } reference && todo?.Find(reference.Id) is null)
                throw new DataStoreException("专注检查点引用了缺失的任务。");
        var p = checkpoint.Pomodoro;
        var f = checkpoint.Free;
        ValidateSession(pomodoroBinding, p.ContributionSequence, p.Snapshot.PlannedDuration - p.Snapshot.Remaining, FocusType.Pomodoro);
        ValidateSession(freeBinding, f.ContributionSequence, f.Snapshot.Elapsed, FocusType.Free);
        foreach (var session in ledger.GetSessions().Where(session => session.EndReason is null))
            if (session.Id != pomodoroBinding?.SessionId && session.Id != freeBinding?.SessionId)
                throw new DataStoreException("未结束的专注记录与检查点不一致。");
    }

    private void ValidateSession(SessionBinding? binding, long sequence, TimeSpan duration, FocusType type)
    {
        if (binding is null) return;
        var session = ledger.GetSession(binding.SessionId);
        if (sequence == 0 && duration == TimeSpan.Zero && session is null) return;
        if (session is null || session.ContributionSequence != sequence || session.ActualDuration != duration ||
            session.Type != type || session.EndReason is not null || session.TaskId != binding.Task?.Id ||
            session.TaskTitleAtStart != binding.Task?.TitleAtStart)
            throw new DataStoreException("专注时长、序号或任务关联与检查点不一致。");
    }

    private void Schedule()
    {
        if (sleeping) { timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); return; }
        var now = clock.GetUtcNow();
        var delay = ledger.NextDayBoundary(now) - now;
        if (snapshot.Free.State == FreeFocusState.Running) delay = Minimum(delay, TimeSpan.FromSeconds(1));
        if (snapshot.Pomodoro.State == PomodoroState.Running)
            delay = Minimum(delay, Minimum(TimeSpan.FromSeconds(1), snapshot.Pomodoro.Remaining));
        // Timer APIs round sub-millisecond waits down; avoid a busy loop at a fractional deadline.
        delay = delay < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : delay;
        timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private static TimeSpan Minimum(TimeSpan a, TimeSpan b) => a < b ? a : b;
    public void Suspend() => Process(suspend: true);
    public void ResumeFromSleep() => Process(resume: true);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
            if (todo is not null) todo.Changed -= OnTasksChanged;
            Changed = null;
        }
    }
}
