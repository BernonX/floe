using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class TaskBindingTests
{
    [Fact]
    public void PauseResumeKeepsTaskAndRestartUnlocksNewAttempt()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var a = todo.Add("A", 2);
        var b = todo.Add("B", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        Assert.True(focus.SelectTask(a.Id));
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(3));
        focus.Execute(PomodoroAction.Pause, focus.Current.StageId);
        Assert.False(focus.SelectTask(b.Id));
        Assert.False(focus.SelectTask(null));
        clock.Advance(TimeSpan.FromHours(1));
        focus.Execute(PomodoroAction.Resume, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.Execute(PomodoroAction.Restart, focus.Current.StageId);
        Assert.True(focus.SelectTask(b.Id));
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(4));
        focus.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(5), focus.GetTaskStatistics(a.Id).TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), focus.GetTaskStatistics(b.Id).TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(9), focus.Snapshot.Today.TotalDuration);
        Assert.Equal(FocusEndReason.Restarted, focus.GetSessions().Single(s => s.TaskId == a.Id).EndReason);
    }

    [Fact]
    public void AutoTransitionsSettleOldTaskBeforeCapturingNextTask()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var a = todo.Add("A", 1);
        var b = todo.Add("B", 1);
        using var focus = new FocusController(clock, new()
        {
            FocusDuration = TimeSpan.FromSeconds(2), ShortBreakDuration = TimeSpan.FromSeconds(1),
            AutoStartBreak = true, AutoStartFocus = true
        }, TimeZoneInfo.Utc, todo);
        focus.SelectTask(a.Id);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.Refresh();
        Assert.Equal(PomodoroPhase.ShortBreak, focus.Current.Phase);
        Assert.True(focus.SelectTask(b.Id));
        clock.Advance(TimeSpan.FromSeconds(1));
        focus.Refresh();
        Assert.Equal(b.Id, focus.Snapshot.PomodoroTask?.Id);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.Refresh();
        Assert.Equal(1, focus.GetTaskStatistics(a.Id).CompletedPomodoros);
        Assert.Equal(1, focus.GetTaskStatistics(b.Id).CompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(4), focus.Snapshot.Today.TotalDuration);
        Assert.All(focus.GetSessions(), session => Assert.Equal(TimeSpan.FromSeconds(2), session.ActualDuration));
    }

    [Fact]
    public void RenameAndDeletePreserveCurrentBindingAndUndoRestoresProgress()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("原名", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(2));
        todo.Edit(task.Id, "新名", 2);
        todo.Delete(task.Id);
        Assert.Null(focus.Snapshot.SelectedTask);
        Assert.Equal(task.Id, focus.Snapshot.FreeTask?.Id);
        clock.Advance(TimeSpan.FromSeconds(3));
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        Assert.True(todo.UndoDelete());
        Assert.Equal(TimeSpan.FromSeconds(5), focus.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Equal("原名", Assert.Single(focus.GetSessions()).TaskTitleAtStart);
        Assert.Equal("新名", todo.Find(task.Id)?.Title);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(1));
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        Assert.Single(focus.GetSessions(), s => s.TaskId is null);
    }

    [Fact]
    public void CompletingTaskDoesNotStopCurrentSessionOrBindFutureSessions()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(2) }, TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        todo.SetCompleted(task.Id, true);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.Refresh();
        Assert.Equal(1, focus.GetTaskStatistics(task.Id).CompletedPomodoros);
        Assert.Null(focus.Snapshot.SelectedTask);
        Assert.False(focus.SelectTask(task.Id));
        todo.SetCompleted(task.Id, false);
        Assert.True(focus.SelectTask(task.Id));
    }

    [Fact]
    public void FreeFinishAndUnboundRestartRetainTotalsWithoutCreatingZeroSessions()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        Assert.Empty(focus.GetSessions());
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.ExecuteFree(FreeFocusAction.Pause, focus.Snapshot.Free.SessionId);
        Assert.False(focus.SelectTask(null));
        clock.Advance(TimeSpan.FromSeconds(30));
        focus.ExecuteFree(FreeFocusAction.Resume, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(3));
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        Assert.True(focus.SelectTask(null));
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(4));
        focus.Execute(PomodoroAction.Restart, focus.Current.StageId);
        Assert.Equal(TimeSpan.FromSeconds(5), focus.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Equal(0, focus.GetTaskStatistics(task.Id).CompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(9), focus.Snapshot.Today.TotalDuration);
    }

    [Fact]
    public void PausedPomodoroAndFreeSessionRetainIndependentCapturedBindings()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.Execute(PomodoroAction.Pause, focus.Current.StageId);
        todo.SetCompleted(task.Id, true); // Clears only the choice for future sessions.
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(3));
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        focus.Execute(PomodoroAction.Resume, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(4));
        focus.Execute(PomodoroAction.Pause, focus.Current.StageId);
        Assert.Equal(TimeSpan.FromSeconds(6), focus.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(9), focus.Snapshot.Today.TotalDuration);
        Assert.Equal(task.Id, focus.Snapshot.PomodoroTask?.Id);
    }

    [Fact]
    public void MidnightSplitsLiveTaskAndGlobalTimeTogether()
    {
        var clock = new ManualTimeProvider();
        clock.AdjustUtc(new DateTimeOffset(2026, 10, 4, 23, 59, 58, TimeSpan.Zero) - clock.GetUtcNow());
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(5));
        focus.Refresh();
        var stats = focus.GetTaskStatistics(task.Id);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), stats.TodayDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), stats.ThisWeekDuration);
        Assert.Equal(stats.TodayDuration, focus.Snapshot.Today.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), focus.GetDay(new(2026, 10, 4)).TotalDuration);
    }

    [Fact]
    public void ConcurrentDeadlineCommandsCompleteBoundTaskOnlyOnce()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(1) }, TimeZoneInfo.Utc, todo);
        focus.SelectTask(task.Id);
        var id = focus.Current.StageId;
        Parallel.For(0, 24, _ => focus.Execute(PomodoroAction.Start, id));
        clock.Advance(TimeSpan.FromSeconds(1));
        Parallel.For(0, 24, _ => focus.Execute(PomodoroAction.Skip, id));
        Assert.Equal(1, focus.GetTaskStatistics(task.Id).CompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(1), focus.GetTaskStatistics(task.Id).TotalDuration);
        Assert.Single(focus.GetSessions());
    }

    [Fact]
    public void ConcurrentSelectionAndStartCaptureOnlyTheAcceptedChoice()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        Assert.False(focus.SelectTask(Guid.NewGuid()));
        var accepted = false;
        Parallel.Invoke(() => accepted = focus.SelectTask(task.Id),
            () => focus.Execute(PomodoroAction.Start, focus.Current.StageId));
        clock.Advance(TimeSpan.FromSeconds(1));
        focus.Refresh();
        Assert.Equal(accepted ? task.Id : (Guid?)null, Assert.Single(focus.GetSessions()).TaskId);
        Assert.Equal(TimeSpan.FromSeconds(1), focus.Snapshot.Today.TotalDuration);
    }
}
