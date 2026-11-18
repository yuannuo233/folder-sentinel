using System.ComponentModel;
using FolderSentinel.Core;
using FolderSentinel.Core.Models;

namespace FolderSentinel.App;

/// <summary>
/// 界面上的一条事件行：把核心层的 FileEventRecord 翻译成显示用的文字。
/// 实现了 INotifyPropertyChanged（属性变了通知界面刷新）——
/// 切换语言时，主窗口会重写 Kind 和 Process 两个属性，列表文字随之更新。
/// </summary>
public sealed class EventRow : INotifyPropertyChanged
{
    private string _time = "";
    private string _kind = "";
    private string _path = "";
    private string _process = "";

    public string Time
    {
        get => _time;
        set { _time = value; OnPropertyChanged(nameof(Time)); }
    }

    public string Kind
    {
        get => _kind;
        set { _kind = value; OnPropertyChanged(nameof(Kind)); }
    }

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(nameof(Path)); }
    }

    public string Process
    {
        get => _process;
        set { _process = value; OnPropertyChanged(nameof(Process)); }
    }

    /// <summary>对应的原始记录（切换语言重新翻译、关键字过滤时都要用到它）。</summary>
    public FileEventRecord Record { get; init; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 事件行工厂：负责“核心记录 → 界面行”的翻译工作。
/// </summary>
public static class EventRowFactory
{
    /// <summary>把一条核心层的记录翻译成界面显示行。</summary>
    public static EventRow FromRecord(FileEventRecord record)
    {
        return new EventRow
        {
            Record = record,
            Time = TimestampHelper.ToLocalString(record.TimeUtc),   // UTC → 本地时间字符串
            Kind = Loc.T("kind." + record.Kind.ToStorageName().ToLowerInvariant()),

            // 重命名事件把“旧名 → 新名”显示在同一格里，一眼能看懂来龙去脉
            Path = record.Kind == FileEventKind.Renamed
                ? $"{record.PathOld} → {record.PathNew}"
                : record.PathNew,

            // v0.1 进程归属未实现：记录里没有进程名 → 显示“未知”
            Process = string.IsNullOrEmpty(record.ProcessName)
                ? Loc.T("process.unknown")
                : record.ProcessName
        };
    }
}
