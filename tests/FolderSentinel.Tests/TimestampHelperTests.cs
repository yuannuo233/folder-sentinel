using Xunit;
using FolderSentinel.Core;

namespace FolderSentinel.Tests;

/// <summary>
/// 时间戳工具单元测试：UTC 毫秒时间戳的正向、反向、往返转换。
/// 注意：所有断言都不依赖运行机器的时区，在任何时区跑结果都一样。
/// </summary>
public class TimestampHelperTests
{
    [Fact]
    public void 已知时刻_转Unix毫秒_得到准确数值()
    {
        // 2026-01-01 00:00:00 UTC = 1767225600000 毫秒（公开可查的固定值）
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1767225600000L, TimestampHelper.ToUnixMs(utc));
    }

    [Fact]
    public void Unix毫秒_转回UTC时间_准确()
    {
        var time = TimestampHelper.FromUnixMs(1767225600123L);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, 123, DateTimeKind.Utc), time);
        // 读出来的必须是 UTC 类型（数据库存取约定）
        Assert.Equal(DateTimeKind.Utc, time.Kind);
    }

    [Fact]
    public void UTC时间往返转换_毫秒精度不丢失()
    {
        var original = new DateTime(2026, 6, 15, 12, 34, 56, 789, DateTimeKind.Utc);
        var roundTrip = TimestampHelper.FromUnixMs(TimestampHelper.ToUnixMs(original));
        Assert.Equal(original, roundTrip);
    }

    [Fact]
    public void 本地时间输入_自动换算成UTC()
    {
        // 同一个时刻：UTC 表示法与本地表示法，转出的时间戳必须相同
        var utc = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var sameMomentAsLocal = utc.ToLocalTime();
        Assert.Equal(TimestampHelper.ToUnixMs(utc), TimestampHelper.ToUnixMs(sameMomentAsLocal));
    }

    [Fact]
    public void Unix纪元_换算正确()
    {
        // 1970-01-01 00:00:00 UTC 是时间戳的起点，应该是 0
        Assert.Equal(0L, TimestampHelper.ToUnixMs(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimestampHelper.FromUnixMs(0));
    }

    [Fact]
    public void 本地显示字符串_与手动换算一致()
    {
        var ms = 1767225600123L;
        var expected = TimestampHelper.FromUnixMs(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        Assert.Equal(expected, TimestampHelper.ToLocalString(ms));
    }
}
