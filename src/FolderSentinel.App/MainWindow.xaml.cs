using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FolderSentinel.Core;
using FolderSentinel.Core.Logging;
using FolderSentinel.Core.Models;
using FolderSentinel.Core.Monitoring;
using FolderSentinel.Core.Storage;
using Microsoft.Win32;

// 本项目同时启用了 WPF 和 WinForms（托盘图标用），两边都有 Application 和 MessageBox，
// 用别名明确指定用 WPF 的版本；Color/SolidColorBrush 指定用 WPF.Media 的版本。
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FolderSentinel.App;

/// <summary>
/// 主窗口：左侧导航栏（事件监控 / 监控目录），右侧卡片式内容区；
/// 最小化后收进系统托盘继续监控。
///
/// 【线程规则——本项目最重要的一条纪律】
/// 监控引擎和数据库都在后台线程上工作；后台线程绝不能直接碰界面控件。
/// 所以引擎的回调里统一用 Dispatcher.InvokeAsync 把“刷新界面”这个活儿
/// 转回界面线程执行，这是 WPF 的标准做法。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>实时列表最多保留多少行（防止长时间运行把内存吃光；完整历史都在数据库里）。</summary>
    private const int MaxLiveRows = 2000;

    // 状态胶囊上的小圆点：绿色 = 监控中，灰色 = 已停止（冻结后可安全复用）
    private static readonly SolidColorBrush DotRunningBrush = MakeBrush(0x0F, 0x7B, 0x0F);
    private static readonly SolidColorBrush DotIdleBrush = MakeBrush(0xA6, 0xB0, 0xBA);

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly AppSettings _settings;
    private readonly ObservableCollection<DirectoryRow> _directories = new();
    private readonly ObservableCollection<EventRow> _liveRows = new();
    private readonly List<EventRow> _historyRows = new();
    private readonly ICollectionView _liveView;

    // 下面三个在 try/catch 里初始化（C# 编译器不允许 readonly 字段在 try/catch 里赋值）。
    // 初始化失败会直接退出程序，所以使用时它们一定已经被正确赋值。
    private SimpleLogger _logger = null!;
    private EventDatabase _database = null!;
    private SentinelEngine _engine = null!;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;   // 系统托盘图标
    private bool _reallyExit;                               // true = 用户点了“退出”；false = 只是收进托盘
    private bool _shownMinimizedTip;                        // “已收进托盘”的气泡提示只弹一次

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // 语言必须最先定下来，后面的界面文字才找得到正确的字典
        _settings = AppSettings.Load();
        Loc.SetLanguage(_settings.Language);

        try
        {
            // 数据目录 = 程序目录\data（数据库、日志、设置都在这里，且自动排除监控）
            var dataDir = SelfGuard.CreateDefault().DataDirectory;
            _logger = new SimpleLogger(Path.Combine(dataDir, "sentinel.log"));
            _database = new EventDatabase(Path.Combine(dataDir, "sentinel.db"));

            // 审查修复（F-17）：按保留天数清理历史事件（默认 30 天），防止表无限膨胀。
            // 清理失败不影响启动，单独 try/catch。
            try
            {
                int retention = Math.Max(1, _settings.RetentionDays);
                _database.DeleteEventsBefore(DateTime.UtcNow.AddDays(-retention));
            }
            catch { /* 清理失败不影响启动 */ }

            _engine = new SentinelEngine(_database, PathFilter.CreateDefault(), new SelfGuard(dataDir), _logger);
            _engine.EventCommitted += OnEngineEventCommitted;   // 注意：后台线程回调！
            _engine.MessageRaised += OnEngineMessage;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.TF("msg.startupFailed", ex.Message),
                Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);   // 数据库都建不了，程序没法用，直接退出
        }

        // 实时列表的“关键字过滤视图”：过滤框打字时，列表即时增减
        _liveView = CollectionViewSource.GetDefaultView(_liveRows);
        _liveView.Filter = row => MatchesKeyword((EventRow)row);

        // 实时列表每变化一次（新事件进来 / 被裁剪），都检查要不要显示“空状态”提示
        _liveRows.CollectionChanged += (sender, args) => UpdateEmptyState();

        // 图标：标题栏和托盘共用同一个“代码画出来”的图标
        Icon = AppIconFactory.CreateWindowIcon();
        InitTrayIcon();

        // 恢复上次选择的语言（下拉框选中项变化会触发 SelectionChanged，但语言相同会直接跳过）
        LangBox.SelectedIndex = string.Equals(_settings.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        // 订阅语言切换事件并手动刷新一次界面文字
        //（启动时的 SetLanguage 发生在订阅之前，不会自动触发，所以要手动调一次）
        Loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        Loaded += OnMainWindowLoaded;
        StateChanged += OnWindowStateChanged;
        _logger.Info("程序启动完成。");
    }

    // ==================== 界面绑定用的集合 ====================

    public ObservableCollection<EventRow> LiveRows => _liveRows;

    // ==================== 启动与关闭 ====================

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        LoadDirectoryListFromDatabase();
        _engine.Start();      // 从数据库读目录清单，逐个开始监控
        UpdateStatus();
        UpdateEmptyState();
        _logger.Info("监控已开始。");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_reallyExit)
        {
            return;   // 托盘菜单点了“退出”：放行，交给 OnClosed 清理
        }

        // 点窗口的“×”不等于退出程序：收进托盘，监控继续（这正是文件哨兵的用处）
        e.Cancel = true;
        Hide();
        ShowMinimizedBalloonOnce();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _logger.Info("程序退出，开始清理。");

        _engine.EventCommitted -= OnEngineEventCommitted;
        _engine.MessageRaised -= OnEngineMessage;
        _engine.Stop();       // 停止所有探头，收尾入库
        _engine.Dispose();
        _database.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        Loc.LanguageChanged -= ApplyLocalization;

        Application.Current.Shutdown();
    }

    // ==================== 侧栏导航 ====================

    /// <summary>切换页面：事件监控页 ↔ 监控目录页（由导航 RadioButton 的选中触发）。</summary>
    private void NavRadio_Checked(object sender, RoutedEventArgs e)
    {
        // XAML 解析过程中首个导航项的 Checked 事件可能早于内容区元素创建完成，保险起见判空
        if (PanelEvents == null || PanelDirs == null)
        {
            return;
        }

        var eventsSelected = ReferenceEquals(sender, NavEventsRadio);
        PanelEvents.Visibility = eventsSelected ? Visibility.Visible : Visibility.Collapsed;
        PanelDirs.Visibility = eventsSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==================== 监控目录管理 ====================

    /// <summary>启动时从数据库把目录清单读出来，填到列表。</summary>
    private void LoadDirectoryListFromDatabase()
    {
        foreach (var dir in _database.GetDirectories())
        {
            var row = new DirectoryRow { Path = dir.Path, Enabled = dir.Enabled };
            row.PropertyChanged += OnDirectoryRowChanged;   // 勾选变化 → 开关监控
            _directories.Add(row);
        }
    }

    /// <summary>勾选框状态变化：打开或暂停这个目录的监控。</summary>
    private void OnDirectoryRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DirectoryRow.Enabled) || sender is not DirectoryRow row)
        {
            return;
        }

        try
        {
            _engine.SetDirectoryEnabled(row.Path, row.Enabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.TF("msg.operationFailed", ex.Message),
                Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateStatus();
    }

    /// <summary>“添加目录”按钮：弹出系统文件夹选择框。</summary>
    private void AddDirButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("dir.pickTitle"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog() != true)
        {
            return;   // 用户点了取消
        }

        string path;
        try
        {
            path = SentinelEngine.NormalizeDirectoryPath(dialog.FolderName);
        }
        catch
        {
            path = dialog.FolderName;
        }

        // 同一个目录不允许加两次
        if (_directories.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Loc.T("dir.duplicate"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // 引擎会做两项检查：目录是否存在？是不是程序自己的数据目录？
            _engine.AddDirectory(path);

            var row = new DirectoryRow { Path = path, Enabled = true };
            row.PropertyChanged += OnDirectoryRowChanged;
            _directories.Add(row);
            _logger.Info($"用户添加监控目录：{path}");
        }
        catch (SelfWatchException)
        {
            MessageBox.Show(Loc.T("dir.selfWatch"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (WatchDirectoryNotFoundException)
        {
            MessageBox.Show(Loc.T("dir.notFound"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.TF("dir.addFailed", ex.Message), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateStatus();
        UpdateEmptyState();
    }

    /// <summary>“删除选中”按钮：从列表和数据库里移除选中的目录（历史记录保留）。</summary>
    private void RemoveDirButton_Click(object sender, RoutedEventArgs e)
    {
        if (DirectoryList.SelectedItem is not DirectoryRow row)
        {
            MessageBox.Show(Loc.T("dir.noSelection"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(Loc.T("dir.confirmRemove"), Loc.T("dir.removeTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _engine.RemoveDirectory(row.Path);
        row.PropertyChanged -= OnDirectoryRowChanged;
        _directories.Remove(row);
        _logger.Info($"用户移除监控目录：{row.Path}");
        UpdateStatus();
        UpdateEmptyState();
    }

    // ==================== 引擎回调（后台线程！） ====================

    private void OnEngineEventCommitted(FileEventRecord record)
    {
        // 【线程提醒】这里运行在引擎的后台线程上。
        // Dispatcher.InvokeAsync 把刷新界面的动作排队转回界面线程，绝不会跨线程碰控件。
        Dispatcher.InvokeAsync(() =>
        {
            _liveRows.Insert(0, EventRowFactory.FromRecord(record));   // 最新事件放最上面
            if (_liveRows.Count > MaxLiveRows)
            {
                _liveRows.RemoveAt(_liveRows.Count - 1);               // 太多了删最旧的
            }
            UpdateStatus();
        });
    }

    private void OnEngineMessage(EngineMessageCode code, string detail)
    {
        // 引擎提示消息：同样从后台线程转回界面线程，翻译成当前语言后显示在状态胶囊里
        Dispatcher.InvokeAsync(() =>
        {
            StatusPillText.Text = code switch
            {
                EngineMessageCode.BufferOverflow => Loc.TF("msg.bufferOverflow", detail),
                EngineMessageCode.DirMissing => Loc.TF("msg.dirMissing", detail),
                _ => Loc.TF("msg.startFailed", detail)
            };
            _logger.Warn($"引擎消息：{code} {detail}");
        });
    }

    // ==================== 过滤 / 回看 ====================

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _liveView.Refresh();   // 实时列表立即应用新关键字

        // 如果正处于“按天回看”模式，用新关键字重新查一次
        if (HistoryDatePicker.SelectedDate is DateTime date)
        {
            LoadHistoryForDate(date);
        }
    }

    private void HistoryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryDatePicker.SelectedDate is DateTime date)
        {
            LoadHistoryForDate(date);
        }
        else
        {
            BackToLiveMode();
        }
    }

    private void BackToLiveButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryDatePicker.SelectedDate = null;   // 会触发 SelectedDateChanged → BackToLiveMode
        BackToLiveMode();
    }

    /// <summary>按天回看：把用户选的“本地一整天”换算成 UTC 时间范围，去数据库查询。</summary>
    private void LoadHistoryForDate(DateTime date)
    {
        var fromUtc = date.Date.ToUniversalTime();               // 本地当天 0 点 → UTC
        var toUtc = date.Date.AddDays(1).ToUniversalTime();      // 本地第二天 0 点 → UTC
        var keyword = string.IsNullOrWhiteSpace(FilterBox.Text) ? null : FilterBox.Text.Trim();

        try
        {
            _historyRows.Clear();
            foreach (var record in _database.QueryEvents(fromUtc, toUtc, keyword))
            {
                _historyRows.Add(EventRowFactory.FromRecord(record));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.TF("msg.queryFailed", ex.Message),
                Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        EventGrid.ItemsSource = _historyRows;   // 切到回看列表
        UpdateStatus();
        UpdateEmptyState();
    }

    /// <summary>回到实时模式：表格数据源换回实时列表（带关键字过滤）。</summary>
    private void BackToLiveMode()
    {
        EventGrid.ItemsSource = _liveView;
        UpdateStatus();
        UpdateEmptyState();
    }

    /// <summary>关键字过滤规则：事件显示行里包含关键字就放行（重命名行里旧路径也在）。</summary>
    private bool MatchesKeyword(EventRow row)
    {
        var keyword = FilterBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return true;
        }
        return row.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== 语言切换 ====================

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        var code = item.Tag as string ?? "zh-CN";
        if (string.Equals(Loc.CurrentLanguage, code, StringComparison.OrdinalIgnoreCase))
        {
            return;   // 语言没变（比如启动时恢复选中项）就不用刷新
        }

        Loc.SetLanguage(code);        // 触发 LanguageChanged → ApplyLocalization 刷新全部文字
        _settings.Language = code;
        _settings.Save();             // 持久化，下次启动记住选择
        _logger.Info($"界面语言切换为：{code}");
    }

    /// <summary>把界面上所有文字按当前语言重新设置一遍（启动时和每次切换语言都调用）。</summary>
    private void ApplyLocalization()
    {
        Title = Loc.T("app.title");

        // 侧栏
        TitleText.Text = Loc.T("app.shortName");
        NavEventsText.Text = Loc.T("nav.events");
        NavDirsText.Text = Loc.T("nav.dirs");
        LangBox.ToolTip = Loc.T("toolbar.languageTip");

        // 事件页工具条
        FilterLabel.Text = Loc.T("toolbar.filterLabel");
        FilterBox.ToolTip = Loc.T("toolbar.filterTip");
        HistoryLabel.Text = Loc.T("toolbar.historyLabel");
        HistoryDatePicker.ToolTip = Loc.T("toolbar.historyTip");
        BackToLiveButton.Content = Loc.T("toolbar.backToLive");

        // 目录页工具条
        AddDirButton.Content = Loc.T("dir.add");
        RemoveDirButton.Content = Loc.T("dir.remove");

        // 事件表格表头
        TimeColumn.Header = Loc.T("col.time");
        KindColumn.Header = Loc.T("col.kind");
        PathColumn.Header = Loc.T("col.path");
        ProcessColumn.Header = Loc.T("col.process");

        // 已显示的行：类型和“未知进程”重新翻译一遍（时间、路径本来就不随语言变）
        RefreshRowTranslations(_liveRows);
        RefreshRowTranslations(_historyRows);

        // 托盘
        RebuildTrayMenu();
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = Loc.T("tray.tooltip");
        }

        UpdateStatus();
        UpdateEmptyState();
    }

    private static void RefreshRowTranslations(IEnumerable<EventRow> rows)
    {
        foreach (var row in rows)
        {
            row.Kind = Loc.T("kind." + row.Record.Kind.ToStorageName().ToLowerInvariant());
            row.Process = string.IsNullOrEmpty(row.Record.ProcessName)
                ? Loc.T("process.unknown")
                : row.Record.ProcessName;
        }
    }

    // ==================== 系统托盘 ====================

    private void InitTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIconFactory.CreateTrayIcon(),
            Visible = true
        };
        RebuildTrayMenu();
        _notifyIcon.DoubleClick += (sender, args) => ShowFromTray();
    }

    /// <summary>托盘菜单是代码生成的（不是 XAML），语言切换时要整个重建。</summary>
    private void RebuildTrayMenu()
    {
        if (_notifyIcon == null)
        {
            return;
        }
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("tray.open"), null, (sender, args) => ShowFromTray());
        menu.Items.Add(Loc.T("tray.exit"), null, (sender, args) => ExitApplication());
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void ShowFromTray()
    {
        // 保险起见也走一次 Dispatcher：托盘菜单事件几乎总在界面线程，但多一层保护不亏
        Dispatcher.InvokeAsync(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // 最小化 = 收进托盘继续监控
            Hide();
            ShowMinimizedBalloonOnce();
        }
    }

    private void ShowMinimizedBalloonOnce()
    {
        if (_shownMinimizedTip || _notifyIcon == null)
        {
            return;
        }
        _shownMinimizedTip = true;
        _notifyIcon.ShowBalloonTip(3000,
            Loc.T("tray.balloonTitle"),
            Loc.T("tray.balloonText"),
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        Close();   // 走 OnClosing（放行）→ OnClosed（统一清理）
    }

    // ==================== 状态与空状态 ====================

    /// <summary>刷新侧栏底部状态胶囊（模式 + 监控目录数 + 绿点）和事件页右上角的计数。</summary>
    private void UpdateStatus()
    {
        var enabledCount = 0;
        foreach (var directory in _directories)
        {
            if (directory.Enabled)
            {
                enabledCount++;
            }
        }

        var mode = HistoryDatePicker.SelectedDate is DateTime date
            ? Loc.TF("status.history", date.ToString("yyyy-MM-dd"))
            : Loc.T("status.live");

        StatusPillText.Text = $"{mode} | {Loc.TF("status.monitoring", enabledCount)}";
        CounterText.Text = Loc.TF("status.recorded", _liveRows.Count);
        StatusDot.Fill = _engine.IsRunning ? DotRunningBrush : DotIdleBrush;
    }

    /// <summary>
    /// 空状态提示：表格或列表没有内容时，在卡片正中显示灰字引导，
    /// 实时 / 回看 / 目录三个场景各有各的文案。
    /// </summary>
    private void UpdateEmptyState()
    {
        if (EventEmpty != null)
        {
            var inHistoryMode = HistoryDatePicker?.SelectedDate is DateTime;
            EventEmpty.Text = inHistoryMode ? Loc.T("empty.history") : Loc.T("empty.live");
            var rowCount = inHistoryMode ? _historyRows.Count : _liveRows.Count;
            EventEmpty.Visibility = rowCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (DirEmpty != null)
        {
            DirEmpty.Text = Loc.T("empty.dirs");
            DirEmpty.Visibility = _directories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
