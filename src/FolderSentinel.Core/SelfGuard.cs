namespace FolderSentinel.Core;

/// <summary>
/// “防自记录”保镖。
///
/// 为什么需要它？程序自己的数据库、日志、设置文件都写在数据目录里。
/// 如果用户恰好监控了这个目录（或它的上级目录），监控就会捕捉到
/// “程序自己写数据库”这个动作，又要存一条记录进数据库，数据库文件又变了，
/// 又被捕捉……无限死循环！
///
/// 这个类负责判断“某个路径是不是程序自己的数据”，监控引擎遇到这类事件一律丢弃。
/// </summary>
public sealed class SelfGuard
{
    /// <summary>数据目录的完整路径，末尾补了目录分隔符，方便做“前缀”比较。</summary>
    private readonly string _dataRootWithSeparator;

    public SelfGuard(string dataDirectory)
    {
        var root = System.IO.Path.GetFullPath(dataDirectory)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        DataDirectory = root;
        _dataRootWithSeparator = root + System.IO.Path.DirectorySeparatorChar;
    }

    /// <summary>程序自己的数据目录（数据库 sentinel.db、日志 sentinel.log、设置 settings.json 都在这里）。</summary>
    public string DataDirectory { get; }

    /// <summary>
    /// 判断某个路径是否属于程序自身的数据目录。
    /// 注意比较时用“忽略大小写”，因为 Windows 文件系统不区分大小写。
    /// </summary>
    public bool IsOwnDataPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(fullPath)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // 路径格式非法，安全起见按“不是自己的”处理（后面入库时还会有别的检查）
            return false;
        }

        // 两种情况都算“自己人”：路径本身就是数据目录，或者路径在数据目录里面。
        // 用“目录名 + 分隔符”做前缀比较，可以避免误伤名字相近的目录（例如 data 和 data2）。
        return string.Equals(full, DataDirectory, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(_dataRootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 默认的数据目录：可执行文件旁边的 data 文件夹。
    /// 选这里是因为简单透明——用户想看、想备份、想删除都容易找到。
    /// </summary>
    public static SelfGuard CreateDefault()
    {
        return new SelfGuard(System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
    }
}
