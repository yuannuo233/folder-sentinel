using FolderSentinel.Core;
using FolderSentinel.Core.Models;
using FolderSentinel.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FolderSentinel.Tests;

/// <summary>
/// SQLite 历史库单元测试：在系统临时目录里建库，验证“存进去 → 读出来”完全一致。
/// 每个测试方法都会拿到一个全新的临时数据库，互不影响，跑完自动清理。
/// </summary>
public class EventDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EventDatabase _db;

    public EventDatabaseTests()
    {
        // xunit 会为每个测试方法新建一个本类实例，所以每个测试都有干净的临时目录
        _tempDir = Path.Combine(Path.GetTempPath(), "FolderSentinelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new EventDatabase(Path.Combine(_tempDir, "test.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // 个别情况下文件句柄释放稍慢，清理失败就留给系统临时目录自动清理
        }
    }

    private static DateTime T1 => new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>造一条事件（方便测试）。</summary>
    private static FileEventRecord MakeEvent(DateTime timeUtc, FileEventKind kind, string path) => new()
    {
        TimeUtc = timeUtc,
        Kind = kind,
        PathNew = path,
        DirPath = @"C:\监控目录"
    };

    [Fact]
    public void 事件存取往返_所有字段完整一致()
    {
        var record = new FileEventRecord
        {
            TimeUtc = new DateTime(2026, 3, 14, 9, 26, 53, 588, DateTimeKind.Utc),
            Kind = FileEventKind.Renamed,
            PathOld = @"C:\a\旧名字.txt",
            PathNew = @"C:\a\新名字.txt",
            DirPath = @"C:\a"
        };
        _db.InsertEvent(record);

        var loaded = Assert.Single(_db.QueryEvents(record.TimeUtc.AddMinutes(-1), record.TimeUtc.AddMinutes(1)));
        Assert.Equal(record.TimeUtc, loaded.TimeUtc);
        Assert.Equal(FileEventKind.Renamed, loaded.Kind);
        Assert.Equal(record.PathOld, loaded.PathOld);
        Assert.Equal(record.PathNew, loaded.PathNew);
        Assert.Equal(record.DirPath, loaded.DirPath);
        // v0.1 进程归属未实现：读出来必须是“空”（界面显示“未知”）
        Assert.Null(loaded.ProcessName);
    }

    [Fact]
    public void 目录增改_重复添加不会出现两行()
    {
        _db.UpsertDirectory(@"C:\监控A");
        _db.UpsertDirectory(@"C:\监控B");
        Assert.Equal(2, _db.GetDirectories().Count);

        // 同一个目录再次 Upsert = 更新启用状态，不是新增一行
        _db.UpsertDirectory(@"C:\监控A", enabled: false);
        var dirs = _db.GetDirectories();
        Assert.Equal(2, dirs.Count);
        Assert.False(dirs.First(d => d.Path == @"C:\监控A").Enabled);
        Assert.True(dirs.First(d => d.Path == @"C:\监控B").Enabled);
    }

    [Fact]
    public void 目录删除_删干净()
    {
        _db.UpsertDirectory(@"C:\监控A");
        _db.RemoveDirectory(@"C:\监控A");
        Assert.Empty(_db.GetDirectories());
    }

    [Fact]
    public void 按时间范围查询_边界包含且按时间倒序()
    {
        var t2 = T1.AddHours(1);
        var t3 = T1.AddHours(2);
        _db.InsertEvent(MakeEvent(T1, FileEventKind.Created, "第一个.txt"));
        _db.InsertEvent(MakeEvent(t2, FileEventKind.Changed, "第二个.txt"));
        _db.InsertEvent(MakeEvent(t3, FileEventKind.Deleted, "第三个.txt"));

        // 查询 T1-1分钟 到 t2+30秒：应该命中前两条；倒序 = 最新的在前
        var result = _db.QueryEvents(T1.AddMinutes(-1), t2.AddSeconds(30));
        Assert.Equal(2, result.Count);
        Assert.Equal(t2, result[0].TimeUtc);
        Assert.Equal(T1, result[1].TimeUtc);
    }

    [Fact]
    public void 按关键字过滤_新旧路径都能搜到()
    {
        _db.InsertEvent(new FileEventRecord
        {
            TimeUtc = T1, Kind = FileEventKind.Created,
            PathNew = @"C:\a\报告.docx", DirPath = @"C:\a"
        });
        _db.InsertEvent(new FileEventRecord
        {
            TimeUtc = T1, Kind = FileEventKind.Created,
            PathNew = @"C:\a\照片.png", DirPath = @"C:\a"
        });
        _db.InsertEvent(new FileEventRecord
        {
            TimeUtc = T1, Kind = FileEventKind.Renamed,
            PathOld = @"C:\a\报告草稿.docx", PathNew = @"C:\a\报告最终版.docx", DirPath = @"C:\a"
        });

        // 关键字“报告”：命中三条里的两条（含重命名事件的旧路径“报告草稿”）
        var result = _db.QueryEvents(T1.AddHours(-1), T1.AddHours(1), keyword: "报告");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void 空关键字_返回范围内全部()
    {
        _db.InsertEvent(MakeEvent(T1, FileEventKind.Created, "a.txt"));
        _db.InsertEvent(MakeEvent(T1, FileEventKind.Created, "b.txt"));

        Assert.Equal(2, _db.QueryEvents(T1.AddHours(-1), T1.AddHours(1), keyword: null).Count);
        Assert.Equal(2, _db.QueryEvents(T1.AddHours(-1), T1.AddHours(1), keyword: "   ").Count);
    }

    [Fact]
    public void 查询条数上限生效()
    {
        for (var i = 0; i < 5; i++)
        {
            _db.InsertEvent(MakeEvent(T1.AddMinutes(i), FileEventKind.Created, $"文件{i}.txt"));
        }
        Assert.Equal(3, _db.QueryEvents(T1.AddMinutes(-1), T1.AddMinutes(10), maxCount: 3).Count);
    }

    [Fact]
    public void 时间戳确实以UTC毫秒整数存储()
    {
        var timeUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.InsertEvent(MakeEvent(timeUtc, FileEventKind.Created, "x.txt"));

        // 用一个“裸”的 SQLite 连接直接看原始数据，验证存的是整数毫秒时间戳
        var builder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_tempDir, "test.db") };
        using var raw = new SqliteConnection(builder.ToString());
        raw.Open();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT ts_utc FROM events";
        var stored = Convert.ToInt64(command.ExecuteScalar());
        Assert.Equal(TimestampHelper.ToUnixMs(timeUtc), stored);
    }

    [Fact]
    public void 建库时表和索引都存在()
    {
        // 用裸连接检查两张表、一个索引是否自动创建
        var builder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_tempDir, "test.db") };
        using var raw = new SqliteConnection(builder.ToString());
        raw.Open();

        Assert.Equal("dirs", Scalar(raw, "SELECT name FROM sqlite_master WHERE type='table' AND name='dirs'"));
        Assert.Equal("events", Scalar(raw, "SELECT name FROM sqlite_master WHERE type='table' AND name='events'"));
        Assert.Equal("idx_events_ts_utc", Scalar(raw, "SELECT name FROM sqlite_master WHERE type='index' AND name='idx_events_ts_utc'"));
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
