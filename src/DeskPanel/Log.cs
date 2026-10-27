using System.IO;

namespace DeskPanel;

internal static class Log
{
    /// <summary>主日志写不进去时的原因（用于诊断）。</summary>
    public static string LastError { get; private set; }

    public static void Write(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(System.IO.Path.Combine(Config.Dir, "app.log"), line);
            return;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + " " + ex.Message;
        }

        // 兜底：写临时目录，否则“写不进去”这件事本身会完全看不见
        try
        {
            File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeskPanel.log"),
                line + $"    [主日志写入失败: {LastError}]{Environment.NewLine}");
        }
        catch { }
    }

    public static void Ex(Exception ex) => Write("EX " + ex);
}
