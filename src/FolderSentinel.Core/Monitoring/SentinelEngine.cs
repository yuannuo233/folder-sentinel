using System.Collections.Concurrent;
using FolderSentinel.Core.Logging;
using FolderSentinel.Core.Models;
using FolderSentinel.Core.Storage;

namespace FolderSentinel.Core.Monitoring;

/// <summary>
/// 监控引擎（总指挥）。它的工作流程：
///
///   探头(DirectoryMonitor) ──事件──▶ 排队(Queue) ──▶ 后台处理线程
///                                                        │
///                          ┌─────────────────────────────┘
///                          ▼
///        防抖合并(300ms) → 防自记录检查 → 写入 SQLite → 通知界面
///
/// 线程规则（非常重要）：
/// - FileSystemWatcher 的回调、数据库写入都发生在后台线程上；
/// - 界面（WPF）绝不能直接在后台线程更新控件，必须通过 Dispatcher 转回界面线程；
/// - 引擎对外只暴露两个事件：EventCommitted（已入库的事件）和 MessageRaised（提示消息），
///   界面订阅它们并自行处理线程切换。
/// </summary>
public sealed class SentinelEngine : IDisposable
{
    /// <summary>一条事件已经防抖、入库完毕（后台线程触发，界面需用 Dispatcher 转回 UI 线程）。</summary>
    public event Action<FileEventRecord>? EventCommitted;

    /// <summary>引擎提示消息（后台线程触发）。第一个参数是消息代码，第二个是相关路径，界面负责翻译。</summary>
    public event Action<EngineMessageCode, string>? MessageRaised;

    private readonly EventDatabase _database;
    private readonly PathFilter _filter;
    private readonly SelfGuard _selfGuard;
    private readonly SimpleLogger? _logger;

    /// <summary>正在工作的探头们，按目录路径索引（不区分大小写）。</summary>
    private readonly Dictionary<string, DirectoryMonitor> _monitors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _monitorsLock = new();

    /// <summary>防抖器：同一文件 300ms 内的多次修改合并成一条。</summary>
    private readonly EventDebouncer _debouncer = new();

    /// <summary>待处理事件队列：探头把事件丢进来，处理线程慢慢消化。</summary>
    private readonly ConcurrentQueue<FileEventRecord> _queue = new();

    /// <summary>叫醒信号：探头放进新事件、或防抖检查时间到了，都会“摇铃”叫醒处理线程。</summary>
    private readonly SemaphoreSlim _signal = new(initialCount: 0, maxCount: int.MaxValue);

    private CancellationTokenSource? _cts;
    private Task? _processTask;
    private Timer? _flushTimer;
    private bool _disposed;

    /// <summary>数据库实例（界面用它做按天回看查询）。</summary>
    public EventDatabase Database => _database;

    /// <summary>引擎是否正在监控中。</summary>
    public bool IsRunning => _processTask is { IsCompleted: false };

    public SentinelEngine(EventDatabase database, PathFilter? filter = null, SelfGuard? selfGuard = null, SimpleLogger? logger = null)
    {
        _database = database;
        _filter = filter ?? PathFilter.CreateDefault();
        _selfGuard = selfGuard ?? SelfGuard.CreateDefault();
        _logger = logger;
    }

    // ==================== 启动与停止 ====================

    /// <summary>
    /// 启动监控：开一个后台处理线程，然后从数据库读出目录清单，逐个派探头。
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _processTask = Task.Run(() => ProcessLoop(_cts.Token));

        // 每隔 100 毫秒“摇一次铃”：提醒处理线程去检查防抖窗口里有没有该发布的记录。
        // （修改事件要等 300ms 窗口安静后才发布，不能光靠新事件来叫醒。）
        _flushTimer = new Timer(_ => _signal.Release(), null, dueTime: 100, period: 100);

