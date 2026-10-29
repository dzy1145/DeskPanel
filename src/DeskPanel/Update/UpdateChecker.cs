using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DeskPanel.Update;

/// <summary>一次检测的结果。</summary>
public sealed class UpdateInfo
{
    public bool Ok { get; init; }
    public bool HasUpdate { get; init; }
    public string Current { get; init; } = "";
    public string Latest { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Error { get; init; } = "";
    public DateTime CheckedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// 查 GitHub 的最新 Release，和当前程序版本比一比。
/// 只做「检测 + 打开下载页」，不自动替换程序（运行中被替换会触发 SmartScreen 和文件占用）。
/// </summary>
public static class UpdateChecker
{
    private const string Owner = "dzy1145";
    private const string Repo = "DeskPanel";
    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // GitHub API 强制要求 User-Agent，否则直接 403
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DeskPanel-Updater");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return client;
    }

    /// <summary>当前程序版本，例如 "1.3.1"。</summary>
    public static string CurrentVersion
    {
        get
        {
            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        try
        {
            using HttpResponseMessage resp = await Http.GetAsync(ApiUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Failed($"HTTP {(int)resp.StatusCode}");

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out JsonElement tagEl) ? tagEl.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out JsonElement urlEl) ? urlEl.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body", out JsonElement bodyEl) ? bodyEl.GetString() ?? "" : "";

            string download = "";
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        download = asset.TryGetProperty("browser_download_url", out JsonElement d)
                            ? d.GetString() ?? ""
                            : "";
                        break;
                    }
                }
            }

            string current = CurrentVersion;
            string latest = Normalize(tag);
            if (latest.Length == 0)
                return Failed("Release 里没有版本号");

            bool newer = Compare(latest, current) > 0;

            return new UpdateInfo
            {
                Ok = true,
                HasUpdate = newer,
                Current = current,
                Latest = latest,
                ReleaseUrl = htmlUrl.Length > 0 ? htmlUrl : $"https://github.com/{Owner}/{Repo}/releases",
                DownloadUrl = download,
                Notes = Summarize(notes),
            };
        }
        catch (TaskCanceledException)
        {
            return Failed("检查超时");
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name + " " + ex.Message);
        }
    }

    private static UpdateInfo Failed(string error) => new()
    {
        Ok = false,
        Current = CurrentVersion,
        Error = error,
    };

    /// <summary>"v1.3.1" → "1.3.1"。</summary>
    private static string Normalize(string tag)
    {
        string t = (tag ?? "").Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
        return t.Trim();
    }

    /// <summary>逐段比较版本号，返回 &gt;0 表示 a 比 b 新。</summary>
    private static int Compare(string a, string b)
    {
        if (!Version.TryParse(Pad(a), out Version? va)) return 0;
        if (!Version.TryParse(Pad(b), out Version? vb)) return 0;
        return va.CompareTo(vb);
    }

    private static string Pad(string v)
    {
        string[] parts = (v ?? "").Split('.');
        if (parts.Length >= 3) return v;
        return (v ?? "") + new string('.', 3 - parts.Length) + "0";
    }

    /// <summary>Release 说明太长，面板提示里只留第一行要点。</summary>
    private static string Summarize(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        string[] lines = notes.Replace("\r\n", "\n").Split('\n');
        foreach (string line in lines)
        {
            string t = line.Trim().TrimStart('-', '*', ' ');
            if (t.Length == 0) continue;
            if (t.StartsWith("#", StringComparison.Ordinal)) continue;
            return t.Length > 60 ? t.Substring(0, 58) + "…" : t;
        }
        return "";
    }
}
