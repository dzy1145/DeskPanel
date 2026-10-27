using System.Diagnostics;
using System.IO;
using DeskPanel.Interop;

namespace DeskPanel.Services;

/// <summary>
/// 通用的「服务启动器」：每条配置自己声明启动命令和判断进程，
/// 面板只负责显示在跑/没跑、点一下启动或停止。不含任何特定软件的硬编码。
/// </summary>
internal static class ServiceRunner
{
    /// <summary>探测每条服务当前是否在跑。</summary>
    public static bool[] Probe(IReadOnlyList<ServiceItem> items)
    {
        var result = new bool[items.Count];
        if (items.Count == 0) return result;

        try
        {
            var procs = ProcessProbe.Snapshot();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var names = item.ProcessNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(Normalize)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var p in procs)
                {
                    if (names.Count > 0)
                    {
                        if (!names.Contains(Normalize(p.Exe))) continue;
                    }

                    if (!string.IsNullOrWhiteSpace(item.StopMatch))
                    {
                        string cmd = ProcessProbe.TryGetCmdLine(p.Pid);
                        if (!cmd.Contains(item.StopMatch, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    result[i] = true;
                    break;
                }
            }
        }
        catch { }

        return result;
    }

    private static string Normalize(string name)
    {
        string n = name.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n;
    }

    public static void Start(ServiceItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.StartCommand)) return;

        try
        {
            string command = Environment.ExpandEnvironmentVariables(item.StartCommand.Trim());
            string workDir = string.IsNullOrWhiteSpace(item.WorkingDir)
                ? SafeDir(command)
                : Environment.ExpandEnvironmentVariables(item.WorkingDir);

            string ext = Path.GetExtension(command).ToLowerInvariant();

            if (ext is ".ps1")
            {
                RunHidden("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{command}\" {item.StartArgs}".Trim(),
                    workDir);
                return;
            }

            if (ext is ".bat" or ".cmd")
            {
                RunHidden("cmd.exe", $"/c \"\"{command}\" {item.StartArgs}\"".Trim(), workDir);
                return;
            }

            // 其余当普通可执行文件；StartCommand 也可以直接写一条带参数的命令行
            string file = File.Exists(command) ? command : command;
            RunHidden(file, item.StartArgs ?? "", workDir);
        }
        catch { }
    }

    public static void Stop(ServiceItem item)
    {
        if (item == null) return;

        try
        {
            var names = item.ProcessNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (names.Count == 0 && string.IsNullOrWhiteSpace(item.StopMatch)) return;

            foreach (var p in ProcessProbe.Snapshot())
            {
                if (names.Count > 0 && !names.Contains(Normalize(p.Exe))) continue;

                if (!string.IsNullOrWhiteSpace(item.StopMatch))
                {
                    string cmd = ProcessProbe.TryGetCmdLine(p.Pid);
                    if (!cmd.Contains(item.StopMatch, StringComparison.OrdinalIgnoreCase)) continue;
                }

                Kill(p.Pid);
            }
        }
        catch { }
    }

    private static string SafeDir(string command)
    {
        try
        {
            string dir = Path.GetDirectoryName(command);
            return string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)
                ? Environment.CurrentDirectory
                : dir;
        }
        catch { return Environment.CurrentDirectory; }
    }

    private static void RunHidden(string file, string args, string workDir)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args ?? "")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? Environment.CurrentDirectory : workDir,
            };
            Process.Start(psi);
        }
        catch { }
    }

    private static void Kill(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            proc.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
