using FolderSentinel.Core;
using FolderSentinel.Core.Monitoring;
using FolderSentinel.Core.Storage;

namespace FolderSentinel.Tests;

/// <summary>
/// 防自记录（SelfGuard）单元测试：
/// 程序自己的数据目录绝不能被监控，防止“监控自己写库 → 又被自己记录”的死循环。
/// </summary>
public class SelfGuardTests
{
    /// <summary>测试用的数据目录。</summary>
    private const string DataDir = @"C:\测试数据\data";

    [Fact]
    public void 数据目录本身_算自身路径()
    {
        var guard = new SelfGuard(DataDir);
        Assert.True(guard.IsOwnDataPath(DataDir));
    }

    [Fact]
    public void 数据目录里的文件_算自身路径()
    {
        var guard = new SelfGuard(DataDir);
        Assert.True(guard.IsOwnDataPath(DataDir + @"\sentinel.db"));
        Assert.True(guard.IsOwnDataPath(DataDir + @"\logs\子目录\x.log"));
    }

    [Fact]
    public void 名字相近但不同的目录_不算自身路径()
    {
        // 关键测试：data 和 data2 是两个不同的目录，前缀比较必须区分开
        var guard = new SelfGuard(DataDir);
        Assert.False(guard.IsOwnDataPath(@"C:\测试数据\data2\sentinel.db"));
        Assert.False(guard.IsOwnDataPath(@"C:\测试数据\database\x.db"));
    }

    [Fact]
    public void 其他目录_不算自身路径()
    {
        var guard = new SelfGuard(DataDir);
        Assert.False(guard.IsOwnDataPath(@"C:\别的地方\文件.txt"));
    }

    [Fact]
    public void 大小写不同_仍然算自身路径()
    {
        // Windows 文件系统不区分大小写
        var guard = new SelfGuard(DataDir);
        Assert.True(guard.IsOwnDataPath(@"C:\测试数据\DATA\SENTINEL.DB"));
    }

    [Fact]
    public void 空路径_不算自身路径()
    {
        var guard = new SelfGuard(DataDir);
        Assert.False(guard.IsOwnDataPath(""));
    }

    [Fact]
    public void 默认数据目录_位于程序目录旁边且叫data()
    {
        // 约定：数据目录 = 程序目录\data（用户好找好删）
        var guard = SelfGuard.CreateDefault();
        Assert.EndsWith("data", guard.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppContext.BaseDirectory, guard.DataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== 引擎层面的防自记录保险丝 ====================

    [Fact]
    public void 引擎拒绝把数据目录加入监控_含子目录()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FolderSentinelSelf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var db = new EventDatabase(Path.Combine(tempDir, "t.db"));
            using var engine = new SentinelEngine(db, PathFilter.CreateDefault(), new SelfGuard(tempDir));

            // 数据目录本身、以及数据目录里的子目录，都不允许监控
            Assert.Throws<SelfWatchException>(() => engine.AddDirectory(tempDir));
            Assert.Throws<SelfWatchException>(() => engine.AddDirectory(Path.Combine(tempDir, "子目录")));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* 清理失败不影响测试结论 */ }
        }
    }

    [Fact]
    public void 引擎拒绝监控不存在的目录()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FolderSentinelSelf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var db = new EventDatabase(Path.Combine(tempDir, "t.db"));
            using var engine = new SentinelEngine(db, PathFilter.CreateDefault(), new SelfGuard(tempDir));

            Assert.Throws<WatchDirectoryNotFoundException>(() => engine.AddDirectory(Path.Combine(tempDir, "不存在的目录")));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* 清理失败不影响测试结论 */ }
        }
    }
}
