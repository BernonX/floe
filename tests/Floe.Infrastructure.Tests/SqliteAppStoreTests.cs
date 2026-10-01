using Floe.Application.Storage;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Application.Statistics;
using Floe.Application.SystemIntegration;
using Floe.Domain.Widgets;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Infrastructure.Storage;
using Floe.TestSupport;
using Microsoft.Data.Sqlite;

namespace Floe.Infrastructure.Tests;

public sealed class SqliteAppStoreTests
{
    [Fact]
    public void LegacyWidgetJsonDefaultsToExpandedAndCompactRoundTripsWithOtherSettings()
    {
        using var directory = new TestDirectory(); string path;
        using (var store = new SqliteAppStore(directory.Path)) { path = store.DatabasePath; store.SaveWidgetSettings(new(true, false, true, 0.75, -300, 50)); }
        // A real Phase 10 record has no IsCompact field. No schema reset or migration is required.
        using (var edit = OpenDatabase(path)) Execute(edit, "UPDATE widget_settings SET json='{\"IsVisible\":true,\"IsTopmost\":false,\"IsPositionLocked\":true,\"Opacity\":0.75,\"LeftPixels\":-300,\"TopPixels\":50,\"LayoutId\":\"focus\"}';");
        using (var reopened = new SqliteAppStore(directory.Path))
        {
            var legacy = reopened.Load().WidgetSettings!;
            Assert.False(legacy.IsCompact);
            reopened.SaveWidgetSettings(legacy with { IsCompact = true });
            reopened.SaveAppSettings(new(AppTheme.Dark, false, true));
            reopened.SaveSystemSettings(new(false, false));
        }
        using var verified = new SqliteAppStore(directory.Path);
        Assert.Equal(new WidgetSettings(true, false, true, 0.75, -300, 50, IsCompact: true), verified.Load().WidgetSettings);
    }

