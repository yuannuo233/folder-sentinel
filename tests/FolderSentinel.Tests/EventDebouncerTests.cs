using Xunit;
using FolderSentinel.Core.Models;
using FolderSentinel.Core.Monitoring;

namespace FolderSentinel.Tests;

/// <summary>
/// 防抖器单元测试。
/// 防抖器不看真实时钟（nowUtc 由测试传入），所以这些测试完全确定、永远不会“偶发失败”。
/// </summary>
public class EventDebouncerTests
{
    /// <summary>测试用的“起点时间”：2026-01-01 08:00:00 UTC。</summary>
    private static readonly DateTime T0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>造一条“修改”事件（方便测试）。</summary>
    private static FileEventRecord Changed(string path, DateTime timeUtc) => new()
    {
        TimeUtc = timeUtc,
        Kind = FileEventKind.Changed,
        PathNew = path,
        DirPath = @"C:\监控目录"
    };

    [Fact]
    public void 同一文件窗口内的多次修改_合并为一条()
    {
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300));

        // 同一个文件在 300ms 内连发三条修改事件：三条都应该先进窗口等待，不直接发布
        Assert.Empty(debouncer.HandleEvent(Changed("a.txt", T0), T0));
        Assert.Empty(debouncer.HandleEvent(Changed("a.txt", T0.AddMilliseconds(100)), T0.AddMilliseconds(100)));
        Assert.Empty(debouncer.HandleEvent(Changed("a.txt", T0.AddMilliseconds(200)), T0.AddMilliseconds(200)));

        // 300ms 之后（安静满一个窗口）：只发布一条合并结果
        var flushed = debouncer.FlushExpired(T0.AddMilliseconds(600));
        Assert.Single(flushed);
        Assert.Equal(FileEventKind.Changed, flushed[0].Kind);
        Assert.Equal("a.txt", flushed[0].PathNew);
    }

    [Fact]
    public void 合并时保留最早一条的时间戳()
    {
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300));
        debouncer.HandleEvent(Changed("a.txt", T0), T0);
        debouncer.HandleEvent(Changed("a.txt", T0.AddMilliseconds(200)), T0.AddMilliseconds(200));

        var flushed = debouncer.FlushExpired(T0.AddMilliseconds(600));
        Assert.Single(flushed);
        // 发布的这条记录，时间应该是这一串修改最早发生的那一刻
        Assert.Equal(T0, flushed[0].TimeUtc);
    }

    [Fact]
    public void 不同文件的修改事件_各自独立互不影响()
    {
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300));
        debouncer.HandleEvent(Changed("a.txt", T0), T0);
        debouncer.HandleEvent(Changed("b.txt", T0), T0);

        // 两个不同的文件，各自合并出一条，一共发布两条
        var flushed = debouncer.FlushExpired(T0.AddMilliseconds(400));
        Assert.Equal(2, flushed.Count);
    }

    [Fact]
    public void 两次爆发_间隔超过窗口_不合并()
    {
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300));

        // 第一波：T0 时刻的修改，500ms 后发布
        debouncer.HandleEvent(Changed("a.txt", T0), T0);
        Assert.Single(debouncer.FlushExpired(T0.AddMilliseconds(500)));

        // 第二波：T0+1秒 又修改了一次（离第一波已经超过 300ms 窗口），应产生第二条记录
        debouncer.HandleEvent(Changed("a.txt", T0.AddSeconds(1)), T0.AddSeconds(1));
        var second = debouncer.FlushExpired(T0.AddMilliseconds(1400));
        Assert.Single(second);
        Assert.Equal(T0.AddSeconds(1), second[0].TimeUtc);
    }

    [Fact]
    public void 新建和删除事件_立即放行不做防抖()
    {
        var debouncer = new EventDebouncer();

        var created = new FileEventRecord
        {
            TimeUtc = T0,
            Kind = FileEventKind.Created,
            PathNew = @"C:\监控目录\新文件.txt",
            DirPath = @"C:\监控目录"
        };
        Assert.Single(debouncer.HandleEvent(created, T0));   // 直接发布，不等窗口

        var deleted = new FileEventRecord
        {
            TimeUtc = T0,
            Kind = FileEventKind.Deleted,
            PathNew = @"C:\监控目录\新文件.txt",
            DirPath = @"C:\监控目录"
        };
        Assert.Single(debouncer.HandleEvent(deleted, T0));   // 直接发布，不等窗口
    }

    [Fact]
    public void 重命名事件_立即放行不做防抖()
    {
        var debouncer = new EventDebouncer();
        var renamed = new FileEventRecord
        {
            TimeUtc = T0,
            Kind = FileEventKind.Renamed,
            PathOld = @"C:\监控目录\旧.txt",
            PathNew = @"C:\监控目录\新.txt",
            DirPath = @"C:\监控目录"
        };
        Assert.Single(debouncer.HandleEvent(renamed, T0));
    }

    [Fact]
    public void 一直被写入的文件_超过最长等待也必须放行()
    {
        // 窗口 300ms，最长等待 2 秒
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300), maxDelay: TimeSpan.FromSeconds(2));

        // 模拟“日志文件持续被写”：从 T0 开始每 100ms 修改一次，一直写到 T0+1.9s
        for (var i = 0; i < 19; i++)
        {
            var now = T0.AddMilliseconds(i * 100);
            debouncer.HandleEvent(Changed("busy.log", now), now);
        }

        // 期间从未安静满 300ms，所以一直不该发布
        Assert.Empty(debouncer.FlushExpired(T0.AddMilliseconds(1500)));

        // 但到了 T0+2s：超过最长等待，必须放行（不能让高频文件永远占着窗口）
        var flushed = debouncer.FlushExpired(T0.AddSeconds(2));
        Assert.Single(flushed);
        Assert.Equal("busy.log", flushed[0].PathNew);
    }

    [Fact]
    public void 收尾FlushAll_把窗口里剩余的修改全部发布()
    {
        var debouncer = new EventDebouncer(window: TimeSpan.FromMilliseconds(300));
        debouncer.HandleEvent(Changed("a.txt", T0), T0);
        debouncer.HandleEvent(Changed("b.txt", T0), T0);

        // 程序退出前不等窗口了，全部拿出来
        var all = debouncer.FlushAll();
        Assert.Equal(2, all.Count);
        Assert.Empty(debouncer.FlushAll());   // 第二次就没有了
    }
}
