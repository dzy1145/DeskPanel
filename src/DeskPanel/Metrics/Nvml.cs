using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

namespace DeskPanel.Metrics;

/// <summary>
/// NVIDIA 用户态 NVML：温度/功耗/风扇都不用管理员权限（风扇在笔记本上通常报 0 个）。
/// 非 NVIDIA 或驱动不支持时静默降级。
/// </summary>
internal static class Nvml
{
    private const string Dll = "nvml.dll";

    /// <summary>功耗上限（毫瓦）：桌面旗舰卡也就 600 W 左右，1500 W 以上一定是驱动返回的垃圾值。</summary>
    private const uint MaxPowerMw = 1_500_000;

    private static DateTime _lastBadPowerLog = DateTime.MinValue;

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

    /// <summary>异常功耗值只写一次日志（每分钟最多一条），免得刷屏。</summary>
    private static void LogBadPower(uint first, uint second)
    {
        try
        {
            if ((DateTime.Now - _lastBadPowerLog).TotalMinutes < 1) return;
            _lastBadPowerLog = DateTime.Now;
            Log.Write($"nvml: 功耗读数异常 raw={first} mW / retry={second} mW，已忽略");
        }
        catch { }
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

            // 功耗：正常是毫瓦。某些驱动/低功耗状态下会返回离谱值（实测见过 4104637 mW ≈ 4104 W），
            // 超过 1500 W 一律视为无效，宁可显示 -- 也不要把 4213W 写到脸上。
            if (nvmlDeviceGetPowerUsage(_dev, out uint mw) == 0)
            {
                if (mw > 0 && mw <= MaxPowerMw)
                {
                    powerW = mw / 1000.0;
                }
                else
                {
                    Thread.Sleep(30);
                    if (nvmlDeviceGetPowerUsage(_dev, out uint retry) == 0 && retry > 0 && retry <= MaxPowerMw)
                    {
                        powerW = retry / 1000.0;
                    }
                    else
                    {
                        LogBadPower(mw, retry);
                    }
                }
            }

            if (nvmlDeviceGetNumFans(_dev, out uint fans) == 0 && fans > 0 &&
                nvmlDeviceGetFanSpeed_v2(_dev, 0, out uint pct) == 0)
                fanPercent = pct;
        }
        catch { }
    }
}