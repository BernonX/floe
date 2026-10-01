using System.Text.Json;
using Floe.Application.Storage;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Domain.Tasks;
using Floe.Domain.Widgets;
using Floe.Application.SystemIntegration;
using Microsoft.Data.Sqlite;

namespace Floe.Infrastructure.Storage;

/// <summary>A single serialized connection. Every focus commit includes its recovery checkpoint.</summary>
public sealed class SqliteAppStore : IAppStore
{
    public const int SchemaVersion = 4;
    private readonly object gate = new();
    private readonly FileStream instanceLock;
    private readonly SqliteConnection connection;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };
    private bool disposed;
    public string DatabasePath { get; }

    public SqliteAppStore(string dataDirectory)
    {
        FileStream? acquiredLock = null;
        SqliteConnection? opened = null;
        try
        {
            dataDirectory = Path.GetFullPath(dataDirectory);
            Directory.CreateDirectory(dataDirectory);
            DatabasePath = Path.Combine(dataDirectory, "floe.db");
            acquiredLock = new FileStream(Path.Combine(dataDirectory, "floe.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            opened = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false, ForeignKeys = true, DefaultTimeout = 2
            }.ToString());
            opened.Open();
            instanceLock = acquiredLock;
            connection = opened;
            InitializeSchema();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or DataStoreException)
        {
            opened?.Dispose();
            acquiredLock?.Dispose();
            if (error is DataStoreException) throw;
            throw new DataStoreException("无法打开本地数据。请检查目录权限、可用空间，以及是否已有 Floe 正在使用该目录。原数据库不会被重置。", error);
        }
    }

    private void InitializeSchema()
    {
        using var versionCommand = Command("PRAGMA user_version;");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > SchemaVersion) throw new DataStoreException("此数据库由更新版本的 Floe 创建，请使用对应版本打开。");
        using var wal = Command("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
        wal.ExecuteNonQuery();
        if (version == 0)
        {
            using var transaction = connection.BeginTransaction();
            using var schema = Command("""
                CREATE TABLE todo_tasks (
                    id TEXT PRIMARY KEY, title TEXT NOT NULL, created_utc_ticks INTEGER NOT NULL,
                    updated_utc_ticks INTEGER NOT NULL, completed_utc_ticks INTEGER, deleted_utc_ticks INTEGER,
                    estimated_pomodoros INTEGER NOT NULL CHECK(estimated_pomodoros BETWEEN 0 AND 99));
                CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE focus_sessions (
                    id TEXT PRIMARY KEY, type INTEGER NOT NULL CHECK(type IN (0,1)), started_utc_ticks INTEGER NOT NULL,
                    ended_utc_ticks INTEGER, duration_ticks INTEGER NOT NULL CHECK(duration_ticks>0),
                    end_reason INTEGER, sequence INTEGER NOT NULL CHECK(sequence>0),
                    task_id TEXT REFERENCES todo_tasks(id) ON DELETE RESTRICT, task_title_at_start TEXT);
                CREATE INDEX ix_focus_task_start ON focus_sessions(task_id, started_utc_ticks);
                CREATE TABLE focus_intervals (
                    session_id TEXT NOT NULL REFERENCES focus_sessions(id) ON DELETE CASCADE,
                    ordinal INTEGER NOT NULL, started_utc_ticks INTEGER NOT NULL,
                    duration_ticks INTEGER NOT NULL CHECK(duration_ticks>0), PRIMARY KEY(session_id, ordinal));
                CREATE INDEX ix_focus_interval_start ON focus_intervals(started_utc_ticks);
                CREATE TABLE daily_focus_records (
                    date TEXT NOT NULL, time_zone_id TEXT NOT NULL, pomodoro_ticks INTEGER NOT NULL,
                    free_ticks INTEGER NOT NULL, PRIMARY KEY(date, time_zone_id));
                CREATE TABLE pomodoro_settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                CREATE TABLE app_settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                CREATE TABLE runtime_checkpoint (id INTEGER PRIMARY KEY CHECK(id=1), version INTEGER NOT NULL, json TEXT NOT NULL);
                PRAGMA user_version=1;
                """, transaction);
            schema.ExecuteNonQuery();
            transaction.Commit();
            version = 1;
        }
        if (version == 1)
        {
            using var transaction = connection.BeginTransaction();
            using var migration = Command("""
                CREATE TABLE pomodoro_stages (
                    stage_id TEXT PRIMARY KEY, cycle_id TEXT NOT NULL, stage_index INTEGER NOT NULL,
                    attempt_number INTEGER NOT NULL, phase INTEGER NOT NULL, outcome INTEGER NOT NULL,
                    planned_ticks INTEGER NOT NULL, actual_ticks INTEGER NOT NULL,
                    started_utc_ticks INTEGER, ended_utc_ticks INTEGER NOT NULL);
                CREATE INDEX ix_pomodoro_cycle ON pomodoro_stages(cycle_id,stage_index,attempt_number);
                PRAGMA user_version=2;
                """, transaction);
            migration.ExecuteNonQuery();
            transaction.Commit();
            version = 2;
        }
        if (version == 2)
        {
            using var transaction = connection.BeginTransaction();
            using var migration = Command("CREATE TABLE widget_settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL); PRAGMA user_version=3;", transaction);
            migration.ExecuteNonQuery();
            transaction.Commit();
            version = 3;
        }
        if (version == 3)
        {
            using var transaction = connection.BeginTransaction();
            using var migration = Command("CREATE TABLE system_settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL); PRAGMA user_version=4;", transaction);
            migration.ExecuteNonQuery();
            transaction.Commit();
        }
        using var check = Command("PRAGMA quick_check;");
        if (Convert.ToString(check.ExecuteScalar()) != "ok") throw new DataStoreException("数据库完整性检查未通过。原数据已保留。");
    }

    public StoredAppState Load() => Read<StoredAppState>(() =>
    {
        var tasks = new List<TodoTask>();
        using (var command = Command("SELECT id,title,estimated_pomodoros,created_utc_ticks,updated_utc_ticks,completed_utc_ticks,deleted_utc_ticks FROM todo_tasks;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tasks.Add(TodoTask.Restore(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2),
                Utc(reader.GetInt64(3)), Utc(reader.GetInt64(4)), OptionalUtc(reader, 5), OptionalUtc(reader, 6)));

        var intervals = new Dictionary<Guid, List<FocusInterval>>();
        using (var command = Command("SELECT session_id,started_utc_ticks,duration_ticks FROM focus_intervals ORDER BY session_id,ordinal;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var id = Guid.Parse(reader.GetString(0));
                if (!intervals.TryGetValue(id, out var list)) intervals[id] = list = [];
                list.Add(new(Utc(reader.GetInt64(1)), TimeSpan.FromTicks(reader.GetInt64(2))));
            }
        var sessions = new List<FocusSession>();
        using (var command = Command("SELECT id,type,started_utc_ticks,ended_utc_ticks,duration_ticks,end_reason,task_id,task_title_at_start,sequence FROM focus_sessions ORDER BY started_utc_ticks,id;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var id = Guid.Parse(reader.GetString(0));
                sessions.Add(new(id, (FocusType)reader.GetInt32(1), Utc(reader.GetInt64(2)), OptionalUtc(reader, 3),
                    TimeSpan.FromTicks(reader.GetInt64(4)), reader.IsDBNull(5) ? null : (FocusEndReason)reader.GetInt32(5),
                    intervals.GetValueOrDefault(id)?.ToArray() ?? [],
                    reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8)));
            }
        using var deleted = Command("SELECT value FROM metadata WHERE key='last_deleted_task_id';");
        var deletedValue = deleted.ExecuteScalar() as string;
        var settings = ReadJson<PomodoroSettings>("pomodoro_settings") ?? new();
        settings.Validate();
        var appearance = ReadJson<AppSettings>("app_settings") ?? new();
        if (!Enum.IsDefined(appearance.Theme)) throw new DataStoreException("外观设置包含未知主题。");
        var widget = ReadJson<WidgetSettings>("widget_settings") ?? new();
        widget.Validate();
        return new(tasks.AsReadOnly(), deletedValue is null ? null : Guid.Parse(deletedValue),
            sessions.AsReadOnly(), settings, appearance, ReadJson<FocusCheckpoint>("runtime_checkpoint"), widget,
            ReadJson<SystemSettings>("system_settings") ?? new());
    });

    public void SaveTask(TodoTask task, Guid? lastDeletedTaskId) => Write(transaction =>
    {
        using var command = Command("""
            INSERT INTO todo_tasks VALUES($id,$title,$created,$updated,$completed,$deleted,$estimate)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,updated_utc_ticks=excluded.updated_utc_ticks,
                completed_utc_ticks=excluded.completed_utc_ticks,deleted_utc_ticks=excluded.deleted_utc_ticks,
                estimated_pomodoros=excluded.estimated_pomodoros;
            """, transaction);
        Add(command, "$id", Id(task.Id)); Add(command, "$title", task.Title);
        Add(command, "$created", task.CreatedAtUtc.UtcTicks); Add(command, "$updated", task.UpdatedAtUtc.UtcTicks);
        Add(command, "$completed", task.CompletedAtUtc?.UtcTicks); Add(command, "$deleted", task.DeletedAtUtc?.UtcTicks);
        Add(command, "$estimate", task.EstimatedPomodoros);
        command.ExecuteNonQuery();
        using var metadata = Command("INSERT INTO metadata VALUES('last_deleted_task_id',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;", transaction);
        Add(metadata, "$value", lastDeletedTaskId is Guid id ? Id(id) : null);
        metadata.ExecuteNonQuery();
    });

    public void SaveFocus(FocusCheckpoint checkpoint, IReadOnlyList<FocusSession> changedSessions,
        IReadOnlyList<DailyFocusRecord> changedDays, IReadOnlyList<PomodoroStageResult>? finishedStages = null) => Write(transaction =>
    {
        using var revision = Command("SELECT version FROM runtime_checkpoint WHERE id=1;", transaction);
        if (revision.ExecuteScalar() is long storedVersion && storedVersion > checkpoint.Version)
            throw new DataStoreException("拒绝保存过期的专注检查点。");
        foreach (var session in changedSessions) SaveSession(session, transaction);
        foreach (var stage in finishedStages ?? [])
        {
            using var command = Command("INSERT INTO pomodoro_stages VALUES($id,$cycle,$index,$attempt,$phase,$outcome,$planned,$actual,$start,$end) ON CONFLICT(stage_id) DO NOTHING;", transaction);
            Add(command, "$id", Id(stage.StageId)); Add(command, "$cycle", Id(stage.CycleId));
            Add(command, "$index", stage.StageIndex); Add(command, "$attempt", stage.AttemptNumber);
            Add(command, "$phase", (int)stage.Phase); Add(command, "$outcome", (int)stage.Outcome);
            Add(command, "$planned", stage.PlannedDuration.Ticks); Add(command, "$actual", stage.ActualElapsed.Ticks);
            Add(command, "$start", stage.StartedAtUtc?.UtcTicks); Add(command, "$end", stage.EndedAtUtc.UtcTicks);
            command.ExecuteNonQuery();
        }
        foreach (var day in changedDays)
        {
            using var command = Command("""
                INSERT INTO daily_focus_records VALUES($date,$zone,$pomodoro,$free)
                ON CONFLICT(date,time_zone_id) DO UPDATE SET pomodoro_ticks=excluded.pomodoro_ticks,free_ticks=excluded.free_ticks;
                """, transaction);
            Add(command, "$date", day.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Add(command, "$zone", checkpoint.StatisticsTimeZoneId);
            Add(command, "$pomodoro", day.PomodoroDuration.Ticks); Add(command, "$free", day.FreeDuration.Ticks);
            command.ExecuteNonQuery();
        }
        WriteJson("pomodoro_settings", checkpoint.Pomodoro.ConfiguredSettings, transaction);
        using var save = Command("INSERT INTO runtime_checkpoint VALUES(1,$version,$json) ON CONFLICT(id) DO UPDATE SET version=excluded.version,json=excluded.json;", transaction);
        Add(save, "$version", checkpoint.Version); Add(save, "$json", JsonSerializer.Serialize(checkpoint));
        save.ExecuteNonQuery();
    });

    private void SaveSession(FocusSession session, SqliteTransaction transaction)
    {
        using (var existing = Command("SELECT type,started_utc_ticks,sequence,task_id,task_title_at_start,duration_ticks,end_reason FROM focus_sessions WHERE id=$id;", transaction))
        {
            Add(existing, "$id", Id(session.Id));
            using var reader = existing.ExecuteReader();
            if (reader.Read() && (reader.GetInt32(0) != (int)session.Type || reader.GetInt64(1) != session.StartedAtUtc.UtcTicks ||
                reader.GetInt64(2) > session.ContributionSequence || reader.GetInt64(5) > session.ActualDuration.Ticks ||
                (reader.IsDBNull(3) ? null : reader.GetString(3)) != (session.TaskId is Guid taskId ? Id(taskId) : null) ||
                (reader.IsDBNull(4) ? null : reader.GetString(4)) != session.TaskTitleAtStart ||
                (!reader.IsDBNull(6) && (session.EndReason is null || reader.GetInt32(6) != (int)session.EndReason))))
                throw new DataStoreException("拒绝改写专注记录的身份、任务归属或已保存进度。");
        }
        using var command = Command("""
            INSERT INTO focus_sessions VALUES($id,$type,$start,$end,$duration,$reason,$sequence,$task,$title)
            ON CONFLICT(id) DO UPDATE SET ended_utc_ticks=excluded.ended_utc_ticks,duration_ticks=excluded.duration_ticks,
                end_reason=excluded.end_reason,sequence=excluded.sequence;
            """, transaction);
        Add(command, "$id", Id(session.Id)); Add(command, "$type", (int)session.Type);
        Add(command, "$start", session.StartedAtUtc.UtcTicks); Add(command, "$end", session.EndedAtUtc?.UtcTicks);
        Add(command, "$duration", session.ActualDuration.Ticks); Add(command, "$reason", session.EndReason is { } reason ? (int)reason : null);
        Add(command, "$sequence", session.ContributionSequence); Add(command, "$task", session.TaskId is Guid task ? Id(task) : null);
        Add(command, "$title", session.TaskTitleAtStart);
        command.ExecuteNonQuery();
        using var remove = Command("DELETE FROM focus_intervals WHERE session_id=$id;", transaction);
        Add(remove, "$id", Id(session.Id)); remove.ExecuteNonQuery();
        for (var i = 0; i < session.Intervals.Count; i++)
        {
            var interval = session.Intervals[i];
            using var insert = Command("INSERT INTO focus_intervals VALUES($id,$ordinal,$start,$duration);", transaction);
            Add(insert, "$id", Id(session.Id)); Add(insert, "$ordinal", i);
            Add(insert, "$start", interval.StartedAtUtc.UtcTicks); Add(insert, "$duration", interval.Duration.Ticks);
            insert.ExecuteNonQuery();
        }
    }

    public void SaveAppSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)) throw new ArgumentOutOfRangeException(nameof(settings));
        Write(transaction => WriteJson("app_settings", settings, transaction));
    }

    public void SaveWidgetSettings(WidgetSettings settings)
    {
        settings.Validate();
        Write(transaction => WriteJson("widget_settings", settings, transaction));
    }

    public void SaveSystemSettings(SystemSettings settings) => Write(transaction => WriteJson("system_settings", settings, transaction));

    private void WriteJson<T>(string table, T value, SqliteTransaction transaction)
    {
        // Table names are internal constants, never user input. All data is parameterized.
        using var command = Command($"INSERT INTO {table} VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json;", transaction);
        Add(command, "$json", JsonSerializer.Serialize(value)); command.ExecuteNonQuery();
    }
    private T? ReadJson<T>(string table) where T : class
    {
        using var command = Command($"SELECT json FROM {table} WHERE id=1;");
        return command.ExecuteScalar() is string value
            ? JsonSerializer.Deserialize<T>(value, JsonOptions) ?? throw new DataStoreException("本地设置或检查点为空。") : null;
    }
    private T Read<T>(Func<T> read)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try { return read(); }
            catch (Exception error) when (error is SqliteException or JsonException or ArgumentException or InvalidOperationException or FormatException)
            { throw new DataStoreException("读取本地数据失败。请保留数据库，勿用空数据覆盖。", error); }
        }
    }
    private void Write(Action<SqliteTransaction> write)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                using var transaction = connection.BeginTransaction();
                write(transaction);
                transaction.Commit();
            }
            catch (SqliteException error) { throw new DataStoreException("本地保存失败。请检查可用空间和目录权限后重试。", error); }
        }
    }
    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }
    private static void Add(SqliteCommand command, string key, object? value) => command.Parameters.AddWithValue(key, value ?? DBNull.Value);
    private static string Id(Guid id) => id.ToString("N");
    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
    private static DateTimeOffset? OptionalUtc(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : Utc(reader.GetInt64(column));

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            connection.Dispose();
            instanceLock.Dispose();
        }
    }
}
