namespace FolderSentinel.Core;

/// <summary>
/// 试图把“程序自己的数据目录”加入监控时抛出的异常（防自记录的保险丝）。
/// 界面层捕获它以后，用本地化文案提示用户。
/// </summary>
public sealed class SelfWatchException : ArgumentException
{
    public SelfWatchException(string path)
        : base($"不能监控程序自己的数据目录（否则会陷入“自己记录自己”的死循环）：{path}")
    {
    }
}

/// <summary>要监控的目录不存在时抛出的异常。</summary>
public sealed class WatchDirectoryNotFoundException : DirectoryNotFoundException
{
    public WatchDirectoryNotFoundException(string path)
        : base($"目录不存在：{path}")
    {
    }
}
