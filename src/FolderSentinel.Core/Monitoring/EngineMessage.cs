namespace FolderSentinel.Core.Monitoring;

/// <summary>
/// 引擎消息代码：引擎内部出了值得告诉用户的事，就发一个“代码 + 相关路径”。
/// 核心层不负责写界面文案（界面有中英双语），由界面层把代码翻译成当前语言的提示。
/// </summary>
public enum EngineMessageCode
{
    /// <summary>探头内部缓冲区溢出：短时间事件太多装不下，可能丢了一部分（常见于大量文件同时复制）。</summary>
    BufferOverflow,

    /// <summary>要监控的目录不存在了（可能已被删除或移动），对这个目录的监控自动暂停。</summary>
    DirMissing,

    /// <summary>某个目录的监控启动失败（例如没有读取权限）。</summary>
    StartFailed
}
