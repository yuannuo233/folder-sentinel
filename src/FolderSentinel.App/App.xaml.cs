using System.Windows;
using System.Windows.Threading;

// 本项目同时启用了 WPF 和 WinForms（托盘图标用），
// 两边都有 Application 和 MessageBox，用别名明确指定用 WPF 的版本。
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace FolderSentinel.App;

/// <summary>
/// WPF 程序入口类。这里只做一件事：挂一个“全局兜底异常处理”，
/// 任何没被接住的异常都弹一条本地化的提示框，而不是让程序悄悄闪退。
/// 其他初始化（数据库、引擎、界面）都在 MainWindow 里完成。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        MessageBox.Show(
            Loc.TF("msg.unexpected", args.Exception.Message),
            Loc.T("app.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // 标记为“已处理”，程序继续运行
        args.Handled = true;
    }
}
