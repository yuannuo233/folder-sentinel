namespace FolderSentinel.Core.Models;

/// <summary>
/// 文件事件的四种类型。
/// </summary>
public enum FileEventKind
{
    /// <summary>新建：文件或文件夹被创建。</summary>
    Created,

    /// <summary>修改：文件内容或属性被改动。</summary>
    Changed,

    /// <summary>重命名：文件被改名或移动位置。</summary>
    Renamed,

    /// <summary>删除：文件或文件夹被删除。</summary>
    Deleted
}
