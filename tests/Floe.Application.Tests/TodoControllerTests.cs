using Floe.Application.Tasks;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class TodoControllerTests
{
    [Fact]
    public void CrudPublishesSnapshotsWithoutMutatingPreviousVersions()
    {
        var clock = new ManualTimeProvider();
        var controller = new TodoController(clock);
        var empty = controller.Snapshot;
        var task = controller.Add("第一章", 2);
        var added = controller.Snapshot;
        clock.Advance(TimeSpan.FromMinutes(1));
        controller.Edit(task.Id, "第二章", 5);
        Assert.Empty(empty.Tasks);
        Assert.Equal("第一章", Assert.Single(added.Tasks).Title);
        Assert.Equal("第二章", Assert.Single(controller.Snapshot.Tasks).Title);
        Assert.Equal(task.CreatedAtUtc, controller.Find(task.Id)!.CreatedAtUtc);
        controller.Delete(task.Id);
        Assert.Empty(controller.Snapshot.Tasks);
        Assert.True(controller.Find(task.Id)!.IsDeleted);
        Assert.True(controller.UndoDelete());
        Assert.Equal("第二章", Assert.Single(controller.Snapshot.Tasks).Title);
    }

    [Fact]
    public void InvalidOperationsDoNotPublishOrChangeState()
    {
        var controller = new TodoController(new ManualTimeProvider());
        var task = controller.Add("阅读", 2);
        var previous = controller.Snapshot;
        Assert.Throws<ArgumentException>(() => controller.Add(" ", 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Edit(task.Id, "新标题", 100));
        Assert.Throws<KeyNotFoundException>(() => controller.Delete(Guid.NewGuid()));
        Assert.Same(previous, controller.Snapshot);
        Assert.Equal("阅读", controller.Find(task.Id)!.Title);
    }

    [Fact]
    public void RepeatedCompletionAndDeletionDoNotReplaceTheOriginalTimestamp()
    {
        var clock = new ManualTimeProvider();
        var controller = new TodoController(clock);
        var task = controller.Add("阅读", 1);
        controller.SetCompleted(task.Id, true);
        var completed = controller.Find(task.Id)!;
        var version = controller.Snapshot.Version;
        clock.Advance(TimeSpan.FromHours(1));
        controller.SetCompleted(task.Id, true);
        Assert.Equal(version, controller.Snapshot.Version);
        Assert.Equal(completed.CompletedAtUtc, controller.Find(task.Id)!.CompletedAtUtc);
        controller.Delete(task.Id);
        var deleted = controller.Snapshot;
        controller.Delete(task.Id);
        Assert.Same(deleted, controller.Snapshot);
    }

    [Fact]
    public void UndoRestoresOnlyTheLatestDeletionWithItsOriginalStatus()
    {
        var controller = new TodoController(new ManualTimeProvider());
        var first = controller.Add("第一项", 1);
        var second = controller.Add("第二项", 2);
        controller.SetCompleted(second.Id, true);
        controller.Delete(first.Id);
        controller.Delete(second.Id);
        Assert.Equal(second.Id, controller.Snapshot.LastDeletedTask!.Id);
        Assert.True(controller.UndoDelete());
        Assert.True(Assert.Single(controller.Snapshot.Tasks).IsCompleted);
        Assert.True(controller.Find(first.Id)!.IsDeleted);
        Assert.Null(controller.Snapshot.LastDeletedTask);
        Assert.False(controller.UndoDelete());
    }

    [Fact]
    public void DailyCompletionUsesTheStatisticsTimeZoneAndCurrentTaskStatus()
    {
        var clock = new ManualTimeProvider();
        clock.AdjustUtc(new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero) - clock.GetUtcNow());
        var controller = new TodoController(clock);
        var task = controller.Add("阅读", 1);
        controller.SetCompleted(task.Id, true);
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        Assert.Equal(1, controller.CountCompletedOn(new(2026, 10, 2), zone));
        Assert.Equal(0, controller.CountCompletedOn(new(2026, 10, 1), zone));
        controller.Delete(task.Id);
        Assert.Equal(0, controller.CountCompletedOn(new(2026, 10, 2), zone));
        controller.UndoDelete();
        Assert.Equal(1, controller.CountCompletedOn(new(2026, 10, 2), zone));
        controller.SetCompleted(task.Id, false);
        Assert.Equal(0, controller.CountCompletedOn(new(2026, 10, 2), zone));
        clock.Advance(TimeSpan.FromDays(1));
        controller.SetCompleted(task.Id, true);
        Assert.Equal(0, controller.CountCompletedOn(new(2026, 10, 2), zone));
        Assert.Equal(1, controller.CountCompletedOn(new(2026, 10, 3), zone));
    }

    [Fact]
    public void ChangeObserversSeeCommittedStateAndNoOpCommandsStayQuiet()
    {
        var controller = new TodoController(new ManualTimeProvider());
        var versions = new List<long>();
        controller.Changed += (_, update) =>
        {
            versions.Add(update.Version);
            Assert.Same(controller.Snapshot, update);
        };
        var task = controller.Add("阅读", 1);
        controller.Edit(task.Id, " 阅读 ", 1);
        controller.SetCompleted(task.Id, false);
        controller.SetCompleted(task.Id, true);
        controller.SetCompleted(task.Id, true);
        Assert.Equal(new long[] { 1, 2 }, versions);
    }

    [Fact]
    public void ConcurrentChangesKeepAllUniqueTasksAndMonotonicVersions()
    {
        var controller = new TodoController(new ManualTimeProvider());
        Parallel.For(0, 32, index => controller.Add($"任务 {index}", 1));
        Assert.Equal(32, controller.Snapshot.Tasks.Count);
        Assert.Equal(32, controller.Snapshot.Tasks.Select(task => task.Id).Distinct().Count());
        Assert.Equal(32, controller.Snapshot.Version);
    }
}
