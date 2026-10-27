using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DeskPanel.Interop;

/// <summary>
/// 把窗口“压在桌面之上、所有应用之下”。
/// 早期版本用 SetWindowLongPtr(GWLP_HWNDPARENT, Progman) 认领 owner —— 实测这会让
/// 资源管理器拖不动桌面图标（跨进程 owner 会干扰 shell 输入），已弃用。
/// 现在改为：不认领 owner，只把自己插到 Progman 正上方。
/// </summary>
internal sealed class DesktopLayer : IDisposable
{
    private readonly Window _window;
    private readonly bool _pinToDesktop;
    private readonly bool _noActivate;

    private IntPtr _hwnd;
    private System.Windows.Threading.DispatcherTimer _timer;

    public DesktopLayer(Window window, bool pinToDesktop, bool noActivate)
    {
        _window = window;
        _pinToDesktop = pinToDesktop;
        _noActivate = noActivate;
    }

    public void Attach()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();

        long ex = Native.GetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW;
        if (_noActivate) ex |= Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        Place();

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick += (_, _) => Pin();
        _timer.Start();
        Pin();

        Log($"attach hwnd=0x{_hwnd.ToInt64():X} pin={_pinToDesktop} noact={_noActivate}");
    }

    /// <summary>
    /// 用「面板中心点下面是谁」判断该置顶还是压底：
    /// - 下面是桌面（Progman/WorkerW/DefView）或就是自己 → 置顶；
    /// - 下面是别的应用窗口 → 压到底部（被应用盖住）。
    /// 比看前台窗口可靠：Win+D 之后前台经常还停在原来的应用上，但桌面上其实已经空了。
    /// </summary>
    private void Pin()
    {
        if (!_pinToDesktop || _hwnd == IntPtr.Zero) return;

        var rect = new Native.RECT();
        if (!Native.GetWindowRect(_hwnd, ref rect)) return;

        var center = new Native.POINT
        {
            X = (rect.Left + rect.Right) / 2,
            Y = (rect.Top + rect.Bottom) / 2,
        };

        IntPtr under = Native.WindowFromPoint(center);

        // 已经在自己上面 → 不用动
        if (under == _hwnd) return;

        bool shouldBeTop = under == IntPtr.Zero || IsDesktopHost(under);

        IntPtr after = shouldBeTop ? Native.HWND_TOP : Native.HWND_BOTTOM;
        if (after == _lastAfter) return;

        // 带 SWP_SHOWWINDOW：实测不带这个标志时，置顶对分层窗口不生效（会留在壁纸下面）
        Native.SetWindowPos(_hwnd, after, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        // 分层窗口被压在壁纸下面时，置顶后 DWM 不一定重绘 → 强制失效一次
        if (shouldBeTop)
        {
            Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
            Native.UpdateWindow(_hwnd);
        }

        if (_logBudget > 0)
        {
            _logBudget--;
            Log($"pin: under=0x{under.ToInt64():X} [{ClassName(under)}] -> {(shouldBeTop ? "TOP" : "BOTTOM")}");
        }

        _lastAfter = after;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(96);
        Native.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private int _logBudget = 12;
    private IntPtr _lastAfter = (IntPtr)(-1);

    /// <summary>桌面宿主窗口（Progman / WorkerW / SHELLDLL_DefView 及其子窗口）。</summary>
    private static bool IsDesktopHost(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(128);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32";
        }
        catch { return false; }
    }

    /// <summary>当前主屏工作区（DIP）。换屏 / 改缩放时会变，用来做自适应。</summary>
    public Rect WorkArea => SystemParameters.WorkArea;

    /// <summary>
    /// 贴到「面板该在的那块屏」的右边缘、占满工作区高度。
    /// 全程用物理像素（GetMonitorInfo + SetWindowPos）算，不依赖 SystemParameters.WorkArea：
    /// 一旦插了虚拟显示器（比如远程串流软件创建的那块屏成了主屏），
    /// WorkArea 会指向那块虚拟屏，面板就被摆到物理屏幕之外 —— 看起来就像“没开机自启”。
    ///
    /// 选屏顺序：鼠标所在屏 → 主屏。宽度上限仍是工作区宽的 30%。
    /// </summary>
    public void Place()
    {
        // 1) 选屏：优先鼠标所在那块，取不到再退回主屏
        IntPtr mon = IntPtr.Zero;
        if (Native.GetCursorPos(out var pt)) mon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) mon = Native.MonitorFromPoint(new Native.POINT(), Native.MONITOR_DEFAULTTOPRIMARY);

        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (mon == IntPtr.Zero || !Native.GetMonitorInfoW(mon, ref mi))
        {
            PlaceFallbackDip();
            return;
        }

        int workW = mi.rcWork.Right - mi.rcWork.Left;
        int workH = mi.rcWork.Bottom - mi.rcWork.Top;
        if (workW <= 0 || workH <= 0)
        {
            PlaceFallbackDip();
            return;
        }

        // 2) 这块屏的 DPI 缩放（拿不到就按 100%）
        double scale = 1.0;
        try
        {
            if (Native.GetDpiForMonitor(mon, Native.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;
        }
        catch { }

        // 3) 宽度：配置的 DIP 宽 → 物理宽，且不超过工作区宽的 30%
        double widthDip = Math.Clamp(_window.Width, 200, 420);
        int widthPx = (int)Math.Round(widthDip * scale);
        int maxPx = (int)Math.Round(workW * 0.30);
        if (widthPx > maxPx) widthPx = Math.Max((int)Math.Round(200 * scale), maxPx);

        int x = mi.rcWork.Right - widthPx;
        int y = mi.rcWork.Top;

        // 4) 摆好（物理像素，绕开 WPF 的 DIP 换算）；WPF 侧同步尺寸，避免内容按旧宽度排版
        try
        {
            _window.Width = widthPx / scale;
            _window.Height = workH / scale;
        }
        catch { }

        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, widthPx, workH,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        var r = new Native.RECT();
        Native.GetWindowRect(_hwnd, ref r);

        bool onScreen = r.Right > mi.rcMonitor.Left && r.Left < mi.rcMonitor.Right &&
                        r.Bottom > mi.rcMonitor.Top && r.Top < mi.rcMonitor.Bottom;

        Log($"place mon=({mi.rcMonitor.Left},{mi.rcMonitor.Top})-({mi.rcMonitor.Right},{mi.rcMonitor.Bottom}) " +
            $"work=({mi.rcWork.Left},{mi.rcWork.Top})-({mi.rcWork.Right},{mi.rcWork.Bottom}) " +
            $"phys=({r.Left},{r.Top},{r.Right},{r.Bottom}) scale={scale:0.##} onScreen={onScreen} vis={Native.IsWindowVisible(_hwnd)}");
    }

    /// <summary>取不到显示器信息时的退路：仍按 WPF 的 DIP 工作区贴右边缘。</summary>
    private void PlaceFallbackDip()
    {
        Rect wa = SystemParameters.WorkArea;

        double width = _window.Width;
        double maxWidth = Math.Max(240, wa.Width * 0.30);
        if (width > maxWidth) width = maxWidth;
        if (width < 200) width = 200;
        _window.Width = width;

        _window.Left = wa.Right - width;
        _window.Top = wa.Top;
        _window.Height = wa.Bottom - wa.Top;

        Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        var r = new Native.RECT();
        Native.GetWindowRect(_hwnd, ref r);
        Log($"place(fallback DIP) phys=({r.Left},{r.Top},{r.Right},{r.Bottom}) vis={Native.IsWindowVisible(_hwnd)}");
    }

    public IntPtr Handle => _hwnd;

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(System.IO.Path.Combine(Config.Dir, "diag.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
