using System.Runtime.InteropServices;
using System.Text;

namespace DeskPanel.Ui;

/// <summary>选文件夹（SHBrowseForFolder，够用且不用引入 WinForms）。</summary>
internal static class FolderPicker
{
    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint BIF_USENEWUI = 0x0040 | 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public string pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    public static string Pick(string title)
    {
        var bi = new BROWSEINFO
        {
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_USENEWUI,
        };

        IntPtr pidl = SHBrowseForFolderW(ref bi);
        if (pidl == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(1024);
            return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }
}
