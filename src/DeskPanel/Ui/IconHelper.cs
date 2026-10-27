using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPanel.Ui;

internal static class IconHelper
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string path, uint fileAttributes, ref SHFILEINFO info,
        uint infoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static ImageSource Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // 1) .lnk 先解到真实目标，再去取目标自己的图标
            string probe = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = ShortcutResolver.ResolveLnk(path);
                if (!string.IsNullOrWhiteSpace(resolved)) probe = resolved;
            }

            // 2) exe / dll 内置图标（ExtractIconEx）。这一步比 shell 图标可靠：
            //    某些程序（QQ）走 SHGetFileInfo 会退化成「未知文件」白纸。
            if (File.Exists(probe) && !Directory.Exists(probe))
            {
                var fromResource = FromExtractIcon(probe);
                if (fromResource != null) return fromResource;
            }

            // 3) 退回 shell 图标（文件夹、网址、.bat 这类靠它）
            return FromShell(path);
        }
        catch { return null; }
    }

    private static ImageSource FromExtractIcon(string file)
    {
        try
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            uint got = ExtractIconExW(file, 0, large, small, 1);
            if (got == 0 || large[0] == IntPtr.Zero)
            {
                if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
                return null;
            }

            var src = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
            return src;
        }
        catch { return null; }
    }

    private static ImageSource FromShell(string path)
    {
        uint flags = SHGFI_ICON | SHGFI_LARGEICON;
        uint attr;

        if (Directory.Exists(path))
        {
            attr = FILE_ATTRIBUTE_DIRECTORY;
        }
        else if (File.Exists(path))
        {
            attr = FILE_ATTRIBUTE_NORMAL;
        }
        else
        {
            attr = FILE_ATTRIBUTE_NORMAL;
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        var info = new SHFILEINFO();
        IntPtr res = SHGetFileInfoW(path, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        DestroyIcon(info.hIcon);
        return src;
    }
}
