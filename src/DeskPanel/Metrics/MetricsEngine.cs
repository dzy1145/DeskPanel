using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskPanel.Interop;

namespace DeskPanel.Metrics;

internal sealed class DriveSample
{
    public string Name = "";
    public double Percent;
    public double FreeGb;
    public double TotalGb;
}

internal sealed class Sample
{
    public double Cpu;
    public double Gpu;
    public double MemPercent;
    public double MemUsedGb;
    public double MemTotalGb;
    public List<DriveSample> Drives = new();

    // 温度/功耗：拿不到就是 null（CPU 走 PDH 热区，GPU 走 NVML）
    public double? CpuTemp;
    public double? GpuTemp;
    public double? GpuPower;
    public double? FanPercent;
}

/// <summary>PDH + 内存 API + DriveInfo，全部零管理员权限。</summary>
internal sealed class MetricsEngine : IDisposable
{
    private readonly int _periodMs;
    private readonly List<string> _driveLetters;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    private IntPtr _cpuQuery, _cpuCounter;
    private IntPtr _tempQuery, _tempCounter;
    private IntPtr _gpuQuery, _gpuCounter;
    private string _cpuPath = "";
    private volatile bool _disposed;
    private int _tick;
    private double _lastGpu;
    private double? _gpuTemp, _gpuPower, _fan;

    public event Action<Sample> Sampled;

    public MetricsEngine(int periodMs, List<string> driveLetters)
    {
        _periodMs = Math.Max(500, periodMs);
        _driveLetters = driveLetters ?? new List<string>();
        OpenCounters();

        // 预热：PDH 第一次采集没有有效值
        try { Native.PdhCollectQueryData(_cpuQuery); Native.PdhCollectQueryData(_gpuQuery); } catch { }

        _timer = new System.Threading.Timer(_ => Tick(), null, 600, _periodMs);
    }

    private void OpenCounters()
    {
        if (Native.PdhOpenQueryW(null, IntPtr.Zero, out _cpuQuery) != 0) return;

        string[] cpuCandidates =
        {
            @"\Processor Information(_Total)\% Processor Utility",
            @"\Processor Information(_Total)\% Processor Time",
        };
        foreach (var p in cpuCandidates)
        {
            if (Native.PdhAddEnglishCounterW(_cpuQuery, p, IntPtr.Zero, out _cpuCounter) == 0)
            {
                _cpuPath = p;
                break;
            }
            _cpuCounter = IntPtr.Zero;
        }

        if (Native.PdhOpenQueryW(null, IntPtr.Zero, out _gpuQuery) != 0) return;
        if (Native.PdhAddEnglishCounterW(_gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) != 0)
            _gpuCounter = IntPtr.Zero;

        // ACPI 热区温度（Kelvin）。WMI 的 MSAcpi_ThermalZoneTemperature 在这台机器上给的是陈旧值，
        // 这个 PDH 计数器是实时的，而且不需要管理员权限。
        if (Native.PdhOpenQueryW(null, IntPtr.Zero, out _tempQuery) == 0)
        {
            if (Native.PdhAddEnglishCounterW(_tempQuery, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _tempCounter) != 0)
                _tempCounter = IntPtr.Zero;
        }
    }

    private void Tick()
    {
        if (_disposed) return;
        try
        {
            // GPU 的 PDH 数组枚举比 CPU 贵，2 秒取一次，中间沿用上次值
            if ((_tick++ & 1) == 0)
            {
                _lastGpu = ReadGpu();
                Nvml.Read(out _gpuTemp, out _gpuPower, out _fan);
            }

            var s = new Sample
            {
                Cpu = ReadCpu(),
                Gpu = _lastGpu,
                CpuTemp = ReadCpuTemp(),
                GpuTemp = _gpuTemp,
                GpuPower = _gpuPower,
                FanPercent = _fan,
            };

            var ms = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                s.MemPercent = ms.dwMemoryLoad;
                s.MemTotalGb = ms.ullTotalPhys / 1024.0 / 1024 / 1024;
                s.MemUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1024.0 / 1024 / 1024;
            }

            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                string letter = d.Name.TrimEnd('\\');   // "C:"
                if (_driveLetters.Count > 0 &&
                    !_driveLetters.Any(x => string.Equals(x.TrimEnd('\\', ':') + ":", letter, StringComparison.OrdinalIgnoreCase)))
                    continue;

                double total = d.TotalSize;
                if (total <= 0) continue;
                double free = d.TotalFreeSpace;
                s.Drives.Add(new DriveSample
                {
                    Name = letter,
                    Percent = (total - free) / total * 100.0,
                    FreeGb = free / 1024.0 / 1024 / 1024,
                    TotalGb = total / 1024.0 / 1024 / 1024,
                });
            }

