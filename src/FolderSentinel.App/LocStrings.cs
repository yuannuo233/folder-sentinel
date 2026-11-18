namespace FolderSentinel.App;

/// <summary>
/// 两套界面文案字典（简体中文 / 英文）。
/// 新增界面文字时，两个字典必须同步添加、键名保持一致——漏一边就会显示回退文案。
/// 注意：语言选择框里的“简体中文 / English”是刻意的例外——
/// 每种语言的名字永远用它自己的语言显示（通用惯例），所以不走翻译字典。
/// </summary>
internal static class LocStrings
{
    /// <summary>简体中文文案。</summary>
    public static readonly Dictionary<string, string> Zh = new()
    {
        // ---- 窗口与工具条 ----
        ["app.title"]            = "文件哨兵 —— 看看谁动了我的文件",
        ["app.shortName"]        = "文件哨兵",
        ["nav.events"]           = "事件监控",
        ["nav.dirs"]             = "监控目录",
        ["toolbar.filterLabel"]  = "关键字过滤：",
        ["toolbar.filterTip"]    = "只显示路径包含关键字的事件（对实时和回看都生效）",
        ["toolbar.historyLabel"] = "按天回看：",
        ["toolbar.historyTip"]   = "选择日期，从数据库加载当天的全部记录",
        ["toolbar.backToLive"]   = "回到实时",
        ["toolbar.languageTip"]  = "切换界面语言（立即生效并记住选择）",

        // ---- 监控目录面板 ----
        ["dir.add"]           = "添加目录",
        ["dir.remove"]        = "删除选中",
        ["dir.pickTitle"]     = "选择要监控的文件夹",
        ["dir.duplicate"]     = "这个目录已经在监控列表里了。",
        ["dir.noSelection"]   = "请先在列表中选中一个目录。",
        ["dir.removeTitle"]   = "移除监控目录",
        ["dir.confirmRemove"] = "确定不再监控这个目录吗？（已有的历史记录不会被删除）",
        ["dir.selfWatch"]     = "不能监控程序自己的数据目录，否则会陷入“自己记录自己”的死循环。",
        ["dir.notFound"]      = "这个目录不存在，无法监控。",
        ["dir.addFailed"]     = "无法添加目录：{0}",

        // ---- 事件列表 ----
        ["col.time"]     = "时间",
        ["col.kind"]     = "类型",
        ["col.path"]     = "路径",
        ["col.process"]  = "进程",

        // ---- 事件类型 ----
        ["kind.created"] = "新建",
        ["kind.changed"] = "修改",
        ["kind.renamed"] = "重命名",
        ["kind.deleted"] = "删除",
        ["process.unknown"] = "未知",

        // ---- 状态栏 ----
        ["status.live"]       = "实时模式",
        ["status.history"]    = "回看模式：{0}",
        ["status.monitoring"] = "监控 {0} 个目录",
        ["status.recorded"]   = "本次运行已记录 {0} 条",

        // ---- 空状态提示 ----
        ["empty.live"]    = "还没有记录到任何文件事件。\n添加监控目录后，其他软件对文件的操作会实时出现在这里。",
        ["empty.history"] = "这一天没有找到文件记录。",
        ["empty.dirs"]    = "还没有监控目录。\n点击上方“添加目录”，开始守护你的文件夹。",

        // ---- 系统托盘 ----
        ["tray.tooltip"]     = "文件哨兵：正在守护你的文件",
        ["tray.open"]        = "打开主窗口",
        ["tray.exit"]        = "退出（停止监控）",
        ["tray.balloonTitle"] = "文件哨兵仍在监控",
        ["tray.balloonText"]  = "程序已收进系统托盘，会继续记录文件变动。双击托盘图标可回到主窗口。",

        // ---- 消息提示 ----
        ["msg.startupFailed"]   = "程序启动失败（可能是数据目录没有写权限）：{0}",
        ["msg.queryFailed"]     = "查询历史记录失败：{0}",
        ["msg.operationFailed"] = "操作失败：{0}",
        ["msg.unexpected"]      = "程序遇到了意外问题，但还能继续运行。\n\n详细信息：{0}",
        ["msg.bufferOverflow"]  = "目录 {0} 的事件太多，监控缓冲区溢出，可能有部分事件没被记录。",
        ["msg.dirMissing"]      = "目录 {0} 不存在（可能已被删除或移动），已自动暂停对它的监控。",
        ["msg.startFailed"]     = "目录 {0} 的监控启动失败，请检查读取权限。",
    };

