using System.Runtime.InteropServices;

namespace DeskPanel.Interop;

/// <summary>Win11 亚克力 / 模糊，三级回退。</summary>
internal static class Acrylic
{
    public static string LastMode { get; private set; } = "solid";

    /// <param name="abgr">0xAABBGGRR</param>
    public static bool TryApply(IntPtr hwnd, int abgr)
    {
        // 贴边条不需要圆角
        int round = Native.DWMWCP_DONOTROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        foreach (int state in new[] { Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, Native.ACCENT_ENABLE_BLURBEHIND })
        {
            var accent = new Native.AccentPolicy
            {
                AccentState = state,
                AccentFlags = 2,
                GradientColor = abgr,
                AnimationId = 0,
            };

            int size = Marshal.SizeOf<Native.AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new Native.WindowCompositionAttributeData
                {
                    Attribute = Native.WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr,
                };
                if (Native.SetWindowCompositionAttribute(hwnd, ref data) != 0)
                {
                    LastMode = state == Native.ACCENT_ENABLE_ACRYLICBLURBEHIND ? "acrylic" : "blur";
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        LastMode = "solid";
        return false;
    }
}
