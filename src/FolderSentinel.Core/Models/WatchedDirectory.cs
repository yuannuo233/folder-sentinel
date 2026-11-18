namespace FolderSentinel.Core.Models;

/// <summary>
/// 一个被监控目录的配置：路径 + 是否启用。
/// </summary>
public sealed class WatchedDirectory
{
    /// <summary>数据库里的自增编号。</summary>
    public long Id { get; init; }

    /// <summary>监控目录的完整路径。</summary>
    public string Path { get; init; } = "";

    /// <summary>是否启用监控（可以临时关掉某个目录，不必删除它）。</summary>
    public bool Enabled { get; set; }
}
