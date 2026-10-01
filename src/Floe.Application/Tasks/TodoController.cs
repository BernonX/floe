using Floe.Domain.Tasks;
using Floe.Application.Storage;

namespace Floe.Application.Tasks;

public sealed record TodoSnapshot(long Version, IReadOnlyList<TodoTask> Tasks, TodoTask? LastDeletedTask);

/// <summary>Owns the local task collection; no WPF, timer or focus binding. Callers receive immutable snapshots.</summary>
public sealed class TodoController
{
    private readonly TimeProvider clock;
    private readonly IAppStore? store;
    private readonly object gate = new();
    private readonly Dictionary<Guid, TodoTask> tasks = [];
    private TodoSnapshot snapshot = new(0, Array.Empty<TodoTask>(), null);
    private Guid? lastDeletedId;

    public TodoController(TimeProvider clock, IAppStore? store = null, StoredAppState? restored = null)
    {
        this.clock = clock;
        this.store = store;
        if (restored is null) return;
        foreach (var task in restored.Tasks) tasks.Add(task.Id, task);
        lastDeletedId = restored.LastDeletedTaskId;
        if (lastDeletedId is Guid id && (!tasks.TryGetValue(id, out var deleted) || !deleted.IsDeleted))
            throw new DataStoreException("删除撤销记录与任务数据不一致。");
        PublishSnapshot();
    }

    public TodoSnapshot Snapshot { get { lock (gate) return snapshot; } }
    public event EventHandler<TodoSnapshot>? Changed;
    public event EventHandler<TodoTask>? Completed;
    public TodoTask? Find(Guid id) { lock (gate) return tasks.GetValueOrDefault(id); }

    public TodoTask Add(string title, int estimatedPomodoros)
    {
        TodoTask task;
        TodoSnapshot update;
        lock (gate)
        {
            task = TodoTask.Create(title, estimatedPomodoros, clock.GetUtcNow());
            store?.SaveTask(task, lastDeletedId);
            tasks.Add(task.Id, task);
            update = PublishSnapshot();
        }
        Changed?.Invoke(this, update);
        return task;
    }

    public void Edit(Guid id, string title, int estimatedPomodoros) => Mutate(id, task => task.Edit(title, estimatedPomodoros, clock.GetUtcNow()));
    public void SetCompleted(Guid id, bool completed) => Mutate(id, task => task.SetCompleted(completed, clock.GetUtcNow()), reportCompletion: completed);

    public void Delete(Guid id)
    {
        TodoSnapshot? update = null;
        lock (gate)
        {
            var task = Require(id);
            if (!task.IsDeleted)
            {
                var deleted = task.Delete(clock.GetUtcNow());
                store?.SaveTask(deleted, id);
                tasks[id] = deleted;
                lastDeletedId = id;
                update = PublishSnapshot();
            }
        }
        if (update is not null) Changed?.Invoke(this, update);
    }

    public bool UndoDelete()
    {
        TodoSnapshot update;
        lock (gate)
        {
            if (lastDeletedId is not Guid id) return false;
            var restored = Require(id).UndoDelete(clock.GetUtcNow());
            store?.SaveTask(restored, null);
            tasks[id] = restored;
            lastDeletedId = null;
            update = PublishSnapshot();
        }
        Changed?.Invoke(this, update);
        return true;
    }

    public int CountCompletedOn(DateOnly date, TimeZoneInfo timeZone)
    {
        lock (gate) return tasks.Values.Count(task => !task.IsDeleted && task.CompletedAtUtc is DateTimeOffset completed &&
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(completed, timeZone).DateTime) == date);
    }

    private void Mutate(Guid id, Func<TodoTask, TodoTask> operation, bool reportCompletion = false)
    {
        TodoSnapshot? update = null;
        TodoTask? completed = null;
        lock (gate)
        {
            var previous = Require(id);
            var next = operation(previous);
            if (next != previous)
            {
                store?.SaveTask(next, lastDeletedId);
                tasks[id] = next;
                update = PublishSnapshot();
                if (reportCompletion && !previous.IsCompleted && next.IsCompleted) completed = next;
            }
        }
        if (update is not null) Changed?.Invoke(this, update);
        if (completed is not null) Completed?.Invoke(this, completed);
    }

    private TodoTask Require(Guid id) => tasks.GetValueOrDefault(id) ?? throw new KeyNotFoundException("找不到此任务。");
    private TodoSnapshot PublishSnapshot() => snapshot = new(snapshot.Version + 1,
        Array.AsReadOnly(tasks.Values.Where(task => !task.IsDeleted).OrderByDescending(task => task.CreatedAtUtc).ThenBy(task => task.Id).ToArray()),
        lastDeletedId is Guid id ? tasks[id] : null);
}
