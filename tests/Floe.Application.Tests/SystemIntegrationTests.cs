using System.Collections.Concurrent;
using Floe.Application.Storage;
using Floe.Application.SystemIntegration;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class SystemIntegrationTests
{
    private sealed class Sink : ICompletionNotificationSink
    {
        public ConcurrentQueue<(CompletionNotice Notice, bool Notification, bool Sound)> Notices { get; } = new();
        public void Deliver(CompletionNotice notice, bool showNotification, bool playSound) => Notices.Enqueue((notice, showNotification, playSound));
    }

    [Fact]
    public void NaturalFocusAndBreakNotifyOnceWhileSkipAndRestartStayQuiet()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(2), ShortBreakDuration = TimeSpan.FromSeconds(1), AutoStartBreak = true });
        var sink = new Sink();
        using var reminders = new CompletionNotifications(focus, todo, new(), sink);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        Parallel.For(0, 20, _ => focus.Refresh());
        Assert.Equal("专注结束", Assert.Single(sink.Notices).Notice.Title);
        clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); focus.Refresh();
        Assert.Equal(new[] { "专注结束", "休息结束" }, sink.Notices.Select(item => item.Notice.Title));
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        focus.Execute(PomodoroAction.Restart, focus.Current.StageId);
        focus.Execute(PomodoroAction.Skip, focus.Current.StageId);
        Assert.Equal(2, sink.Notices.Count);
    }

    [Fact]
    public void CompletionWaitsForSuccessfulRetryAndDoesNotRepeat()
    {
        var clock = new ManualTimeProvider(); var store = new TestAppStore(); var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(1) }, todo: todo, store: store);
        var sink = new Sink(); using var reminders = new CompletionNotifications(focus, todo, new(), sink);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        store.FailWrites = true; clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh();
        Assert.NotNull(focus.Snapshot.PersistenceError); Assert.Empty(sink.Notices);
        focus.RetrySave(); Assert.Empty(sink.Notices);
        store.FailWrites = false; focus.RetrySave(); focus.RetrySave(); focus.Refresh();
        Assert.Equal("专注结束", Assert.Single(sink.Notices).Notice.Title);
    }

    [Fact]
    public void TaskCompletionNotifiesOnlyAfterCommitAndUndoDoesNotReplay()
    {
        var clock = new ManualTimeProvider(); var store = new TestAppStore(); var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new()); var sink = new Sink();
        using var reminders = new CompletionNotifications(focus, todo, new(), sink);
        var task = todo.Add("阅读", 2);
        store.FailWrites = true;
        Assert.Throws<DataStoreException>(() => todo.SetCompleted(task.Id, true)); Assert.Empty(sink.Notices);
        store.FailWrites = false; todo.SetCompleted(task.Id, true); todo.SetCompleted(task.Id, true);
        todo.Edit(task.Id, "阅读完毕", 3); todo.Delete(task.Id); todo.UndoDelete();
        Assert.Equal("阅读", Assert.Single(sink.Notices).Notice.Message);
        todo.SetCompleted(task.Id, false); clock.Advance(TimeSpan.FromSeconds(1)); todo.SetCompleted(task.Id, true);
        Assert.Equal(2, sink.Notices.Count);
    }

    [Fact]
    public void RestoredCompletedHistoryDoesNotGenerateReminders()
    {
        var clock = new ManualTimeProvider(); var store = new TestAppStore(); var todo = new TodoController(clock, store);
        todo.SetCompleted(todo.Add("已完成", 1).Id, true);
        using (var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(1) }, todo: todo, store: store))
        {
            focus.Execute(PomodoroAction.Start, focus.Current.StageId); clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); focus.PrepareForShutdown();
        }
        var restored = store.Load(); var tasks = new TodoController(clock, store, restored);
        using var recovered = new FocusController(clock, restored.PomodoroSettings, todo: tasks, store: store, restored: restored);
        var sink = new Sink(); using var reminders = new CompletionNotifications(recovered, tasks, new(), sink);
        recovered.Refresh(); Assert.Empty(sink.Notices);
    }

    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, true, 1)] [InlineData(true, false, 1)] [InlineData(true, true, 1)]
    public void NotificationAndSoundPreferencesAreIndependent(bool notification, bool sound, int count)
    {
        var clock = new ManualTimeProvider(); var todo = new TodoController(clock); using var focus = new FocusController(clock, new());
        var settings = new SystemSettingsController(restored: new(notification, sound)); var sink = new Sink();
        using var reminders = new CompletionNotifications(focus, todo, settings, sink);
        reminders.TestReminder(); Assert.Equal(count, sink.Notices.Count);
        if (count > 0) { var item = Assert.Single(sink.Notices); Assert.Equal(notification, item.Notification); Assert.Equal(sound, item.Sound); }
    }

    [Fact]
    public void FailedPreferenceSaveKeepsCommittedValuesAndEmitsNoChange()
    {
        var store = new TestAppStore { FailWrites = true }; var controller = new SystemSettingsController(store);
        var count = 0; controller.Changed += (_, _) => count++;
        Assert.Throws<DataStoreException>(() => controller.Update(s => s with { CloseToTray = false }));
        Assert.True(controller.Settings.CloseToTray); Assert.Equal(0, count);
        store.FailWrites = false; controller.Update(s => s with { CloseToTray = false });
        Assert.False(store.Load().SystemSettings!.CloseToTray); Assert.Equal(1, count);
    }

    [Fact]
    public void DisposalRemovesSubscriptions()
    {
        var clock = new ManualTimeProvider(); var todo = new TodoController(clock); using var focus = new FocusController(clock, new());
        var sink = new Sink(); var reminders = new CompletionNotifications(focus, todo, new(), sink); reminders.Dispose();
        todo.SetCompleted(todo.Add("不会提醒", 0).Id, true); Assert.Empty(sink.Notices);
    }
}
