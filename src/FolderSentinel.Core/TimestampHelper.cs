namespace FolderSentinel.Core;

/// <summary>
/// 时间戳工具：数据库里统一存“Unix 毫秒时间戳”（从 1970-01-01 UTC 开始数的毫秒数）。
/// 好处：不依赖时区，任何机器上存取都一致；显示的时候再转回本地时间。
/// </summary>
public static class TimestampHelper
{
    /// <summary>
    /// 把一个时间转换成 Unix 毫秒时间戳。
    /// 如果传进来的是“本地时间”，会先自动换算成 UTC，避免算错。
    /// </summary>
    public static long ToUnixMs(DateTime time)
    {
        // DateTimeKind 有三种：Utc（UTC 时间）、Local（本地时间）、Unspecified（没说明）。
        // 本地时间先换算成 UTC；其余按 UTC 处理。
        var utc = time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => time
        };
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>把 Unix 毫秒时间戳转换回 UTC 时间（Kind 一定是 Utc）。</summary>
    public static DateTime FromUnixMs(long unixMs)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
    }

    /// <summary>把 Unix 毫秒时间戳格式化成本地时间字符串，例如：2026-01-01 14:30:05.123。</summary>
    public static string ToLocalString(long unixMs)
    {
        return FromUnixMs(unixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    }

    /// <summary>把 UTC 时间格式化成本地时间字符串（重载版本，方便直接传时间）。</summary>
    public static string ToLocalString(DateTime timeUtc)
    {
        return ToLocalString(ToUnixMs(timeUtc));
    }
}
