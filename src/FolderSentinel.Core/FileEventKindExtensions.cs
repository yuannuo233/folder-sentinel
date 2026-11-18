using FolderSentinel.Core.Models;

namespace FolderSentinel.Core;

/// <summary>
/// 文件事件类型的“翻译官”：
/// 数据库里存英文代号（稳定、方便查询），界面上显示中文/英文（友好）。
/// </summary>
public static class FileEventKindExtensions
{
    /// <summary>存数据库时用的英文名，例如 Created、Renamed。</summary>
    public static string ToStorageName(this FileEventKind kind)
    {
        return kind.ToString();
    }

    /// <summary>从数据库读出来时，把英文名还原成枚举。</summary>
    public static FileEventKind FromStorageName(string name)
    {
        return Enum.TryParse<FileEventKind>(name, ignoreCase: true, out var kind)
            ? kind
            : FileEventKind.Changed;   // 万一遇到不认识的名字，按“修改”兜底
    }

    /// <summary>
    /// 界面上显示的中文名。
    /// 注意：界面层的多语言文案在 App 项目里（Loc 类），这里只提供核心层的中文习惯叫法，
    /// 主要给日志和调试用。
    /// </summary>
    public static string ToChineseName(this FileEventKind kind)
    {
        return kind switch
        {
            FileEventKind.Created => "新建",
            FileEventKind.Changed => "修改",
            FileEventKind.Renamed => "重命名",
            FileEventKind.Deleted => "删除",
            _ => kind.ToString()
        };
    }
}