        foreach (var dir in _database.GetDirectories())
        {
            if (dir.Enabled)
            {
                StartMonitor(dir.Path);
            }
        }
        _logger?.Info($"监控引擎已启动，共启用 {_monitors.Count} 个目录监控。");
    }

    /// <summary>
    /// 停止监控：停掉所有探头，把队列和防抖窗口里剩下的事件全部收尾入库。
    /// </summary>
    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _flushTimer?.Dispose();
        _flushTimer = null;
        try
        {
            _processTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 收尾阶段出现异常：记录下来即可，不能打断退出流程
        }
        _processTask = null;
        _cts.Dispose();
        _cts = null;

        lock (_monitorsLock)
        {
            foreach (var monitor in _monitors.Values)
            {
                monitor.EventDetected -= OnRawEvent;
                monitor.Dispose();
            }
            _monitors.Clear();
        }
        _logger?.Info("监控引擎已停止。");
    }

    // ==================== 目录管理（界面调用） ====================

    /// <summary>
    /// 添加一个监控目录：写入数据库，并立即开始监控（如果引擎在运行）。
    /// 目录不存在或选中的是程序自己的数据目录时会抛异常，由界面提示用户。
    /// </summary>
    public void AddDirectory(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = NormalizeDirectoryPath(path);

        if (!Directory.Exists(normalized))
        {
            throw new WatchDirectoryNotFoundException(normalized);
        }
        if (_selfGuard.IsOwnDataPath(normalized))
        {
            throw new SelfWatchException(normalized);
        }

        _database.UpsertDirectory(normalized, enabled: true);
        if (IsRunning)
        {
            StartMonitor(normalized);
        }
        _logger?.Info($"添加监控目录：{normalized}");
    }

    /// <summary>移除一个监控目录：删掉数据库记录并停掉对应的探头。</summary>
    public void RemoveDirectory(string path)
    {
        var normalized = NormalizeDirectoryPath(path);
        _database.RemoveDirectory(normalized);
        StopMonitor(normalized);
        _logger?.Info($"移除监控目录：{normalized}");
    }

    /// <summary>打开或暂停某个目录的监控（不删除记录，随时可以再打开）。</summary>
    public void SetDirectoryEnabled(string path, bool enabled)
    {
        var normalized = NormalizeDirectoryPath(path);
        _database.UpsertDirectory(normalized, enabled);
        if (enabled)
        {
            if (IsRunning)
            {
                StartMonitor(normalized);
            }
            _logger?.Info($"开启监控目录：{normalized}");
        }
        else
        {
            StopMonitor(normalized);
            _logger?.Info($"暂停监控目录：{normalized}");
        }
    }

    /// <summary>目录路径标准化（供界面在添加前先把路径统一格式）。</summary>
    public static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        // 根目录（如 C:\）保留末尾分隔符；其余目录去掉末尾分隔符，保证同一目录只有一种写法
        if (full.Length > 3 &&
            (full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            full = full[..^1];
        }
        return full;
    }

    // ==================== 内部实现 ====================

    /// <summary>给一个目录派探头（重复添加、目录不存在、启动失败都会被安全处理）。</summary>
    private void StartMonitor(string directory)
    {
        lock (_monitorsLock)
        {
            if (_monitors.ContainsKey(directory))
            {
                return;   // 已经在监控了
            }
            if (!Directory.Exists(directory))
            {
                _logger?.Warn($"目录不存在，跳过监控：{directory}");
                MessageRaised?.Invoke(EngineMessageCode.DirMissing, directory);
                return;
            }

            try
            {
                var monitor = new DirectoryMonitor(directory, _filter);
                monitor.EventDetected += OnRawEvent;
                monitor.ErrorOccurred += message =>
                {
                    // 探头出故障：写日志 + 发消息代码给界面（界面翻译成当前语言提示用户）
                    _logger?.Warn(message);
                    MessageRaised?.Invoke(EngineMessageCode.BufferOverflow, directory);
                };
                _monitors[directory] = monitor;
                _logger?.Info($"开始监控目录：{directory}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"启动监控失败：{directory}，原因：{ex.Message}");
                MessageRaised?.Invoke(EngineMessageCode.StartFailed, directory);
            }
        }
    }

    /// <summary>撤掉一个目录的探头。</summary>
    private void StopMonitor(string directory)
    {
        lock (_monitorsLock)
        {
            if (_monitors.TryGetValue(directory, out var monitor))
            {
                monitor.EventDetected -= OnRawEvent;
                monitor.Dispose();
                _monitors.Remove(directory);
                _logger?.Info($"停止监控目录：{directory}");
            }
        }
    }

    /// <summary>探头回调（系统后台线程）：把事件放进队列，摇铃叫醒处理线程。</summary>
    private void OnRawEvent(FileEventRecord record)
    {
        _queue.Enqueue(record);
        _signal.Release();
    }

    /// <summary>
    /// 后台处理主循环：被铃声叫醒 → 消化队列（防抖）→ 清点防抖窗口 → 逐条入库。
    /// </summary>
    private void ProcessLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                _signal.Wait(token);   // 等铃响（有新事件，或到了检查防抖窗口的时间）
                DrainQueueAndFlush();
            }
        }
        catch (OperationCanceledException)
        {
            // 收到停止信号：正常退出
        }
        finally
        {
            // 退出前把队列和防抖窗口里剩下的事件全部入库，一条都不丢
            DrainQueueAndFlush(finalFlush: true);
        }
    }

    /// <summary>消化队列 + 清点防抖窗口；finalFlush=true 表示收尾（不再等窗口）。</summary>
    private void DrainQueueAndFlush(bool finalFlush = false)
    {
        while (_queue.TryDequeue(out var raw))
        {
            foreach (var ready in _debouncer.HandleEvent(raw, DateTime.UtcNow))
            {
                Commit(ready);
            }
        }

        var flushed = finalFlush ? _debouncer.FlushAll() : _debouncer.FlushExpired(DateTime.UtcNow);
        foreach (var ready in flushed)
        {
            Commit(ready);
        }
    }

    /// <summary>单条事件落地：防自记录检查 → 写入数据库 → 通知界面。</summary>
    private void Commit(FileEventRecord record)
    {
        try
        {
            // 防自记录：程序自己写数据库/日志产生的事件一律丢弃，防止死循环。
            // 重命名事件的新旧两个路径都要检查。
            if (_selfGuard.IsOwnDataPath(record.PathNew))
            {
                return;
            }
            if (record.PathOld.Length > 0 && _selfGuard.IsOwnDataPath(record.PathOld))
            {
                return;
            }

            _database.InsertEvent(record);
            EventCommitted?.Invoke(record);
        }
        catch (Exception ex)
        {
            // 入库失败不能拖垮整个引擎，记录日志继续干活
            _logger?.Error($"事件入库失败：{record.PathNew}，原因：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        _signal.Dispose();
    }
}
