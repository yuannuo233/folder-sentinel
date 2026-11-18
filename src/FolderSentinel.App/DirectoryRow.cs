using System.ComponentModel;

namespace FolderSentinel.App;

/// <summary>
/// 左侧列表的一行：一个监控目录。
/// 实现 INotifyPropertyChanged，这样勾选框（CheckBox）的状态一变化，
/// 主窗口就能立刻收到通知，去打开或暂停这个目录的监控。
/// </summary>
public sealed class DirectoryRow : INotifyPropertyChanged
{
    private bool _enabled;

    /// <summary>监控目录的完整路径。</summary>
    public string Path { get; init; } = "";

    /// <summary>是否启用监控（勾选框）。</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
