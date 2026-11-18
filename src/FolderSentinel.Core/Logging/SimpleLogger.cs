namespace FolderSentinel.Core.Logging;

/// <summary>
/// 极简文件日志：把程序运行中的大事小事写进数据目录的 sentinel.log，
/// 方便排查“为什么没有记录到某个文件”这类问题。
///
/// 要点：
/// - 加锁：监控、界面、数据库都可能在写日志，锁保证多线程不会写乱；
/// - 文件超过 5MB 自动“滚动”（旧文件改名为 sentinel.old.log，从新文件开始写）；
/// - 日志是给人看的，用本地时间；数据库里才用 UTC 时间；
/// - 写日志失败绝不影响主程序（比如磁盘满了，程序也得继续跑）。
/// </summary>
public sealed class SimpleLogger
{
    /// <summary>日志文件最大 5MB，超过就滚动换新文件。</summary>
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly object _lock = new();
    private readonly string _logFilePath;

    public SimpleLogger(string logFilePath)
    {
        _logFilePath = logFilePath;
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_logFilePath));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
    }

    /// <summary>记一条普通信息。</summary>
    public void Info(string message) => Write("信息", message);

    /// <summary>记一条警告。</summary>
    public void Warn(string message) => Write("警告", message);

    /// <summary>记一条错误。</summary>
    public void Error(string message) => Write("错误", message);

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 写日志失败就失败吧，别让它影响主程序
        }
    }

    /// <summary>日志太大就滚动：删掉上一次的旧文件，把当前文件改名成 *.old.log。</summary>
    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logFilePath);
            if (info.Exists && info.Length > MaxFileBytes)
            {
                var directory = System.IO.Path.GetDirectoryName(_logFilePath)!;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(_logFilePath);
                var extension = System.IO.Path.GetExtension(_logFilePath);
                var oldFile = System.IO.Path.Combine(directory, fileName + ".old" + extension);

                File.Delete(oldFile);
                File.Move(_logFilePath, oldFile);
            }
        }
        catch
        {
            // 滚动失败不影响本次记录
        }
    }
}
