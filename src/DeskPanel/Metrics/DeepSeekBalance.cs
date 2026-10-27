using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DeskPanel.Metrics;

/// <summary>
/// DeepSeek 官方 API 余额：GET https://api.deepseek.com/user/balance
/// 返回 balance_infos[]（通常一条 CNY）：total_balance / granted_balance / topped_up_balance。
/// key 优先取 config 的 Usage.DeepSeekKey，其次是环境变量 DEEPSEEK_API_KEY。
/// </summary>
public static class DeepSeekBalance
{
    private const string Url = "https://api.deepseek.com/user/balance";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public sealed class Snapshot
    {
        public bool Available { get; init; }
        public string Currency { get; init; } = "CNY";
        public double Total { get; init; }
        public double Granted { get; init; }
        public double ToppedUp { get; init; }
        public DateTime FetchedAt { get; init; }
        public string Error { get; init; }
    }

    public static async Task<Snapshot> FetchAsync(string keyOverride)
    {
        string key = ResolveKey(keyOverride);
        if (string.IsNullOrWhiteSpace(key)) return Failed("没找到 DEEPSEEK_API_KEY");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return Failed($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("balance_infos", out var infos) ||
                infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0)
                return Failed("返回里没有 balance_infos");

            // 有多币种时优先 CNY
            JsonElement pick = infos[0];
            foreach (var info in infos.EnumerateArray())
            {
                if (info.TryGetProperty("currency", out var cur) &&
                    string.Equals(cur.GetString(), "CNY", StringComparison.OrdinalIgnoreCase))
                {
                    pick = info;
                    break;
                }
            }

            return new Snapshot
            {
                Available = !doc.RootElement.TryGetProperty("is_available", out var av) || av.ValueKind != JsonValueKind.False,
                Currency = pick.TryGetProperty("currency", out var c2) ? c2.GetString() ?? "CNY" : "CNY",
                Total = Num(pick, "total_balance"),
                Granted = Num(pick, "granted_balance"),
                ToppedUp = Num(pick, "topped_up_balance"),
                FetchedAt = DateTime.Now,
            };
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name + " " + ex.Message);
        }
    }

    private static double Num(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out double d)) return d;
        return 0;
    }

    private static Snapshot Failed(string error) => new() { Error = error, FetchedAt = DateTime.Now };

    public static string ResolveKey(string keyOverride)
    {
        if (!string.IsNullOrWhiteSpace(keyOverride)) return keyOverride.Trim();

        // 只认 config.json 的 Usage.DeepSeekKey 和这个环境变量
        string env = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    // ---------------------------------------------------------------- 缓存（取数失败时沿用上次）

    private sealed class CacheDto
    {
        public double Total { get; set; }
        public double Granted { get; set; }
        public double ToppedUp { get; set; }
        public string Currency { get; set; } = "CNY";
        public string SavedAt { get; set; } = "";
    }

    private static string CachePath => Path.Combine(Config.Dir, "deepseek-balance.json");

    public static void SaveCache(Snapshot snap)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(new CacheDto
            {
                Total = snap.Total,
                Granted = snap.Granted,
                ToppedUp = snap.ToppedUp,
                Currency = snap.Currency,
                SavedAt = snap.FetchedAt.ToString("O"),
            }));
        }
        catch (Exception ex) { Log.Write("balance: 写缓存失败 :: " + ex.Message); }
    }

    public static Snapshot LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(CachePath));
            if (dto == null) return null;

            DateTime at = DateTime.TryParse(dto.SavedAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : default;

            return new Snapshot
            {
                Available = true,
                Total = dto.Total,
                Granted = dto.Granted,
                ToppedUp = dto.ToppedUp,
                Currency = dto.Currency,
                FetchedAt = at,
            };
        }
        catch { return null; }
    }
}
