using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DeskPanel.Metrics;

/// <summary>一个额度窗口（5h / 周 / 月）。</summary>
public sealed class UsageWindow
{
    public string Label { get; init; } = "";
    public int Percent { get; init; }
    public DateTime ResetsAt { get; init; }
    public bool Ok { get; init; }
}

/// <summary>
/// OpenCode Go 订阅额度：GET https://opencode.ai/zen/go/v1/usage
/// 返回 usage.rolling / weekly / monthly 各含 percent（整数）与 resetsAt。
/// key 优先取 config.json 的 Usage.OpenCodeKey，其次是环境变量 OPENCODE_GO_API_KEY。
/// </summary>
public static class OpenCodeUsage
{
    private const string Url = "https://opencode.ai/zen/go/v1/usage";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public sealed class Snapshot
    {
        public UsageWindow Rolling { get; init; }
        public UsageWindow Weekly { get; init; }
        public UsageWindow Monthly { get; init; }
        public string Error { get; init; }
        public DateTime FetchedAt { get; init; }
    }

    /// <summary>异步取一次；失败也返回 Snapshot（Error 非空）。</summary>
    public static async Task<Snapshot> FetchAsync(string keyOverride)
    {
        string key = ResolveKey(keyOverride);
        if (string.IsNullOrWhiteSpace(key))
            return Failed("没找到 OPENCODE_GO_API_KEY");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return Failed($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("usage", out var usage))
                return Failed("返回里没有 usage");

            return new Snapshot
            {
                Rolling = Read(usage, "rolling", "5h"),
                Weekly = Read(usage, "weekly", "周"),
                Monthly = Read(usage, "monthly", "月"),
                FetchedAt = DateTime.Now,
            };
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name + " " + ex.Message);
        }
    }

    private static UsageWindow Read(JsonElement usage, string name, string label)
    {
        if (!usage.TryGetProperty(name, out var w)) return new UsageWindow { Label = label };

        int pct = 0;
        if (w.TryGetProperty("percent", out var p))
        {
            if (p.ValueKind == JsonValueKind.Number) pct = (int)Math.Round(p.GetDouble());
            else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out int parsed)) pct = parsed;
        }

        DateTime reset = default;
        if (w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.String)
            DateTime.TryParse(r.GetString(), null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out reset);

        // status 不是 ok 时也算拿到了数据，只是不可信
        bool ok = w.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
            ? string.Equals(st.GetString(), "ok", StringComparison.OrdinalIgnoreCase)
            : true;

        return new UsageWindow { Label = label, Percent = Math.Clamp(pct, 0, 100), ResetsAt = reset, Ok = ok };
    }

    private static Snapshot Failed(string error) => new()
    {
        Error = error,
        Rolling = new UsageWindow { Label = "5h" },
        Weekly = new UsageWindow { Label = "周" },
        Monthly = new UsageWindow { Label = "月" },
        FetchedAt = DateTime.Now,
    };

    // ---------------------------------------------------------------- 本地缓存
    // 取数失败时沿用上一次成功的结果，别让用户看到 "--"。

    private sealed class CacheDto
    {
        public int Rolling { get; set; }
        public string RollingReset { get; set; } = "";
        public int Weekly { get; set; }
        public string WeeklyReset { get; set; } = "";
        public int Monthly { get; set; }
        public string MonthlyReset { get; set; } = "";
        public string SavedAt { get; set; } = "";
    }

    private static string CachePath => Path.Combine(Config.Dir, "usage-cache.json");

    public static void SaveCache(Snapshot snap)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            var dto = new CacheDto
            {
                Rolling = snap.Rolling.Percent,
                RollingReset = snap.Rolling.ResetsAt.ToString("O"),
                Weekly = snap.Weekly.Percent,
                WeeklyReset = snap.Weekly.ResetsAt.ToString("O"),
                Monthly = snap.Monthly.Percent,
                MonthlyReset = snap.Monthly.ResetsAt.ToString("O"),
                SavedAt = snap.FetchedAt.ToString("O"),
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(dto));
        }
        catch (Exception ex)
        {
            Log.Write("usage: 写缓存失败 :: " + ex.Message);
        }
    }

    /// <summary>读上一次成功的缓存；没有则返回 null。</summary>
    public static Snapshot LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;

            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(CachePath));
            if (dto == null) return null;

            static DateTime Parse(string s) =>
                DateTime.TryParse(s, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : default;

            return new Snapshot
            {
                Rolling = new UsageWindow { Label = "5h", Percent = dto.Rolling, ResetsAt = Parse(dto.RollingReset) },
                Weekly = new UsageWindow { Label = "周", Percent = dto.Weekly, ResetsAt = Parse(dto.WeeklyReset) },
                Monthly = new UsageWindow { Label = "月", Percent = dto.Monthly, ResetsAt = Parse(dto.MonthlyReset) },
                FetchedAt = Parse(dto.SavedAt),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>key 来源：config 覆盖 → 环境变量 → DSH 的 .credentials.yaml。</summary>
    public static string ResolveKey(string keyOverride)
    {
        if (!string.IsNullOrWhiteSpace(keyOverride)) return keyOverride.Trim();

        // 只认 config.json 的 Usage.OpenCodeKey 和这个环境变量，不读任何其它软件的凭据文件
        string env = Environment.GetEnvironmentVariable("OPENCODE_GO_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }
}
