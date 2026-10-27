using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;

namespace DeskPanel.Interop;

/// <summary>
/// 自己注册 OLE 拖放目标。
/// WPF 的 AllowDrop 在我们这种窗口上没把 DragEnter/Drop 送进来（实测一条事件都没有，
/// 拖动光标一直是「禁止」），所以直接实现 IDropTarget + RegisterDragDrop。
/// </summary>
internal sealed class FileDropTarget : IDropTarget
{
    private const uint DROPEFFECT_COPY = 1;
    private const uint DROPEFFECT_NONE = 0;
    private const short CF_HDROP = 15;

    private readonly Action<string[], Point> _onDrop;
    private readonly Action<bool> _onHover;

    public FileDropTarget(Action<string[], Point> onDrop, Action<bool> onHover)
    {
        _onDrop = onDrop;
        _onHover = onHover;
    }

    public void DragEnter([MarshalAs(UnmanagedType.Interface)] object data, uint keyState, Native.POINT pt, ref uint effect)
    {
        bool ok = HasFiles(data);
        effect = ok ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        _onHover?.Invoke(ok);
        Log.Write($"drop: IDropTarget.DragEnter files={ok}");
    }

    public void DragOver(uint keyState, Native.POINT pt, ref uint effect)
    {
        effect = _hovering ? DROPEFFECT_COPY : DROPEFFECT_NONE;
    }

    public void DragLeave()
    {
        _hovering = false;
        _onHover?.Invoke(false);
    }

    public void Drop([MarshalAs(UnmanagedType.Interface)] object data, uint keyState, Native.POINT pt, ref uint effect)
    {
        var files = Extract(data);
        effect = files.Count > 0 ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        _hovering = false;
        _onHover?.Invoke(false);

        Log.Write($"drop: IDropTarget.Drop 收到 {files.Count} 项");
        if (files.Count > 0) _onDrop?.Invoke(files.ToArray(), new Point(pt.X, pt.Y));
    }

    private bool _hovering;

    private void SetHover(bool on)
    {
        if (_hovering == on) return;
        _hovering = on;
    }

    private static bool HasFiles(object data)
    {
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject obj) return false;

        var fmt = new FORMATETC
        {
            cfFormat = CF_HDROP,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        try { return obj.QueryGetData(ref fmt) == 0; }
        catch { return false; }
    }

    private static List<string> Extract(object data)
    {
        var list = new List<string>();
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject obj) return list;

        var fmt = new FORMATETC
        {
            cfFormat = CF_HDROP,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        STGMEDIUM medium = default;
        try
        {
            obj.GetData(ref fmt, out medium);
            IntPtr hDrop = medium.unionmember;
            if (hDrop == IntPtr.Zero) return list;

            uint count = Native.DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var sb = new System.Text.StringBuilder(1024);
                if (Native.DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity) > 0)
                    list.Add(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            Log.Ex(ex);
        }
        finally
        {
            try { ReleaseStgMedium(ref medium); } catch { }
        }

        return list;
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    void DragEnter([MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, Native.POINT pt, ref uint pdwEffect);
    void DragOver(uint grfKeyState, Native.POINT pt, ref uint pdwEffect);
    void DragLeave();
    void Drop([MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, Native.POINT pt, ref uint pdwEffect);
}

internal static class OleDrop
{
    public const int S_OK = 0;
    public const int DRAGDROP_E_ALREADYREGISTERED = unchecked((int)0x80040101);
    public const int DRAGDROP_E_NOTREGISTERED = unchecked((int)0x80040100);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    private static IDropTarget _keepAlive;

    public static int Install(IntPtr hwnd, IDropTarget target)
    {
        try { OleInitialize(IntPtr.Zero); } catch { }

        int hr = RegisterDragDrop(hwnd, target);
        Log.Write($"drop: RegisterDragDrop hr=0x{hr:X8}");

        if (hr == DRAGDROP_E_ALREADYREGISTERED)
        {
            // WPF 已经注册了一个，但它在这个窗口上不往上传事件（实测一条都没有）。
            // 撤掉它，换成我们自己的。
            int revoke = RevokeDragDrop(hwnd);
            Log.Write($"drop: RevokeDragDrop hr=0x{revoke:X8}");
            hr = RegisterDragDrop(hwnd, target);
            Log.Write($"drop: 重新注册 hr=0x{hr:X8}");
        }

        if (hr == S_OK) _keepAlive = target;
        return hr;
    }

    public static void Uninstall(IntPtr hwnd)
    {
        try { RevokeDragDrop(hwnd); } catch { }
        _keepAlive = null;
    }
}
