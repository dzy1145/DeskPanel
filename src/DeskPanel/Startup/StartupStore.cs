using System.IO;
using System.Text.Json;
using DeskPanel.Ui;
using Microsoft.Win32;

namespace DeskPanel.Startup;

public sealed class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Source { get; set; } = "Run";     // Run | 文件夹
    public bool Enabled { get; set; }
    public string LinkPath { get; set; } = "";      // 启动文件夹项用
}

/// <summary>
/// 读 HKCU Run + 用户启动文件夹；开关写 StartupApproved（与任务管理器同机制，只禁用不删除）。
/// </summary>
public static class StartupStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static List<StartupItem> Load()
    {
        var list = new List<StartupItem>();

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, false);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedRun, false);

            if (run != null)
            {
                foreach (var name in run.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string cmd = run.GetValue(name) as string ?? "";
                    var item = new StartupItem
                    {
                        Name = name,
                        Command = cmd,
                        Source = "Run",
                        Enabled = ReadEnabled(approved, name),
                    };
                    list.Add(item);
                }
            }
        }
        catch { }

        try
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedFolder, false);

            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "*.lnk"))
                {
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    string file = Path.GetFileName(f);
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Command = f,
                        Source = "文件夹",
                        LinkPath = f,
                        Enabled = ReadEnabled(approved, file),
                    });
                }
                foreach (var f in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".lnk") continue;
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (string.Equals(Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string file = Path.GetFileName(f);
                    list.Add(new StartupItem
                    {
                        Name = file,
                        Command = f,
                        Source = "文件夹",
                        LinkPath = f,
                        Enabled = ReadEnabled(approved, file),
                    });
                }
            }
        }
        catch { }

        return list.OrderBy(x => x.Source).ThenBy(x => x.Name).ToList();
    }

    private static bool ReadEnabled(RegistryKey approved, string valueName)
    {
        try
        {
            if (approved?.GetValue(valueName) is byte[] b && b.Length >= 1)
                return (b[0] & 1) == 0;   // 02/06 = 启用，03/0B = 禁用
        }
        catch { }
        return true;                       // 没有记录 = 启用
    }

    /// <summary>写 StartupApproved。成功返回 null，失败返回错误原因。</summary>
    public static string SetEnabled(StartupItem item, bool enabled)
    {
        string sub = item.Source == "Run" ? ApprovedRun : ApprovedFolder;
        string name = item.Source == "Run" ? item.Name : Path.GetFileName(item.LinkPath);

        var bytes = new byte[12];
        if (enabled)
        {
            bytes[0] = 0x02;
        }
        else
        {
            bytes[0] = 0x03;
            var ft = BitConverter.GetBytes(DateTime.Now.ToFileTime());
            Array.Copy(ft, 0, bytes, 4, 8);
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(sub, true);
            if (key == null) return "打不开 " + sub;

            try
            {
                key.SetValue(name, bytes, RegistryValueKind.Binary);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
            {
                // 旧值类型不是 REG_BINARY（别的工具写坏过）→ 删掉重写
                Log.Write($"startup: {name} 旧值不能直接覆盖（{ex.GetType().Name}），改为删掉重写");
                key.DeleteValue(name, false);
                key.SetValue(name, bytes, RegistryValueKind.Binary);
            }

            Log.Write($"startup: {name} -> {(enabled ? "启用" : "禁用")} ok");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 权限不足：不是代码写错，是当前进程 token 受限（例如被父进程以受限身份拉起）
            Log.Write($"startup: {name} 权限不足 :: {ex.Message}");
            return "权限不足。请托盘右键退出，再双击桌面「系统监控 DeskPanel」重新启动";
        }
        catch (Exception ex)
        {
            Log.Write($"startup: {name} 写入失败 :: {ex.GetType().Name} {ex.Message}");
            return ex.GetType().Name + " " + ex.Message;
        }
    }

    /// <summary>新增一个 Run 启动项（等价于在任务管理器里启用一个自启程序）。成功返回 null。</summary>
    public static string AddRun(string name, string command, string args)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command)) return "命令为空";
            command = command.Trim();

            using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (run == null) return "打不开 Run 键";

            string baseName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(command)
                : name.Trim();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "新建启动项";

            string unique = baseName;
            int n = 2;
            while (run.GetValue(unique) != null) unique = $"{baseName} ({n++})";

            string data = command.Contains(' ') && !command.StartsWith('"') ? $"\"{command}\"" : command;
            if (!string.IsNullOrWhiteSpace(args)) data += " " + args.Trim();

            run.SetValue(unique, data, RegistryValueKind.String);

            // 写完读回来确认真的落地了（受限进程会静默写不进去）
            string readBack = run.GetValue(unique) as string;
            if (readBack == null)
            {
                Log.Write($"startup: 新增 Run 项 {unique} 写入后读不到");
                return "写入后读不到值（可能权限受限）";
            }

            // 新项默认启用：写 02，避免继承同名的旧禁用记录
            using var approved = Registry.CurrentUser.CreateSubKey(ApprovedRun, true);
            approved?.SetValue(unique, new byte[12] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                RegistryValueKind.Binary);

            Log.Write($"startup: 新增 Run 项 {unique} = {data}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Write($"startup: 新增 Run 权限不足 :: {ex.Message}");
            return "权限不足。请托盘右键退出，再双击桌面「系统监控 DeskPanel」重新启动";
        }
        catch (Exception ex)
        {
            Log.Write($"startup: 新增 Run 失败 :: {ex.GetType().Name} {ex.Message}");
            return ex.GetType().Name + " " + ex.Message;
        }
    }

    /// <summary>在启动文件夹里生成一个 .lnk。成功返回 null。</summary>
    public static string AddLink(string target, string name, string args)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)) return "目标为空";

            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            Directory.CreateDirectory(folder);

            string baseName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(target) : name.Trim();
            baseName = Sanitize(baseName);
            string file = Path.Combine(folder, baseName + ".lnk");
            int n = 2;
            while (File.Exists(file)) file = Path.Combine(folder, $"{baseName} ({n++}).lnk");

            if (ShortcutFactory.CreateAt(target, file, args) == null) return "生成快捷方式失败";

            Log.Write($"startup: 新增启动文件夹项 {file}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"startup: 新增快捷方式失败 :: {ex.GetType().Name} {ex.Message}");
            return ex.GetType().Name + " " + ex.Message;
        }
    }

    /// <summary>移除一个启动项（Run 值 / 启动文件夹里的 .lnk）。成功返回 null。</summary>
    public static string Remove(StartupItem item)
    {
        try
        {
            if (item.Source == "Run")
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
                run?.DeleteValue(item.Name, false);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedRun, true);
                approved?.DeleteValue(item.Name, false);
                Log.Write($"startup: 移除 Run 项 {item.Name}");
            }
            else
            {
                if (File.Exists(item.LinkPath)) File.Delete(item.LinkPath);
                string file = Path.GetFileName(item.LinkPath);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedFolder, true);
                approved?.DeleteValue(file, false);
                Log.Write($"startup: 移除启动文件夹项 {item.LinkPath}");
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"startup: 移除失败 {item.Name} :: {ex.GetType().Name} {ex.Message}");
            return ex.GetType().Name + " " + ex.Message;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        string s = sb.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(s) ? "启动项" : s;
    }
}
