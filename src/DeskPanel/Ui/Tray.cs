using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using DeskPanel.Interop;

namespace DeskPanel.Ui;

internal sealed class Tray : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskPanel";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly IntPtr _hwnd;
    private readonly MainWindow _window;
    private Native.NOTIFYICONDATA _data;
    private IntPtr _icon;

    public Tray(IntPtr hwnd, MainWindow window)
    {
        _hwnd = hwnd;
        _window = window;

        _icon = LoadTrayIcon();
        Log.Write($"tray icon=0x{_icon.ToInt64():X}");
        _data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = Native.WM_TRAYICON,
            hIcon = _icon,
            szTip = "DeskPanel",
        };
        Native.Shell_NotifyIconW(Native.NIM_ADD, ref _data);
        Log.Write("tray added");
    }

    private static IntPtr LoadTrayIcon()
    {
        try
        {
            string dir = Config.Dir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "app.ico");

            if (!File.Exists(path))
            {
                var res = Application.GetResourceStream(new Uri("Resources/app.ico", UriKind.Relative));
                if (res?.Stream != null)
                {
                    using var stream = res.Stream;
                    using var fs = File.Create(path);
                    stream.CopyTo(fs);
                }
            }

            if (File.Exists(path))
                return Native.LoadImageW(IntPtr.Zero, path, Native.IMAGE_ICON, 16, 16, Native.LR_LOADFROMFILE);
        }
        catch { }
        return IntPtr.Zero;
    }

    public void OnMessage(int lParam)
    {
        if (lParam != Native.WM_RBUTTONUP && lParam != Native.WM_LBUTTONUP) return;

        IntPtr menu = Native.CreatePopupMenu();
        try
        {
            bool auto = IsAutostart();
            Native.AppendMenuW(menu, Native.MF_STRING | (auto ? Native.MF_CHECKED : Native.MF_UNCHECKED), new IntPtr(1), "开机自启");

            // 宽度子菜单
            IntPtr widthMenu = Native.CreatePopupMenu();
            int cfgWidth = _window.PanelWidth;
            foreach ((int id, int px) in new[] { (20, 260), (21, 300), (22, 340), (23, 380) })
            {
                uint flags = Native.MF_STRING | (cfgWidth == px ? Native.MF_CHECKED : Native.MF_UNCHECKED);
                Native.AppendMenuW(widthMenu, flags, new IntPtr(id), $"{px} DIP");
            }
            Native.AppendMenuW(menu, Native.MF_POPUP, widthMenu, "面板宽度");

            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(2), "刷新启动项");
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(5), "重新载入配置");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(3), "打开配置文件");
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(6), "打开配置目录");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(4), "退出");

            Native.GetCursorPos(out var pt);
            Native.SetForegroundWindow(_hwnd);
            int cmd = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_LEFTALIGN | Native.TPM_RIGHTBUTTON,
                pt.X, pt.Y, _hwnd, IntPtr.Zero);

            switch (cmd)
            {
                case 1:
                    SetAutostart(!auto);
                    break;
                case 2:
                    _window.ReloadStartupItems();
                    break;
                case 3:
                    TryOpen(Config.FilePath);
                    break;
                case 4:
                    _window.Quit();
                    break;
                case 5:
                    _window.ReloadFromConfig();
                    break;
                case 6:
                    try
                    {
                        Directory.CreateDirectory(Config.Dir);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Config.Dir}\"") { UseShellExecute = true });
                    }
                    catch { }
                    break;
                case 20:
                case 21:
                case 22:
                case 23:
                    _window.SetPanelWidth(cmd == 20 ? 260 : cmd == 21 ? 300 : cmd == 22 ? 340 : 380);
                    break;
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    private static void TryOpen(string path)
    {
        try
        {
            if (!File.Exists(path)) Config.Load().Save();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>是否真的会开机启动：Run 里有值，并且 StartupApproved 没被标成禁用（03）。</summary>
    public static bool IsAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValue(ValueName) == null) return false;

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, false);
            if (approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && state[0] == 0x03)
                return false;

            return true;
        }
        catch { return false; }
    }

    public static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;

            if (on)
            {
                string exe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);

                // 任务管理器把启动项关掉时会在 StartupApproved 里写 03（禁用），
                // 只写 Run 值不解除禁用，所以这里同时写回 02（启用）。
                using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey, true);
                approved?.SetValue(ValueName,
                    new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    RegistryValueKind.Binary);
            }
            else
            {
                key.DeleteValue(ValueName, false);

                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
                if (approved?.GetValue(ValueName) != null)
                {
                    // 03 + 时间戳 = 已禁用（和任务管理器一致）；没时间戳也照样生效
                    var stamp = new byte[12];
                    stamp[0] = 0x03;
                    BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(stamp, 4);
                    approved.SetValue(ValueName, stamp, RegistryValueKind.Binary);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { Native.Shell_NotifyIconW(Native.NIM_DELETE, ref _data); } catch { }
        if (_icon != IntPtr.Zero)
        {
            Native.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
