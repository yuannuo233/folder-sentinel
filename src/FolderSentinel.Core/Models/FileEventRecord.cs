namespace FolderSentinel.Core.Models;

/// <summary>
/// 统一的文件事件记录——监控引擎捕获到的每一次文件变动，
/// 最终都会变成这样一条“标准化记录”，既能存进数据库，也能显示到界面上。
/// </summary>
public sealed record FileEventRecord
{
    /// <summary>事件发生的时间（一律用 UTC 时间，显示时再转成本地时间）。</summary>
    public DateTime TimeUtc { get; init; }

    /// <summary>事件类型（新建 / 修改 / 重命名 / 删除）。</summary>
    public FileEventKind Kind { get; init; }

    /// <summary>
    /// 原来的路径。只有“重命名”事件用它表示改名前的旧路径；
    /// 其他事件此字段为空字符串。
    /// </summary>
    public string PathOld { get; init; } = "";

    /// <summary>
    /// 当前的路径。重命名事件表示改名后的新路径，
    /// 其余事件直接表示这个文件的完整路径。
    /// </summary>
    public string PathNew { get; init; } = "";

    /// <summary>事件发生在哪个监控目录里（就是用户添加的那个目录）。</summary>
    public string DirPath { get; init; } = "";

    /// <summary>
    /// 动手的“进程名”（哪个软件干的）。
    /// v0.1 版本暂时无法可靠获取，此字段留空，界面上显示“未知”。
    /// TODO(v0.2)：计划改用 Windows 的 ETW 事件源（Microsoft-Windows-Kernel-File），
    /// 在内核层面准确记录每一次文件操作的发起进程。
    /// </summary>
    public string? ProcessName { get; init; }
}
