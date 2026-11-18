using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// WPF 和 GDI+（画图标的 System.Drawing）里有些类重名（Color、Pen 等），
// 用别名明确指定：画图标用的是 GDI+ 的那一套。
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using Icon = System.Drawing.Icon;
using Pen = System.Drawing.Pen;
using SolidBrush = System.Drawing.SolidBrush;

namespace FolderSentinel.App;

/// <summary>
/// 程序图标工厂：图标不是从 .ico 文件读的，而是程序启动时用代码“画”出来的
/// （蓝色圆底 + 白色对勾，寓意“哨兵记录放行”）。这样仓库里就不需要二进制图标文件。
/// </summary>
internal static class AppIconFactory
{
    private static Icon? _trayIcon;          // GDI+ 图标：给系统托盘用
    private static ImageSource? _windowIcon; // WPF 图像：给窗口标题栏用

    /// <summary>生成托盘图标（32×32，生成一次缓存起来反复用）。</summary>
    public static Icon CreateTrayIcon()
    {
        if (_trayIcon != null)
        {
            return _trayIcon;
        }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 深灰蓝圆底：克制的强调色，符合 Windows 原生工具的气质
            using (var background = new SolidBrush(Color.FromArgb(255, 38, 99, 154)))
            {
                g.FillEllipse(background, 1, 1, 30, 30);
            }

            // 白色对勾
            using var check = new Pen(Color.White, 3.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(check, new PointF[] { new(9f, 16.5f), new(14.5f, 22f), new(23f, 10.5f) });
        }

        // GetHicon 会生成一个独立的图标句柄（复制了像素数据），bitmap 可以安全释放。
        // 图标随程序全程使用，句柄不单独释放（进程退出时系统自动回收）。
        _trayIcon = Icon.FromHandle(bitmap.GetHicon());
        return _trayIcon;
    }

    /// <summary>生成窗口标题栏图标（WPF 的 ImageSource 形式）。</summary>
    public static ImageSource CreateWindowIcon()
    {
        if (_windowIcon != null)
        {
            return _windowIcon;
        }

        _windowIcon = Imaging.CreateBitmapSourceFromHIcon(
            CreateTrayIcon().Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        _windowIcon.Freeze();   // 冻结后允许跨线程只读访问
        return _windowIcon;
    }
}
