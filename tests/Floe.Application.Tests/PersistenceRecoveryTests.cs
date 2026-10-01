using Floe.Application.Storage;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class PersistenceRecoveryTests
{
    [Fact]
    public void CrashRecoversLastAtomicCheckpointAndDoesNotCreditOfflineTime()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        var task = todo.Add("学习", 2);
        using (var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store))
        {
            focus.SelectTask(task.Id);
            focus.Execute(PomodoroAction.Start, focus.Current.StageId);
            for (var i = 0; i < 8; i++) { clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); }
            Assert.Equal(TimeSpan.FromSeconds(8), focus.Snapshot.Today.TotalDuration);
        } // Abrupt stop: deliberately omit PrepareForShutdown.
        clock.Advance(TimeSpan.FromHours(8));
        var saved = store.Load();
        var restoredTodo = new TodoController(clock, store, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, restoredTodo, store, saved);
        Assert.Equal(PomodoroState.Paused, restored.Current.State);
        Assert.Equal(PauseReason.AppRestart, restored.Current.PauseReason);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(5), restored.Current.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(5), restored.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Equal(task.Id, restored.Snapshot.PomodoroTask?.Id);
        restored.Execute(PomodoroAction.Resume, restored.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        restored.Execute(PomodoroAction.Pause, restored.Current.StageId);
        Assert.Equal(TimeSpan.FromSeconds(7), restored.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Single(restored.GetSessions());
    }

    [Fact]
    public void NormalShutdownKeepsSubsecondFreeTimeAndResumeUsesNextSequence()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        var task = todo.Add("学习", 1);
        Guid id;
        using (var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store))
        {
            focus.SelectTask(task.Id);
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            id = focus.Snapshot.Free.SessionId;
            clock.Advance(TimeSpan.FromMilliseconds(1250));
            Assert.True(focus.PrepareForShutdown());
        }
        clock.Advance(TimeSpan.FromDays(1));
        var saved = store.Load();
        var restoredTodo = new TodoController(clock, store, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, restoredTodo, store, saved);
        Assert.Equal(id, restored.Snapshot.Free.SessionId);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), restored.Snapshot.Free.Elapsed);
        Assert.Equal(TimeSpan.Zero, restored.Snapshot.Today.TotalDuration);
        restored.ExecuteFree(FreeFocusAction.Resume, id);
        clock.Advance(TimeSpan.FromMilliseconds(750));
        restored.ExecuteFree(FreeFocusAction.Finish, id);
        Assert.Equal(TimeSpan.FromSeconds(2), restored.GetTaskStatistics(task.Id).TotalDuration);
        var session = Assert.Single(restored.GetSessions());
        Assert.Equal(2, session.ContributionSequence);
        Assert.Equal(2, session.Intervals.Count);
        Assert.Equal(FocusEndReason.Finished, session.EndReason);
    }

    [Fact]
    public void PendingConfigurationAndZeroTimeBindingSurviveRestart()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        var task = todo.Add("学习", 1);
        var desired = new PomodoroSettings { FocusDuration = TimeSpan.FromMinutes(40), FocusesPerCycle = 2 };
        using (var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store))
        {
            focus.SelectTask(task.Id);
            focus.Execute(PomodoroAction.Start, focus.Current.StageId);
            focus.Configure(desired);
            Assert.Empty(focus.GetSessions());
        }
        var saved = store.Load();
        var restoredTodo = new TodoController(clock, store, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, restoredTodo, store, saved);
        Assert.Equal(desired, restored.ConfiguredSettings);
        Assert.Equal(TimeSpan.FromMinutes(25), restored.Current.PlannedDuration);
        Assert.True(restored.Current.HasPendingSettings);
        Assert.Equal(task.Id, restored.Snapshot.PomodoroTask?.Id);
    }

    [Fact]
    public void StorageFailurePausesTimerAndRetryPreservesUncommittedTime()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        store.FailWrites = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        focus.Refresh();
        Assert.NotNull(focus.Snapshot.PersistenceError);
        Assert.Equal(FreeFocusState.Paused, focus.Snapshot.Free.State);
        Assert.Equal(PauseReason.StorageFailure, focus.Snapshot.Free.PauseReason);
        Assert.False(focus.PrepareForShutdown());
        focus.ExecuteFree(FreeFocusAction.Resume, focus.Snapshot.Free.SessionId);
        Assert.Equal(FreeFocusState.Paused, focus.Snapshot.Free.State);
        clock.Advance(TimeSpan.FromHours(1));
        store.FailWrites = false;
        focus.RetrySave();
        Assert.Null(focus.Snapshot.PersistenceError);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(store.Load().Sessions).ActualDuration);
        Assert.Equal(FreeFocusState.Paused, focus.Snapshot.Free.State);
    }

    [Fact]
    public void FailedTaskWriteDoesNotPublishOrAlterLifecycle()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        var task = todo.Add("原名", 2);
        var version = todo.Snapshot.Version;
        store.FailWrites = true;
        Assert.Throws<DataStoreException>(() => todo.Edit(task.Id, "新名", 3));
        Assert.Throws<DataStoreException>(() => todo.Delete(task.Id));
        Assert.Throws<DataStoreException>(() => todo.Add("失败", 1));
        Assert.Equal(version, todo.Snapshot.Version);
        Assert.Equal(task, todo.Find(task.Id));
        Assert.Single(todo.Snapshot.Tasks);
    }

    [Fact]
    public void IdleDoesNotWriteEverySecondAndMergedIntervalsKeepOriginalSequence()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
        var initial = store.FocusWriteCount;
        for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); }
        Assert.Equal(initial, store.FocusWriteCount);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        for (var i = 0; i < 10; i++) { clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); }
        Assert.Equal(initial + 3, store.FocusWriteCount); // Start and two checkpoints.
        var session = Assert.Single(store.Load().Sessions);
        Assert.Equal(10, session.ContributionSequence);
        Assert.Single(session.Intervals);
    }

    [Fact]
    public void CorruptCheckpointIsRejectedBeforeAnyOverwrite()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        using (var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store))
        {
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromSeconds(2));
            focus.ExecuteFree(FreeFocusAction.Pause, focus.Snapshot.Free.SessionId);
        }
        var saved = store.Load();
        var corrupt = saved with { Checkpoint = saved.Checkpoint! with
        { Free = saved.Checkpoint.Free with { Snapshot = saved.Checkpoint.Free.Snapshot with { Elapsed = TimeSpan.FromSeconds(9) } } } };
        var writes = store.FocusWriteCount;
        Assert.Throws<DataStoreException>(() => new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, todo, store, corrupt));
        Assert.Equal(writes, store.FocusWriteCount);
    }
}
