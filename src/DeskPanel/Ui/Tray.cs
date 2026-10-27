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
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(2), "刷新启动项");
            Native.AppendMenuW(menu, Native.MF_STRING, new IntPtr(3), "打开配置文件");
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

    public static bool IsAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null;
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
            }
            else
            {
                key.DeleteValue(ValueName, false);
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
