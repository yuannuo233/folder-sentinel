using System.Text;
using FolderSentinel.Core.Models;
using Microsoft.Data.Sqlite;

namespace FolderSentinel.Core.Storage;

/// <summary>
/// SQLite 历史库：所有捕获到的文件事件都存进这里，供“按天回看”查询。
///
/// 两条铁律：
/// 1. 【参数化查询】所有 SQL 一律使用 @xxx 占位符传值，绝不把用户输入直接拼进
///    SQL 字符串——字符串拼接 SQL 是经典安全漏洞（SQL 注入），本项目零容忍；
/// 2. 【UTC 时间】数据库里只存 Unix 毫秒时间戳（UTC），显示层负责转本地时间。
///
/// 线程说明：整个类用一把锁串行化所有数据库操作（监控入库和界面查询都走这里），
/// SQLite 的连接不能多线程共用，加锁是最简单可靠的做法。
/// </summary>
public sealed class EventDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public EventDatabase(string databaseFilePath)
    {
        // 确保数据库所在目录存在（通常是程序的 data 目录）
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFilePath))!);

        // 用连接字符串构造器拼连接串，避免手工拼接出错
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        // WAL 模式：写库时读库不互相卡，程序意外崩溃后数据也更安全
        ExecuteRaw("PRAGMA journal_mode=WAL;");

        InitializeTables();
    }

    /// <summary>建表（已存在就跳过）。dirs = 监控目录表；events = 文件事件表。</summary>
    private void InitializeTables()
    {
        ExecuteRaw("""
            CREATE TABLE IF NOT EXISTS dirs (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                path    TEXT    NOT NULL UNIQUE,
                enabled INTEGER NOT NULL DEFAULT 1
            );
            """);

        ExecuteRaw("""
            CREATE TABLE IF NOT EXISTS events (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                ts_utc       INTEGER NOT NULL,
                kind         TEXT    NOT NULL,
                path_old     TEXT,
                path_new     TEXT,
                dir_path     TEXT,
                process_name TEXT
            );
            """);

        // 给“时间”列建索引：按时间范围查询（按天回看）才够快
        ExecuteRaw("CREATE INDEX IF NOT EXISTS idx_events_ts_utc ON events(ts_utc);");
    }

    // ==================== 监控目录管理 ====================

    /// <summary>
    /// 添加一个监控目录；如果目录已经存在，就更新它的启用状态（相当于“添加或更新”）。
    /// </summary>
    public void UpsertDirectory(string path, bool enabled = true)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dirs (path, enabled) VALUES (@path, @enabled)
                ON CONFLICT(path) DO UPDATE SET enabled = @enabled
                """;
            command.Parameters.AddWithValue("@path", NormalizeDirectory(path));
            command.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>删除一个监控目录的记录。</summary>
    public void RemoveDirectory(string path)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM dirs WHERE path = @path";
            command.Parameters.AddWithValue("@path", NormalizeDirectory(path));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>读取全部监控目录（按路径排序，保证界面显示顺序稳定）。</summary>
    public List<WatchedDirectory> GetDirectories()
    {
        lock (_lock)
        {
            var result = new List<WatchedDirectory>();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, path, enabled FROM dirs ORDER BY path";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new WatchedDirectory
                {
                    Id = reader.GetInt64(0),
                    Path = reader.GetString(1),
                    Enabled = reader.GetInt64(2) != 0
                });
            }
            return result;
        }
    }

    // ==================== 事件读写 ====================

    /// <summary>写入一条文件事件记录。</summary>
    public void InsertEvent(FileEventRecord record)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO events (ts_utc, kind, path_old, path_new, dir_path, process_name)
                VALUES (@ts, @kind, @pathOld, @pathNew, @dirPath, @processName)
                """;
            command.Parameters.AddWithValue("@ts", TimestampHelper.ToUnixMs(record.TimeUtc));
            command.Parameters.AddWithValue("@kind", record.Kind.ToStorageName());
            command.Parameters.AddWithValue("@pathOld", NullIfEmpty(record.PathOld));
            command.Parameters.AddWithValue("@pathNew", NullIfEmpty(record.PathNew));
            command.Parameters.AddWithValue("@dirPath", NullIfEmpty(record.DirPath));
            // v0.1：进程归属暂未实现，process_name 存 NULL，界面显示“未知”。
            // TODO(v0.2)：改用 ETW（Microsoft-Windows-Kernel-File）记录真实进程名。
            command.Parameters.AddWithValue("@processName", (object?)record.ProcessName ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 按时间范围 + 关键字查询历史事件（结果按时间倒序，最新的在前）。
    /// </summary>
    /// <param name="fromUtc">开始时间（含）</param>
    /// <param name="toUtc">结束时间（含）</param>
    /// <param name="keyword">路径关键字，可空；空则不加这个条件</param>
    /// <param name="maxCount">最多返回多少条（防止一次查询拉爆内存）</param>
    public List<FileEventRecord> QueryEvents(DateTime fromUtc, DateTime toUtc, string? keyword = null, int maxCount = 2000)
    {
        lock (_lock)
        {
            // SQL 的固定骨架在这里拼好；所有用户输入都通过 @参数 传入，绝不拼接
            var sql = new StringBuilder(
                "SELECT ts_utc, kind, path_old, path_new, dir_path, process_name FROM events");
            sql.Append(" WHERE ts_utc >= @from AND ts_utc <= @to");
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql.Append(" AND (path_new LIKE @kw OR path_old LIKE @kw)");
            }
            sql.Append(" ORDER BY ts_utc DESC LIMIT @max");

            using var command = _connection.CreateCommand();
            command.CommandText = sql.ToString();
            command.Parameters.AddWithValue("@from", TimestampHelper.ToUnixMs(fromUtc));
            command.Parameters.AddWithValue("@to", TimestampHelper.ToUnixMs(toUtc));
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                command.Parameters.AddWithValue("@kw", $"%{keyword.Trim()}%");
            }
            command.Parameters.AddWithValue("@max", maxCount);

            var result = new List<FileEventRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new FileEventRecord
                {
                    TimeUtc = TimestampHelper.FromUnixMs(reader.GetInt64(0)),
                    Kind = FileEventKindExtensions.FromStorageName(reader.GetString(1)),
                    PathOld = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PathNew = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DirPath = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ProcessName = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return result;
        }
    }

    /// <summary>历史事件总条数（状态栏展示用）。</summary>
    public long CountEvents()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM events";
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
    }

    // ==================== 内部小工具 ====================

    /// <summary>空字符串统一存 NULL，数据更规整。</summary>
    private static object NullIfEmpty(string text)
    {
        return string.IsNullOrEmpty(text) ? DBNull.Value : text;
    }

    /// <summary>
    /// 目录路径标准化：转成完整路径、去掉末尾多余的分隔符
    /// （根目录如 C:\ 除外），保证同一个目录在数据库里只有一种写法。
    /// </summary>
    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3 &&
            (full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            full = full[..^1];
        }
        return full;
    }

    /// <summary>执行一条不返回数据的 SQL（建表、PRAGMA 等程序自己写死的语句）。</summary>
    private void ExecuteRaw(string sql)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }
}