            if (_disposed) return;
            Sampled?.Invoke(s);
        }
        catch { }
    }

    private double ReadCpu()
    {
        if (_cpuCounter == IntPtr.Zero) return 0;
        lock (_gate)
        {
            if (Native.PdhCollectQueryData(_cpuQuery) != 0) return 0;
            if (Native.PdhGetFormattedCounterValue(_cpuCounter, Native.PDH_FMT_DOUBLE, out _, out var v) != 0) return 0;
            return Clamp(v.doubleValue, 0, 100);
        }
    }

    /// <summary>CPU/主板温度：取所有热区实例的最大值（Kelvin → °C）。</summary>
    private double? ReadCpuTemp()
    {
        if (_tempCounter == IntPtr.Zero) return null;

        lock (_gate)
        {
            if (Native.PdhCollectQueryData(_tempQuery) != 0) return null;

            int size = 0, count = 0;
            int rc = Native.PdhGetFormattedCounterArrayW(_tempCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (rc != Native.PDH_MORE_DATA || size <= 0) return null;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = Native.PdhGetFormattedCounterArrayW(_tempCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, buf);
                if (rc != 0) return null;

                double best = double.MinValue;
                int itemSize = Marshal.SizeOf<Native.PDH_FMT_COUNTERVALUE_ITEM>();

                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<Native.PDH_FMT_COUNTERVALUE_ITEM>(buf + i * itemSize);
                    if (item.FmtValue.CStatus != 0) continue;

                    double c = item.FmtValue.doubleValue - 273.15;
                    if (c > 5 && c < 120 && c > best) best = c;
                }

                return best == double.MinValue ? null : best;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    private double ReadGpu()
    {
        if (_gpuCounter == IntPtr.Zero) return 0;
        lock (_gate)
        {
            if (Native.PdhCollectQueryData(_gpuQuery) != 0) return 0;

            int size = 0, count = 0;
            int rc = Native.PdhGetFormattedCounterArrayW(_gpuCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (rc != Native.PDH_MORE_DATA || size <= 0) return 0;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = Native.PdhGetFormattedCounterArrayW(_gpuCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, buf);
                if (rc != 0) return 0;

                int itemSize = Marshal.SizeOf<Native.PDH_FMT_COUNTERVALUE_ITEM>();
                var perAdapter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < count; i++)
                {
                    IntPtr p = buf + i * itemSize;
                    var item = Marshal.PtrToStructure<Native.PDH_FMT_COUNTERVALUE_ITEM>(p);
                    string inst = Marshal.PtrToStringUni(item.szName) ?? "";
                    if (inst.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (item.FmtValue.CStatus != 0) continue;

                    string key = AdapterKey(inst);
                    perAdapter.TryGetValue(key, out double acc);
                    perAdapter[key] = acc + item.FmtValue.doubleValue;
                }

                if (perAdapter.Count == 0) return 0;
                return Clamp(perAdapter.Values.Max(), 0, 100);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>实例名形如 pid_1234_luid_0x00000000_0x0000C4AF_phys_0_eng_0_engtype_3D</summary>
    private static string AdapterKey(string inst)
    {
        int luid = inst.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        int phys = inst.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
        if (luid >= 0 && phys > luid) return inst.Substring(luid, phys - luid);
        return "default";
    }

    private static double Clamp(double v, double lo, double hi)
        => double.IsNaN(v) || double.IsInfinity(v) ? lo : Math.Min(hi, Math.Max(lo, v));

    public string CpuPath => _cpuPath;

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();

        // 关闭句柄前拿锁，避免和正在跑的采集撞上
        lock (_gate)
        {
            try
            {
                if (_cpuQuery != IntPtr.Zero) Native.PdhCloseQuery(_cpuQuery);
                if (_gpuQuery != IntPtr.Zero) Native.PdhCloseQuery(_gpuQuery);
                if (_tempQuery != IntPtr.Zero) Native.PdhCloseQuery(_tempQuery);
                _cpuQuery = _gpuQuery = _tempQuery = IntPtr.Zero;
            }
            catch { }
        }
    }
}