    [Fact]
    public void VersionThreeMigrationPreservesHistoryAndIndependentSettings()
    {
        using var directory = new TestDirectory();
        string path;
        using (var store = new SqliteAppStore(directory.Path))
        {
            path = store.DatabasePath;
            var clock = new ManualTimeProvider(); var todo = new TodoController(clock, store);
            todo.Add("升级后保留", 2);
            using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromSeconds(5)); focus.PrepareForShutdown();
            store.SaveWidgetSettings(new(true, false, true, 0.77)); store.SaveAppSettings(new(AppTheme.Dark));
        }
        using (var old = OpenDatabase(path)) Execute(old, "DROP TABLE system_settings; PRAGMA user_version=3;");
        var expected = new SystemSettings(false, true, false, true);
        using (var migrated = new SqliteAppStore(directory.Path))
        {
            Assert.Equal(new SystemSettings(), migrated.Load().SystemSettings);
            migrated.SaveSystemSettings(expected);
            migrated.SaveAppSettings(new(AppTheme.Light));
            migrated.SaveWidgetSettings(new(true, true, false, 0.8));
        }
        using var reopened = new SqliteAppStore(directory.Path);
        var restored = reopened.Load();
        Assert.Equal(expected, restored.SystemSettings);
        Assert.Equal(AppTheme.Light, restored.AppSettings.Theme);
        Assert.Equal(0.8, restored.WidgetSettings!.Opacity);
        Assert.Equal("升级后保留", Assert.Single(restored.Tasks).Title);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(restored.Sessions).ActualDuration);
    }

    [Fact]
    public void VersionTwoMigratesWithoutChangingFocusDataAndWidgetRoundTripsIndependently()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTimeProvider();
        string path;
        using (var store = new SqliteAppStore(directory.Path))
        {
            path = store.DatabasePath;
            var todo = new TodoController(clock, store);
            todo.Add("保留历史", 3);
            using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromSeconds(3));
            focus.PrepareForShutdown();
        }
        using (var old = OpenDatabase(path)) Execute(old, "DROP TABLE system_settings; DROP TABLE widget_settings; PRAGMA user_version=2;");
        var expected = new WidgetSettings(true, false, true, 0.75, -880, 150);
        using (var migrated = new SqliteAppStore(directory.Path))
        {
            Assert.Single(migrated.Load().Tasks);
            Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(migrated.Load().Sessions).ActualDuration);
            Assert.Equal(new WidgetSettings(), migrated.Load().WidgetSettings);
            migrated.SaveWidgetSettings(expected);
            migrated.SaveAppSettings(new(AppTheme.Dark));
        }
        using var reopened = new SqliteAppStore(directory.Path);
        var restored = reopened.Load();
        Assert.Equal(expected, restored.WidgetSettings);
        Assert.Equal(AppTheme.Dark, restored.AppSettings.Theme);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(restored.Sessions).ActualDuration);
    }

    [Fact]
    public void CorruptedWidgetSettingsAreKeptAndRejected()
    {
        using var directory = new TestDirectory();
        string path;
        using (var store = new SqliteAppStore(directory.Path))
        {
            path = store.DatabasePath;
            store.SaveWidgetSettings(new());
        }
        using (var edit = OpenDatabase(path)) Execute(edit, "UPDATE widget_settings SET json='{\"Opacity\":0.01}';");
        using var reopened = new SqliteAppStore(directory.Path);
        Assert.Throws<DataStoreException>(reopened.Load);
        using var verify = OpenDatabase(path);
        Assert.Equal(1L, Scalar(verify, "SELECT count(*) FROM widget_settings;"));
    }

    [Fact]
    public void TaskIdentityLifecycleUndoAndQuotedTitlesRoundTrip()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTimeProvider();
        Guid id;
        using (var store = new SqliteAppStore(directory.Path))
        {
            var todo = new TodoController(clock, store);
            var task = todo.Add("复习 'SQL'; DROP TABLE todo_tasks; --", 5);
            id = task.Id;
            clock.Advance(TimeSpan.FromMinutes(1));
            todo.SetCompleted(id, true);
            todo.Delete(id);
            store.SaveAppSettings(new(AppTheme.Dark, true, true));
        }
        using var reopened = new SqliteAppStore(directory.Path);
        var saved = reopened.Load();
        var restored = new TodoController(clock, reopened, saved);
        Assert.Empty(restored.Snapshot.Tasks);
        Assert.Equal(id, saved.LastDeletedTaskId);
        Assert.True(restored.UndoDelete());
        var taskAfter = Assert.Single(restored.Snapshot.Tasks);
        Assert.Equal(id, taskAfter.Id);
        Assert.True(taskAfter.IsCompleted);
        Assert.Contains("DROP TABLE", taskAfter.Title);
        Assert.Equal(new AppSettings(AppTheme.Dark, true, true), saved.AppSettings);
    }

    [Fact]
    public void NaturalCompletionFreeTimeAndDailyCachesRoundTripWithoutDuplicates()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTimeProvider();
        Guid taskId;
        using (var store = new SqliteAppStore(directory.Path))
        {
            var todo = new TodoController(clock, store);
            taskId = todo.Add("学习", 2).Id;
            using var focus = new FocusController(clock, new() { FocusDuration = TimeSpan.FromSeconds(2) }, TimeZoneInfo.Utc, todo, store);
            focus.SelectTask(taskId);
            focus.Execute(PomodoroAction.Start, focus.Current.StageId);
            clock.Advance(TimeSpan.FromSeconds(2));
            focus.Refresh();
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromSeconds(3));
            focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
            focus.PrepareForShutdown();
            var saved = store.Load();
            var ledger = new FocusLedger(TimeZoneInfo.Utc);
            ledger.Restore(saved.Sessions);
            store.SaveFocus(saved.Checkpoint!, saved.Sessions, ledger.GetDays().ToArray());
            store.SaveFocus(saved.Checkpoint!, saved.Sessions, ledger.GetDays().ToArray());
        }
        using var reopened = new SqliteAppStore(directory.Path);
        var state = reopened.Load();
        var restoredTodo = new TodoController(clock, reopened, state);
        using var restored = new FocusController(clock, state.PomodoroSettings, TimeZoneInfo.Utc, restoredTodo, reopened, state);
        Assert.Equal(TimeSpan.FromSeconds(5), restored.Snapshot.Today.TotalDuration);
        Assert.Equal(1, restored.GetTaskStatistics(taskId).CompletedPomodoros);
        Assert.Equal(2, restored.GetSessions().Count);
        Assert.Equal(0, restored.Current.CompletedFocusesThisRun);
        var statistics = new StatisticsService(restored, restoredTodo).GetSnapshot(30);
        Assert.Equal(1, statistics.Focus.TodayCompletedPomodoros);
        Assert.Equal(TimeSpan.FromSeconds(5), statistics.Focus.TotalDuration);
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.FromSeconds(5).Ticks / 30), statistics.Focus.DailyAverage);
        using var read = OpenDatabase(reopened.DatabasePath);
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, Scalar(read, "SELECT pomodoro_ticks+free_ticks FROM daily_focus_records;"));
        Assert.Equal(1L, Scalar(read, "SELECT count(*) FROM pomodoro_stages;"));
    }

    [Fact]
    public void CrashRestoresPausedSameSessionAndContinuesItsSequence()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTimeProvider();
        Guid id;
        using (var store = new SqliteAppStore(directory.Path))
        {
            var todo = new TodoController(clock, store);
            using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            id = focus.Snapshot.Free.SessionId;
            for (var i = 0; i < 7; i++) { clock.Advance(TimeSpan.FromSeconds(1)); focus.Refresh(); }
        }
        clock.Advance(TimeSpan.FromDays(2));
        using var reopened = new SqliteAppStore(directory.Path);
        var saved = reopened.Load();
        var todoAgain = new TodoController(clock, reopened, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings, TimeZoneInfo.Utc, todoAgain, reopened, saved);
        Assert.Equal(id, restored.Snapshot.Free.SessionId);
        Assert.Equal(FreeFocusState.Paused, restored.Snapshot.Free.State);
        Assert.Equal(TimeSpan.FromSeconds(5), restored.Snapshot.Free.Elapsed);
        restored.ExecuteFree(FreeFocusAction.Resume, id);
        clock.Advance(TimeSpan.FromSeconds(2));
        restored.ExecuteFree(FreeFocusAction.Finish, id);
        var session = Assert.Single(reopened.Load().Sessions);
        Assert.Equal(TimeSpan.FromSeconds(7), session.ActualDuration);
        Assert.Equal(6L, session.ContributionSequence);
        Assert.Equal(2, session.Intervals.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), restored.Snapshot.Today.TotalDuration);
    }

    [Fact]
    public void FailedForeignKeyRollsBackSessionAndCheckpointTogether()
    {
        using var directory = new TestDirectory();
        using var store = new SqliteAppStore(directory.Path);
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
        var before = store.Load().Checkpoint!;
        var invalid = new FocusSession(Guid.NewGuid(), FocusType.Free, clock.GetUtcNow(), null,
            TimeSpan.FromSeconds(1), null, [new(clock.GetUtcNow(), TimeSpan.FromSeconds(1))], Guid.NewGuid(), "缺失任务", 1);
        Assert.Throws<DataStoreException>(() => store.SaveFocus(before with { Version = before.Version + 1 }, [invalid], []));
        var after = store.Load();
        Assert.Equal(before, after.Checkpoint);
        Assert.Empty(after.Sessions);
    }

    [Fact]
    public void StaleCheckpointAndChangedTaskAttributionCannotOverwriteHistory()
    {
        using var directory = new TestDirectory();
        using var store = new SqliteAppStore(directory.Path);
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock, store);
        var task = todo.Add("学习", 1);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
        focus.SelectTask(task.Id);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        var old = store.Load().Checkpoint!;
        clock.Advance(TimeSpan.FromSeconds(2));
        focus.ExecuteFree(FreeFocusAction.Finish, focus.Snapshot.Free.SessionId);
        var saved = store.Load();
        Assert.Throws<DataStoreException>(() => store.SaveFocus(old, [], []));
        Assert.Throws<DataStoreException>(() => store.SaveFocus(saved.Checkpoint!,
            [Assert.Single(saved.Sessions) with { TaskId = null, TaskTitleAtStart = null }], []));
        Assert.Equal(task.Id, Assert.Single(store.Load().Sessions).TaskId);
    }

    [Fact]
    public void SchemaMigrationPreservesExistingTasksAndNewerSchemaIsRejected()
    {
        using var directory = new TestDirectory();
        string path;
        using (var store = new SqliteAppStore(directory.Path))
        {
            path = store.DatabasePath;
            store.SaveTask(Floe.Domain.Tasks.TodoTask.Create("保留", 1, DateTimeOffset.UtcNow), null);
        }
        using (var edit = OpenDatabase(path)) Execute(edit, "DROP TABLE system_settings; DROP TABLE widget_settings; DROP TABLE pomodoro_stages; PRAGMA user_version=1;");

        using (var migrated = new SqliteAppStore(directory.Path)) Assert.Single(migrated.Load().Tasks);
        using (var read = OpenDatabase(path))
        {
            Assert.Equal(SqliteAppStore.SchemaVersion, Scalar(read, "PRAGMA user_version;"));
            Execute(read, "PRAGMA user_version=999;");
        }
        Assert.Throws<DataStoreException>(() => new SqliteAppStore(directory.Path));
        using var verify = OpenDatabase(path);
        Assert.Equal(1L, Scalar(verify, "SELECT count(*) FROM todo_tasks;"));
    }

    [Fact]
    public void InstanceLockPreventsTwoWritersAndReleasesOnDispose()
    {
        using var directory = new TestDirectory();
        using (var first = new SqliteAppStore(directory.Path))
            Assert.Throws<DataStoreException>(() => new SqliteAppStore(directory.Path));
        using var second = new SqliteAppStore(directory.Path);
        Assert.Empty(second.Load().Tasks);
    }

    [Fact]
    public void CorruptDatabaseIsKeptRatherThanRecreated()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "floe.db");
        byte[] bytes = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, bytes);
        Assert.Throws<DataStoreException>(() => new SqliteAppStore(directory.Path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void LockedDatabasePausesAccountingAndRetryCommitsTheRetainedDelta()
    {
        using var directory = new TestDirectory();
        using var store = new SqliteAppStore(directory.Path);
        var clock = new ManualTimeProvider();
        var todo = new TodoController(clock, store);
        using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
        focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
        using var blocker = OpenDatabase(store.DatabasePath);
        using (var transaction = blocker.BeginTransaction())
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            focus.Refresh();
            Assert.NotNull(focus.Snapshot.PersistenceError);
            Assert.Equal(FreeFocusState.Paused, focus.Snapshot.Free.State);
            Assert.Empty(store.Load().Sessions); // WAL still permits reads.
            transaction.Rollback();
        }
        focus.RetrySave();
        Assert.Null(focus.Snapshot.PersistenceError);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(store.Load().Sessions).ActualDuration);
    }

    [Fact]
    public void MidnightAndMondayCachesUseThePersistedStatisticsZone()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTimeProvider();
        clock.AdjustUtc(new DateTimeOffset(2026, 10, 4, 23, 59, 58, TimeSpan.Zero) - clock.GetUtcNow());
        Guid taskId;
        using (var store = new SqliteAppStore(directory.Path))
        {
            var todo = new TodoController(clock, store);
            taskId = todo.Add("跨周", 1).Id;
            using var focus = new FocusController(clock, new(), TimeZoneInfo.Utc, todo, store);
            focus.SelectTask(taskId);
            focus.ExecuteFree(FreeFocusAction.Start, focus.Snapshot.Free.SessionId);
            clock.Advance(TimeSpan.FromSeconds(5));
            focus.PrepareForShutdown();
        }
        using var reopened = new SqliteAppStore(directory.Path);
        var saved = reopened.Load();
        var restoredTodo = new TodoController(clock, reopened, saved);
        using var restored = new FocusController(clock, saved.PomodoroSettings,
            TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"), restoredTodo, reopened, saved);
        Assert.Equal(TimeZoneInfo.Utc.Id, restored.Snapshot.StatisticsTimeZoneId);
        var stats = restored.GetTaskStatistics(taskId);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), stats.TodayDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), stats.ThisWeekDuration);
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }

    [Fact]
    public void MissingCheckpointFieldsAreRejectedInsteadOfRestoringDefaults()
    {
        using var directory = new TestDirectory();
        using var store = new SqliteAppStore(directory.Path);
        using var edit = OpenDatabase(store.DatabasePath);
        Execute(edit, "INSERT INTO runtime_checkpoint VALUES(1,1,'{}');");
        Assert.Throws<DataStoreException>(() => store.Load());
        Assert.Equal(1L, Scalar(edit, "SELECT count(*) FROM runtime_checkpoint;"));
    }
    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private sealed class TestDirectory : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data");
        public string Path { get; }
        public TestDirectory()
        {
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (!Path.StartsWith(System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its private directory.");
            Directory.Delete(Path, recursive: true);
        }
    }
}
