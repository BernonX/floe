namespace Floe.Domain.Tasks;

/// <summary>Immutable task identity and lifecycle; focus progress is a projection of the session ledger.</summary>
public sealed record TodoTask
{
    public const int MaximumTitleLength = 120;
    public const int MaximumEstimatedPomodoros = 99;

    public Guid Id { get; private init; }
    public string Title { get; private init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset UpdatedAtUtc { get; private init; }
    public DateTimeOffset? CompletedAtUtc { get; private init; }
    public DateTimeOffset? DeletedAtUtc { get; private init; }
    public bool IsCompleted => CompletedAtUtc.HasValue;
    public bool IsDeleted => DeletedAtUtc.HasValue;
    public int EstimatedPomodoros { get; private init; }
    public int CompletedPomodoros { get; private init; }
    public TimeSpan TotalFocusTime { get; private init; }

    private TodoTask() { }

    public static string? ValidateTitle(string? title)
    {
        var normalized = title?.Trim();
        if (string.IsNullOrEmpty(normalized)) return "请输入任务标题。";
        if (normalized.Length > MaximumTitleLength) return $"标题最多 {MaximumTitleLength} 个字符。";
        if (normalized.Any(char.IsControl)) return "任务标题请使用单行文字。";
        return null;
    }

    public static TodoTask Create(string title, int estimatedPomodoros, DateTimeOffset now)
    {
        Validate(title, estimatedPomodoros);
        return new() { Id = Guid.NewGuid(), Title = title.Trim(), EstimatedPomodoros = estimatedPomodoros,
            CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    public static TodoTask Restore(Guid id, string title, int estimatedPomodoros, DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc, DateTimeOffset? completedAtUtc, DateTimeOffset? deletedAtUtc)
    {
        Validate(title, estimatedPomodoros);
        if (id == Guid.Empty) throw new ArgumentException("Task ID cannot be empty.", nameof(id));
        return new() { Id = id, Title = title.Trim(), EstimatedPomodoros = estimatedPomodoros,
            CreatedAtUtc = createdAtUtc, UpdatedAtUtc = updatedAtUtc,
            CompletedAtUtc = completedAtUtc, DeletedAtUtc = deletedAtUtc };
    }

    public TodoTask Edit(string title, int estimatedPomodoros, DateTimeOffset now)
    {
        EnsureVisible();
        Validate(title, estimatedPomodoros);
        if (Title == title.Trim() && EstimatedPomodoros == estimatedPomodoros) return this;
        return this with { Title = title.Trim(), EstimatedPomodoros = estimatedPomodoros, UpdatedAtUtc = now };
    }

    public TodoTask SetCompleted(bool completed, DateTimeOffset now)
    {
        EnsureVisible();
        if (IsCompleted == completed) return this;
        return this with { CompletedAtUtc = completed ? now : null, UpdatedAtUtc = now };
    }

    public TodoTask Delete(DateTimeOffset now) => IsDeleted ? this : this with { DeletedAtUtc = now, UpdatedAtUtc = now };
    public TodoTask UndoDelete(DateTimeOffset now) => !IsDeleted ? this : this with { DeletedAtUtc = null, UpdatedAtUtc = now };

    public TodoTask WithFocusProgress(int completedPomodoros, TimeSpan totalFocusTime)
    {
        if (completedPomodoros < 0 || totalFocusTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(completedPomodoros));
        return this with { CompletedPomodoros = completedPomodoros, TotalFocusTime = totalFocusTime };
    }

    private static void Validate(string title, int estimatedPomodoros)
    {
        if (ValidateTitle(title) is string error) throw new ArgumentException(error, nameof(title));
        if (estimatedPomodoros is < 0 or > MaximumEstimatedPomodoros)
            throw new ArgumentOutOfRangeException(nameof(estimatedPomodoros), "预计番茄数量必须在 0–99 之间。");
    }

    private void EnsureVisible()
    {
        if (IsDeleted) throw new InvalidOperationException("已删除的任务不可编辑或改变完成状态。");
    }
}