    /// <summary>English strings (idiomatic, not a word-by-word translation).</summary>
    public static readonly Dictionary<string, string> En = new()
    {
        // ---- Window & toolbar ----
        ["app.title"]             = "FolderSentinel — Who touched my files?",
        ["app.shortName"]         = "FolderSentinel",
        ["nav.events"]            = "Live events",
        ["nav.dirs"]              = "Watched folders",
        ["toolbar.filterLabel"]   = "Filter: ",
        ["toolbar.filterTip"]     = "Show only events whose path contains the keyword (live view and history)",
        ["toolbar.historyLabel"]  = "Browse by day: ",
        ["toolbar.historyTip"]    = "Pick a date to load that day's records from the database",
        ["toolbar.backToLive"]    = "Back to live",
        ["toolbar.languageTip"]   = "Switch UI language (applies immediately)",

        // ---- Watched folders panel ----
        ["dir.add"]           = "Add folder",
        ["dir.remove"]        = "Remove selected",
        ["dir.pickTitle"]     = "Choose a folder to watch",
        ["dir.duplicate"]     = "This folder is already in the list.",
        ["dir.noSelection"]   = "Select a folder in the list first.",
        ["dir.removeTitle"]   = "Remove watched folder",
        ["dir.confirmRemove"] = "Stop watching this folder? (Existing history is kept.)",
        ["dir.selfWatch"]     = "You can't watch the app's own data folder — the app would end up recording itself in an endless loop.",
        ["dir.notFound"]      = "This folder doesn't exist and can't be watched.",
        ["dir.addFailed"]     = "Cannot add folder: {0}",

        // ---- Event list ----
        ["col.time"]     = "Time",
        ["col.kind"]     = "Kind",
        ["col.path"]     = "Path",
        ["col.process"]  = "Process",

        // ---- Event kinds ----
        ["kind.created"] = "Create",
        ["kind.changed"] = "Modify",
        ["kind.renamed"] = "Rename",
        ["kind.deleted"] = "Delete",
        ["process.unknown"] = "Unknown",

        // ---- Status bar ----
        ["status.live"]       = "Live mode",
        ["status.history"]    = "History: {0}",
        ["status.monitoring"] = "Watching {0} folder(s)",
        ["status.recorded"]   = "{0} events this session",

        // ---- Empty states ----
        ["empty.live"]    = "No events recorded yet.\nAdd watched folders and file activity from any program will appear here in real time.",
        ["empty.history"] = "No file records found for this day.",
        ["empty.dirs"]    = "No watched folders yet.\nClick \"Add folder\" above to start guarding your folders.",

        // ---- System tray ----
        ["tray.tooltip"]      = "FolderSentinel: guarding your files",
        ["tray.open"]         = "Open main window",
        ["tray.exit"]         = "Exit (stop monitoring)",
        ["tray.balloonTitle"] = "FolderSentinel is still watching",
        ["tray.balloonText"]  = "Minimized to the system tray — monitoring continues. Double-click the tray icon to reopen.",

        // ---- Message boxes ----
        ["msg.startupFailed"]   = "Failed to start (check write permission of the data folder): {0}",
        ["msg.queryFailed"]     = "Failed to query history: {0}",
        ["msg.operationFailed"] = "Operation failed: {0}",
        ["msg.unexpected"]      = "Something unexpected happened, but the app can keep running.\n\nDetails: {0}",
        ["msg.bufferOverflow"]  = "Too many events in folder {0}; the watcher buffer overflowed and some events may be missing.",
        ["msg.dirMissing"]      = "Folder {0} no longer exists (deleted or moved); watching it is paused.",
        ["msg.startFailed"]     = "Failed to start watching folder {0}. Please check read permissions.",
    };

    /// <summary>
    /// 语言 → 文案字典的总目录。
    /// 注意声明顺序：必须放在 Zh、En 之后（C# 静态字段按声明顺序初始化）。
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new()
    {
        ["zh-CN"] = Zh,
        ["en-US"] = En,
    };
}
