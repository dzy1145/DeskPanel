using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskPanel.Metrics;

/// <summary>
/// NVIDIA 用户态 NVML：温度/功耗/风扇都不用管理员权限（风扇在笔记本上通常报 0 个）。
/// 非 NVIDIA 或驱动不支持时静默降级。
/// </summary>
internal static class Nvml
{
    private const string Dll = "nvml.dll";

    [DllImport(Dll)] private static extern int nvmlInit_v2();
    [DllImport(Dll)] private static extern int nvmlShutdown();
    [DllImport(Dll)] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport(Dll)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
    [DllImport(Dll)] private static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensorType, out uint temp);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint milliwatts);
    [DllImport(Dll)] private static extern int nvmlDeviceGetNumFans(IntPtr dev, out uint fans);
    [DllImport(Dll)] private static extern int nvmlDeviceGetFanSpeed_v2(IntPtr dev, uint fan, out uint percent);
    [DllImport(Dll, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr dev, StringBuilder name, uint len);

    private static bool _tried;
    private static bool _ok;
    private static IntPtr _dev;
    private static string _name = "";

    public static string DeviceName => _name;

    private static bool EnsureReady()
    {
        if (_tried) return _ok;
        _tried = true;

        try
        {
            if (nvmlInit_v2() != 0) return false;

            if (nvmlDeviceGetCount_v2(out uint count) != 0 || count == 0) return false;

            for (uint i = 0; i < count; i++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(i, out var dev) != 0) continue;
                _dev = dev;

                var sb = new StringBuilder(96);
                if (nvmlDeviceGetName(dev, sb, 96) == 0) _name = sb.ToString();
                break;
            }

            _ok = _dev != IntPtr.Zero;
            if (!_ok) nvmlShutdown();
            return _ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读 GPU 温度（°C）、功耗（W）、风扇百分比；读不到的项返回 null。</summary>
    public static void Read(out double? tempC, out double? powerW, out double? fanPercent)
    {
        tempC = null;
        powerW = null;
        fanPercent = null;

        if (!EnsureReady()) return;

        try
        {
            if (nvmlDeviceGetTemperature(_dev, 0, out uint t) == 0) tempC = t;
            if (nvmlDeviceGetPowerUsage(_dev, out uint mw) == 0) powerW = mw / 1000.0;

            if (nvmlDeviceGetNumFans(_dev, out uint fans) == 0 && fans > 0 &&
                nvmlDeviceGetFanSpeed_v2(_dev, 0, out uint pct) == 0)
                fanPercent = pct;
        }
        catch { }
    }
}