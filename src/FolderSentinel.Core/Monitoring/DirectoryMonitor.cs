using FolderSentinel.Core.Models;

namespace FolderSentinel.Core.Monitoring;

/// <summary>
/// 单目录监控器：包着一个 Windows 的 FileSystemWatcher（文件系统监视器），
/// 把它吐出来的各种回调，翻译成统一的 FileEventRecord。
///
/// 通俗比喻：FileSystemWatcher 就是装在目录门口的“监控探头”——
/// 谁往里放了新东西（新建）、谁动了里面的东西（修改）、谁把东西拿走了（删除）、
/// 谁给东西换了标签（重命名），探头都会响一下。本类负责把这些“警报”
/// 统一登记造册，再交给监控引擎处理。
///
/// 线程说明：FileSystemWatcher 的回调发生在系统后台线程上，
/// 本类只做“翻译 + 入队”，绝不直接碰任何界面控件。
/// </summary>
public sealed class DirectoryMonitor : IDisposable
{
    /// <summary>监控到一条事件（在后台线程触发，订阅方注意线程安全）。</summary>
    public event Action<FileEventRecord>? EventDetected;

    /// <summary>探头出了故障（目录被删、缓冲区溢出等），参数是核心层的中文描述（主要用于日志）。</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>正在监控的目录完整路径。</summary>
    public string MonitoredPath { get; }

    private readonly FileSystemWatcher _watcher;
    private readonly PathFilter _filter;

    public DirectoryMonitor(string directory, PathFilter filter)
    {
        MonitoredPath = Path.GetFullPath(directory);
        _filter = filter;

        _watcher = new FileSystemWatcher(MonitoredPath)
        {
            IncludeSubdirectories = true,      // 子文件夹里的动静也要管
            InternalBufferSize = 64 * 1024,    // 内部缓冲区加大到 64KB，减少“事件太多装不下”的丢失
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.CreationTime
        };

        _watcher.Created += OnCreated;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        // 到这里探头才正式开始工作
        _watcher.EnableRaisingEvents = true;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        Raise(e.FullPath, FileEventKind.Created);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        Raise(e.FullPath, FileEventKind.Changed);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        Raise(e.FullPath, FileEventKind.Deleted);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // 改名有两个路径（改前、改后）：只要其中一边不在“忽略名单”里，就值得记录。
        // 例如“a.tmp 改名为 a.txt”值得记；而“草稿1.tmp 改名成 草稿2.tmp”就随它去吧。
        if (_filter.ShouldIgnore(e.OldFullPath) && _filter.ShouldIgnore(e.FullPath))
        {
            return;
        }

        EventDetected?.Invoke(new FileEventRecord
        {
            TimeUtc = DateTime.UtcNow,
            Kind = FileEventKind.Renamed,
            PathOld = e.OldFullPath,
            PathNew = e.FullPath,
            DirPath = MonitoredPath
        });
    }

    /// <summary>新建 / 修改 / 删除 共用的登记逻辑：先过“忽略名单”，再统一打包。</summary>
    private void Raise(string fullPath, FileEventKind kind)
    {
        if (_filter.ShouldIgnore(fullPath))
        {
            return;   // 临时文件之类直接丢弃，别浪费后面的入库和界面刷新
        }

        EventDetected?.Invoke(new FileEventRecord
        {
            TimeUtc = DateTime.UtcNow,
            Kind = kind,
            PathNew = fullPath,
            DirPath = MonitoredPath
        });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // 最常见的两种故障：
        // 1. 短时间事件太多，探头内部缓冲区装不下（会丢一部分事件）；
        // 2. 被监控的目录本身被删除了。
        var reason = e.GetException()?.Message ?? "未知原因";
        ErrorOccurred?.Invoke($"监控目录出现问题（可能是事件过多缓冲区溢出，或目录被删除）：{MonitoredPath}。{reason}");
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
