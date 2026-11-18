using FolderSentinel.Core.Models;

namespace FolderSentinel.Core.Monitoring;

/// <summary>
/// 事件防抖器（“去抖动”）。
///
/// 为什么需要它？保存一个文件时，Windows 的 FileSystemWatcher 常常会在
/// 半秒内连发好几条“修改”事件（属性变一次、时间变一次、大小变一次……）。
/// 如果照单全收，时间轴上同一个文件会刷出一串重复的“修改”，很难看。
///
/// 防抖规则：同一个文件路径在“合并窗口”（默认 300 毫秒）内的多次“修改”事件，
/// 合并成一条，等窗口安静下来后才输出。新建 / 重命名 / 删除不合并，立刻放行。
///
/// 设计说明：本类自己不看时钟，“现在几点”由调用方通过 nowUtc 参数传入。
/// 这样单元测试可以自由模拟时间流逝，测试结果完全确定、不受真实时间影响。
/// </summary>
public sealed class EventDebouncer
{
    /// <summary>合并窗口：窗口内同一路径的多次修改合并成一条（默认 300 毫秒）。</summary>
    private readonly TimeSpan _window;

    /// <summary>最长等待：一个文件如果一直被写个不停，最多等这么久就必须放行（默认 2 秒），
    /// 避免“永远安静不下来”的事件被无限推迟。</summary>
    private readonly TimeSpan _maxDelay;

    /// <summary>窗口里正在等待合并的“修改”事件，按文件路径索引（Windows 路径不分大小写）。</summary>
    private readonly Dictionary<string, PendingChanged> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>一条正在等待合并的修改事件。</summary>
    private sealed class PendingChanged
    {
        /// <summary>创建等待项：最早的那条记录 + 两个时间戳起点相同。</summary>
        public PendingChanged(FileEventRecord record, DateTime timeUtc)
        {
            Record = record;
            FirstUtc = timeUtc;
            LastUtc = timeUtc;
        }

        /// <summary>窗口里最早的那条记录（输出它就代表这一串修改）。</summary>
        public FileEventRecord Record { get; }

        /// <summary>这串修改最早发生的时间。</summary>
        public DateTime FirstUtc { get; }

        /// <summary>最近一次修改的时间（窗口里再来新事件时会刷新，用来判断“安静了多久”）。</summary>
        public DateTime LastUtc { get; set; }
    }

    public EventDebouncer(TimeSpan? window = null, TimeSpan? maxDelay = null)
    {
        _window = window ?? TimeSpan.FromMilliseconds(300);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// 送进一条事件。返回值是“此刻应当立刻对外发布的事件列表”：
    /// - 新建 / 重命名 / 删除：直接返回它本身（不防抖）；
    /// - 修改：进入合并窗口等待，返回空列表，等安静后由 FlushExpired 发布。
    /// </summary>
    public IReadOnlyList<FileEventRecord> HandleEvent(FileEventRecord record, DateTime nowUtc)
    {
        if (record.Kind != FileEventKind.Changed)
        {
            return new[] { record };
        }

        lock (_pending)
        {
            var key = record.PathNew;
            if (_pending.TryGetValue(key, out var pending))
            {
                // 窗口内同一路径又来了一条修改：只刷新“最近修改时间”，不新增记录
                pending.LastUtc = nowUtc;
            }
            else
            {
                // 这条路径第一次进入窗口：登记起来等待
                _pending[key] = new PendingChanged(record, nowUtc);
            }
            return Array.Empty<FileEventRecord>();
        }
    }

    /// <summary>
    /// 清点窗口：把“已经安静满一个窗口”或“等得超过最长上限”的修改事件发布出来。
    /// 引擎每隔一小段时间调用一次这个方法。
    /// </summary>
    public IReadOnlyList<FileEventRecord> FlushExpired(DateTime nowUtc)
    {
        var ready = new List<FileEventRecord>();
        lock (_pending)
        {
            foreach (var (key, pending) in _pending.ToArray())
            {
                var quietLongEnough = nowUtc - pending.LastUtc >= _window;    // 安静满一个窗口了
                var waitedTooLong = nowUtc - pending.FirstUtc >= _maxDelay;   // 一直写个不停，到上限了

                if (quietLongEnough || waitedTooLong)
                {
                    ready.Add(pending.Record);
                    _pending.Remove(key);
                }
            }
        }
        return ready;
    }

    /// <summary>程序退出前调用：不管等没等到窗口，把剩余的修改事件全部发布出来，一条都不丢。</summary>
    public IReadOnlyList<FileEventRecord> FlushAll()
    {
        var all = new List<FileEventRecord>();
        lock (_pending)
        {
            foreach (var pending in _pending.Values)
            {
                all.Add(pending.Record);
            }
            _pending.Clear();
        }
        return all;
    }
}
