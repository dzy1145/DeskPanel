using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeskPanel;

/// <summary>常用区里的一个磁贴。</summary>
public sealed class AppItem
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string Icon { get; set; } = "";
}

public sealed class LauncherCfg
{
    public int Columns { get; set; } = 3;
    public int MaxRows { get; set; } = 3;
    public List<AppItem> Apps { get; set; } = new();
}

/// <summary>
/// 服务启动器里的一个条目：名字 + 启动命令 + 用哪些进程名判断"在跑"。
/// 和具体软件无关，谁都能配。
/// </summary>
public sealed class ServiceItem
{
    public string Name { get; set; } = "";

    /// <summary>启动命令：exe / .bat / .cmd / .ps1 的完整路径，或整条命令行。</summary>
    public string StartCommand { get; set; } = "";

    public string StartArgs { get; set; } = "";

    /// <summary>工作目录，留空取启动命令所在目录。</summary>
    public string WorkingDir { get; set; } = "";

    /// <summary>判断"在跑"用的进程名（不带 .exe），例如 ["python", "node"]。</summary>
    public List<string> ProcessNames { get; set; } = new();

    /// <summary>停止时额外匹配的命令行关键字（可选）。</summary>
    public string StopMatch { get; set; } = "";
}

/// <summary>额度区配置。关掉就整块隐藏。</summary>
public sealed class UsageCfg
{
    /// <summary>是否显示额度区。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>显示哪种：go = OpenCode Go 三环；deepseek = DeepSeek 官方余额。</summary>
    public string Mode { get; set; } = "go";

    /// <summary>OpenCode Go 的 API key（也可用环境变量 OPENCODE_GO_API_KEY）。</summary>
    public string OpenCodeKey { get; set; } = "";

    /// <summary>DeepSeek 的 API key（也可用环境变量 DEEPSEEK_API_KEY）。</summary>
    public string DeepSeekKey { get; set; } = "";
}

/// <summary>自动检测更新。</summary>
public sealed class UpdateCfg
{
    /// <summary>是否在启动后和每隔几小时查一次 GitHub Release。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>检测间隔（小时），默认 6。</summary>
    public int CheckHours { get; set; } = 6;
}
public sealed class Config
{
    public int Width { get; set; } = 300;
    public int RefreshMs { get; set; } = 1000;

    /// <summary>ink = 深色半透明；solid = 纯色不透明。</summary>
    public string Theme { get; set; } = "ink";

    public bool Acrylic { get; set; } = false;
    public bool ShowClock { get; set; } = true;

    /// <summary>时钟显示秒（0 = 不显示）。</summary>
    public int ClockSeconds { get; set; } = 0;

    /// <summary>贴到桌面层（只在 Win+D 后可见）；关掉则变成普通置顶窗。</summary>
    public bool DesktopLayer { get; set; } = true;

    public bool NoActivate { get; set; } = false;

    /// <summary>要监控的盘符，例如 ["C:", "D:"]。留空则自动取前两块固定盘。</summary>
    public List<string> Drives { get; set; } = new();

    public LauncherCfg Launcher { get; set; } = new();

    public List<ServiceItem> Services { get; set; } = new();

    public UsageCfg Usage { get; set; } = new();

    public UpdateCfg Update { get; set; } = new();

    [JsonIgnore] public bool Migrated { get; private set; }

    [JsonIgnore]
    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPanel");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "config.json");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static Config Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(FilePath))
            {
                string text = File.ReadAllText(FilePath);
                var cfg = Parse(text);
                if (cfg != null)
                {
                    bool dirty = cfg.Normalize();
                    if (dirty) cfg.Save();
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置坏了先留档，别让面板因为一个坏 JSON 起不来
            try { File.Copy(FilePath, Path.Combine(Dir, "config.bad.json"), true); } catch { }
        }

        // 走到这里说明现有配置读不出来：先留档再写默认值，别静默覆盖
        try
        {
            if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(Dir, "config.bad.json"), true);
        }
        catch { }

