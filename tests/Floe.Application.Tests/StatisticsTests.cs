using Floe.Application.Statistics;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.TestSupport;

namespace Floe.Application.Tests;

public sealed class StatisticsTests
{
    [Fact]
    public void WindowsIncludeTodayFillGapsAndExcludeOlderRecords()
    {
        var clock = new ManualTimeProvider();
        var today = clock.GetUtcNow();
        var todo = new TodoController(clock);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        var service = new StatisticsService(focus, todo);
        foreach (var (offset, minutes) in new[] { (-30, 120), (-29, 60), (-7, 30), (-6, 14), (0, 7) })
        {
            clock.AdjustUtc(today.AddDays(offset) - clock.GetUtcNow());
            focus.Refresh();
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromMinutes(minutes));
            focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        }
        var week = service.GetSnapshot(7).Focus;
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(DateOnly.FromDateTime(today.Date).AddDays(-6), week.Days[0].Date);
        Assert.Equal(TimeSpan.FromMinutes(21), week.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(3), week.DailyAverage);
        Assert.Equal(5, week.Days.Count(day => day.TotalDuration == TimeSpan.Zero));
        var month = service.GetSnapshot(30).Focus;
        Assert.Equal(30, month.Days.Count);
        Assert.Equal(TimeSpan.FromMinutes(111), month.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(222), month.DailyAverage);
        Assert.Equal(0, month.TodayCompletedPomodoros);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetSnapshot(8));
    }

    [Fact]
    public void BothModesAccumulateButBreaksPausesAndSkippedPomodorosDoNotComplete()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(4) }, TimeZoneInfo.Utc, todo);
        var service = new StatisticsService(focus, todo);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(4));
        focus.Refresh();
        var breakId = focus.Current.StageId;
        focus.Execute(PomodoroAction.Start, breakId);
        clock.Advance(TimeSpan.FromSeconds(10));
        focus.Execute(PomodoroAction.Skip, breakId);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(1));
        focus.Execute(PomodoroAction.Skip, focus.Current.StageId);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromSeconds(3));
        focus.ExecuteFree(FreeFocusAction.Pause, focus.Snapshot.Free.SessionId);
        clock.Advance(TimeSpan.FromHours(1));
        focus.Refresh();
        var stats = service.GetSnapshot(7).Focus;
        Assert.Equal(1, stats.TodayCompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.Days[^1].PomodoroDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), stats.Days[^1].FreeDuration);
        Assert.Equal(TimeSpan.FromSeconds(8), stats.TotalDuration);
    }

    [Theory]
    [InlineData("UTC", 23)]
    [InlineData("China Standard Time", 15)]
    public void MidnightSplitsDurationAndCompletionBelongsToEndDate(string timeZoneId, int utcHour)
    {
        var clock = new ManualTimeProvider();
        clock.AdjustUtc(new DateTimeOffset(2026, 9, 30, utcHour, 59, 58, TimeSpan.Zero) - clock.GetUtcNow());
        var todo = new TodoController(clock);
        using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(4) }, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), todo);
        var task = todo.Add("复习", 1);
        todo.SetCompleted(task.Id, true);
        var service = new StatisticsService(focus, todo);
        Assert.Equal(1, service.GetSnapshot(7).TodayCompletedTasks);
        focus.Execute(PomodoroAction.Start, focus.Current.StageId);
        clock.Advance(TimeSpan.FromSeconds(4));
        focus.Refresh();
        var stats = service.GetSnapshot(7);
        Assert.Equal(new DateOnly(2026, 10, 1), stats.Focus.Today);
        Assert.Equal(1, stats.Focus.TodayCompletedPomodoros);
        Assert.Equal(0, stats.TodayCompletedTasks);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.Focus.Days[^2].TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.Focus.Days[^1].TotalDuration);
    }

    [Fact]
    public void TaskCompletionRestoreDeletionAndUndoRefreshCounts()
    {
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo);
        var service = new StatisticsService(focus, todo);
        var task = todo.Add("阅读", 2);
        Assert.Equal(0, service.GetSnapshot(7).TodayCompletedTasks);
        todo.SetCompleted(task.Id, true);
        Assert.Equal(1, service.GetSnapshot(7).TodayCompletedTasks);
        todo.Delete(task.Id);
        Assert.Equal(0, service.GetSnapshot(7).TodayCompletedTasks);
        todo.UndoDelete();
        Assert.Equal(1, service.GetSnapshot(7).TodayCompletedTasks);
        todo.SetCompleted(task.Id, false);
        Assert.Equal(0, service.GetSnapshot(7).TodayCompletedTasks);
    }

    [Fact]
    public void CompletionCountSurvivesRestartWhileRunCounterResets()
    {
        var clock = new ManualTimeProvider();
        using var store = new TestAppStore();
        var todo = new TodoController(clock, store);
        using (var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(2) }, TimeZoneInfo.Utc, todo, store))
        {
            focus.Execute(PomodoroAction.Start, focus.Current.StageId);
            clock.Advance(TimeSpan.FromSeconds(2));
            focus.PrepareForShutdown();
        }
        var saved = store.Load();
        var restoredTodo = new TodoController(clock, store, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, restoredTodo, store, saved);
        var stats = new StatisticsService(restored, restoredTodo).GetSnapshot(30);
        Assert.Equal(0, restored.Current.CompletedFocusesThisRun);
        Assert.Equal(1, stats.Focus.TodayCompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.Focus.TotalDuration);
        restored.Refresh();
        Assert.Equal(1, restored.GetStatistics(7).TodayCompletedPomodoros);
    }
}
