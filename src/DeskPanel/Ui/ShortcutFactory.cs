using System.IO;

namespace DeskPanel.Ui;

/// <summary>
/// 为拖进来的文件/文件夹生成 .lnk（放在 DeskPanel 自己的 links 目录里），
/// 面板里存这个快捷方式的路径——和把东西拖到桌面生成快捷方式是一回事。
/// </summary>
internal static class ShortcutFactory
{
    public static string LinksDir
    {
        get
        {
            string dir = Path.Combine(Config.Dir, "links");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static readonly char[] Invalid =
        Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).ToArray();

    private static string Safe(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name) sb.Append(Invalid.Contains(c) ? '_' : c);
        string s = sb.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(s) ? "item" : s;
    }

    /// <summary>给 target 生成一个 .lnk，返回 .lnk 路径；失败返回 null。</summary>
    public static string Create(string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)) return null;
            target = target.Trim().TrimEnd('\\');
            if (!File.Exists(target) && !Directory.Exists(target)) return null;

            string baseName = Path.GetFileName(target);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = target;

            string file = Path.Combine(LinksDir, Safe(baseName) + ".lnk");
            int n = 2;
            while (File.Exists(file) &&
                   !string.Equals(ShortcutResolver.ResolveLnk(file), target, StringComparison.OrdinalIgnoreCase))
            {
                file = Path.Combine(LinksDir, $"{Safe(baseName)} ({n++}).lnk");
            }

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;

            dynamic shell = Activator.CreateInstance(type);
            dynamic sc = shell.CreateShortcut(file);
            sc.TargetPath = target;
            sc.WorkingDirectory = Directory.Exists(target) ? target : (Path.GetDirectoryName(target) ?? "");
            sc.Description = "DeskPanel 快捷方式";
            sc.Save();

            return File.Exists(file) ? file : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把拖进来的路径变成一格：.lnk/.url 直接用，其余生成 .lnk。</summary>
    public static AppItem ToItem(string path, out bool createdLink)
    {
        createdLink = false;
        if (string.IsNullOrWhiteSpace(path)) return null;

        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".lnk" or ".url")
        {
            return new AppItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Target = path,
                Icon = path,
            };
        }

        string lnk = Create(path);
        if (lnk != null) createdLink = true;

        return new AppItem
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Target = lnk ?? path,
            Icon = path,
        };
    }

    /// <summary>在指定位置生成 .lnk（启动文件夹用）。成功返回路径，失败 null。</summary>
    public static string CreateAt(string target, string lnkPath, string args = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(lnkPath)) return null;
            target = target.Trim().TrimEnd('\\');

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;

            dynamic shell = Activator.CreateInstance(type);
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            sc.Arguments = args ?? "";
            sc.WorkingDirectory = Directory.Exists(target) ? target : (Path.GetDirectoryName(target) ?? "");
            sc.Description = "DeskPanel 开机启动";
            sc.Save();

            return File.Exists(lnkPath) ? lnkPath : null;
        }
        catch
        {
            return null;
        }
    }}