        var def = Defaults();
        def.Save();
        return def;
    }

    /// <summary>解析配置；兼容早期版本（Services 曾是对象、密钥在顶层）。</summary>
    private static Config Parse(string text)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<Config>(text, JsonOptions);
            if (cfg != null) return cfg;
        }
        catch { }

        // 老版本兼容：把不兼容的键摘掉再解析一次
        var node = JsonNode.Parse(text) as JsonObject;
        if (node == null) return null;

        if (node["Services"] is JsonObject)
        {
            // 早期版本这里是对象，新版是数组 —— 直接丢弃这个键
            node.Remove("Services");
        }

        // 早期密钥放在顶层：迁移到 Usage
        var usage = node["Usage"] as JsonObject ?? new JsonObject();
        foreach (var (from, to) in new[] { ("OpenCodeKey", "OpenCodeKey"), ("DeepSeekKey", "DeepSeekKey"), ("UsageMode", "Mode") })
        {
            if (node[from] is JsonValue v)
            {
                usage[to] = v.ToJsonString().Trim('"');
                node.Remove(from);
            }
        }
        if (usage.Count > 0)
        {
            // 迁移时只有真的带上了 key 才打开额度区，避免升级后自动联网
            bool hasKey = usage["OpenCodeKey"]?.GetValue<string>() is string k1 && k1.Length > 0
                       || usage["DeepSeekKey"]?.GetValue<string>() is string k2 && k2.Length > 0;
            usage["Enabled"] = hasKey;
            node["Usage"] = usage;
        }

        try { return JsonSerializer.Deserialize<Config>(node.ToJsonString(), JsonOptions); }
        catch { return null; }
    }

    /// <returns>是否需要回写（发生了迁移或补默认值）</returns>
    private bool Normalize()
    {
        bool dirty = false;

        if (Width < 220 || Width > 600) { Width = 300; dirty = true; }
        if (RefreshMs < 500) { RefreshMs = 1000; dirty = true; }
        if (ClockSeconds < 0 || ClockSeconds > 59) { ClockSeconds = 0; dirty = true; }

        Launcher ??= new LauncherCfg();
        Launcher.Apps ??= new();
        Services ??= new();
        Usage ??= new UsageCfg();
        Update ??= new UpdateCfg();

        if (Launcher.Columns < 1 || Launcher.Columns > 8) { Launcher.Columns = 3; dirty = true; }
        if (Launcher.MaxRows < 1 || Launcher.MaxRows > 8) { Launcher.MaxRows = 3; dirty = true; }
        if (Update.CheckHours < 1 || Update.CheckHours > 168) { Update.CheckHours = 6; dirty = true; }

        Services.RemoveAll(s => s == null || string.IsNullOrWhiteSpace(s.Name));
        foreach (var s in Services) s.ProcessNames ??= new();

        Drives ??= new();
        if (Drives.Count == 0)
        {
            Drives = DetectDrives();
            dirty = true;
        }

        Migrated = dirty;
        return dirty;
    }

    /// <summary>列出所有可用的固定盘（给设置界面做多选）。</summary>
    public static List<string> DetectDrivesAll()
    {
        var result = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                result.Add(d.Name.TrimEnd('\\'));
            }
        }
        catch { }
        if (result.Count == 0) result.Add("C:");
        return result;
    }

    /// <summary>自动挑前两块固定盘（系统盘优先）。</summary>
    public static List<string> DetectDrives()
    {
        var result = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                result.Add(d.Name.TrimEnd('\\'));
                if (result.Count >= 2) break;
            }
        }
        catch { }
        if (result.Count == 0) result.Add("C:");
        return result;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        catch { }
    }

    private static Config Defaults()
    {
        var c = new Config();
        c.Drives = DetectDrives();
        c.Launcher = new LauncherCfg { Apps = new() };
        return c;
    }
}

/// <summary>从开始菜单 .lnk 解析目标路径（WScript.Shell 晚绑定，失败返回空）。</summary>
public static class ShortcutResolver
{
    private static readonly string[] Roots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
    };

    public static IEnumerable<string> AllLnkFiles()
    {
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)) yield return f;
        }
    }

    public static string Resolve(string lnkFileName)
    {
        try
        {
            foreach (var f in AllLnkFiles())
            {
                if (!string.Equals(Path.GetFileName(f), lnkFileName, StringComparison.OrdinalIgnoreCase)) continue;
                string target = ResolveLnk(f);
                if (!string.IsNullOrWhiteSpace(target) && File.Exists(target)) return target;
            }
        }
        catch { }
        return "";
    }

    public static string ResolveLnk(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return "";
            dynamic shell = Activator.CreateInstance(type);
            dynamic sc = shell.CreateShortcut(lnkPath);
            string target = sc.TargetPath as string ?? "";
            return target;
        }
        catch { return ""; }
    }
}
