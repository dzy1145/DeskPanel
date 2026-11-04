using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DeskPanel.Metrics;

/// <summary>
/// 百度千帆 Token Plan（积分制）余量。
/// 官方开放 API（bce-v3 key）只暴露 chat/completions，余量走的是控制台接口（要登录 Cookie），
/// 所以这里让用户在浏览器里「Copy as cURL」贴进来，我们照着这条请求定时拉一次。
/// 只在本机 config.json 里保存，不上传。
/// </summary>
public static class QianfanCredits
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // 控制台接口会看 UA，用普通浏览器 UA 更稳
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        return client;
    }

    public sealed class Snapshot
    {
        public bool Ok { get; init; }
        public double Percent { get; init; }
        public double Remaining { get; init; }
        public double Total { get; init; }
        public string ResetText { get; init; } = "";
        public string Error { get; init; } = "";
        public DateTime FetchedAt { get; init; } = DateTime.Now;
    }

    /// <summary>解析「Copy as cURL」原文，抽出 URL / 方法 / 头 / body。</summary>
    internal static bool TryParseCurl(string curl, out string method, out string url, out Dictionary<string, string> headers, out string body)
    {
        method = "GET";
        url = "";
        body = "";
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(curl)) return false;

        string text = curl.Trim();
        if (text.StartsWith("curl", StringComparison.OrdinalIgnoreCase)) text = text.Substring(4).Trim();

        // 取 URL：第一个不是以 - 开头的、引号包着的 http(s) 串
        var urlMatch = System.Text.RegularExpressions.Regex.Match(text, @"https?://[^\s'""]+");
        if (!urlMatch.Success) return false;
        url = urlMatch.Value.TrimEnd('\\');

        var methodMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:-X|--request)\s+['""]?(GET|POST|PUT|PATCH|DELETE)");
        if (methodMatch.Success) method = methodMatch.Groups[1].Value.ToUpperInvariant();

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text,
                     @"(?:-H|--header)\s+['""]([^:'""]+):\s*([^'""]*)['""]"))
        {
            string name = m.Groups[1].Value.Trim();
            string value = m.Groups[2].Value.Trim();
            if (name.StartsWith(":")) continue;
            headers[name] = value;
        }

        // -H 'cookie:...' 单行也能认出来
        var cookieMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:-b|--cookie)\s+['""]([^'""]+)['""]");
        if (cookieMatch.Success) headers["Cookie"] = cookieMatch.Groups[1].Value;

        var dataMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"(?:--data-raw|--data-binary|--data|-d)\s+['¥$]?['""]([\s\S]*?)['""]\s*(?:-H|--|$)");
        if (!dataMatch.Success)
        {
            dataMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:--data-raw|--data-binary|--data|-d)\s+[^\s]+");
        }
        if (dataMatch.Success && dataMatch.Groups.Count > 1)
        {
            body = dataMatch.Groups[1].Value.Replace("\\\"", "\"");
            if (method == "GET") method = "POST";
        }

        return url.Length > 0;
    }

    public static async Task<Snapshot> FetchAsync(string curl)
    {
        if (string.IsNullOrWhiteSpace(curl))
            return Fail("未配置抓包");

        if (!TryParseCurl(curl, out string method, out string url, out var headers, out string body))
            return Fail("抓包内容看不懂");

        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            if (method != "GET" && body.Length > 0)
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            foreach (var kv in headers)
            {
                // 这些交由 HttpClient 自己处理
                if (kv.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                    continue;
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Fail($"HTTP {(int)resp.StatusCode}");

            return Parse(text);
        }
        catch (TaskCanceledException)
        {
            return Fail("请求超时");
        }
        catch (Exception ex)
        {
            return Fail(ex.GetType().Name + " " + ex.Message);
        }
    }

    private static Snapshot Fail(string error) => new() { Ok = false, Error = error };

    /// <summary>从返回 JSON 里按字段名找余量；不同版本字段名不一样，用启发式。</summary>
    internal static Snapshot Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            double? remain = null, total = null, percent = null, used = null;
            string reset = "";

            Walk(doc.RootElement, (name, element) =>
            {
                string n = name.ToLowerInvariant();

                if (element.ValueKind == JsonValueKind.Number || element.ValueKind == JsonValueKind.String)
                {
                    double v;
                    bool isNum = element.ValueKind == JsonValueKind.Number
                        ? element.TryGetDouble(out v)
                        : double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

                    if (isNum)
                    {
                        // 千帆真实字段：totalCredits / usedCredits（字符串数字），余量要自己减
                        if (total == null && n.Replace("_", "").Contains("totalcredit")) total = v;
                        else if (used == null && n.Replace("_", "").Contains("usedcredit")) used = v;
                        else if (percent == null && (n.Contains("percent") || n.Contains("ratio") || n.Contains("rate")))
                            percent = v;
                        else if (remain == null && (n.Contains("remain") || n.Contains("left") || n.Contains("surplus") || n.Contains("available")))
                            remain = v;
                        else if (total == null && (n.Contains("total") || n.Contains("quota") || n.Contains("amount") || n.Contains("cap") || n.Contains("limit")) && !n.Contains("count") && !n.Contains("token"))
                            total = v;
                    }
                    else if (reset.Length == 0 && (n.Contains("reset") || n.Contains("expire") || n.Contains("next") || n.Contains("due") || n.Contains("renew")))
                    {
                        reset = element.GetString() ?? "";
                    }
                }
                else if (element.ValueKind == JsonValueKind.String && reset.Length == 0 &&
                         (n.Contains("reset") || n.Contains("expire") || n.Contains("next") || n.Contains("due") || n.Contains("renew")))
                {
                    reset = element.GetString() ?? "";
                }
            });

            // 有 total + used 就先算剩余
            if (!remain.HasValue && total.HasValue && used.HasValue)
                remain = Math.Max(0, total.Value - used.Value);

            // 百分比可能是 0–1 的小数
            if (percent.HasValue && percent.Value > 0 && percent.Value <= 1) percent *= 100;
            if (!percent.HasValue && remain.HasValue && total.HasValue && total.Value > 0)
                percent = remain.Value / total.Value * 100;

            if (!remain.HasValue && !total.HasValue && !percent.HasValue)
                return Fail("返回里找不到余量字段");

            // 把 ISO 时间（2026-12-04T13:26:59+08:00）格式化成 2026-12-04 13:26
            if (reset.Length > 10 && DateTime.TryParse(reset, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out DateTime resetAt))
                reset = resetAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            double pct = percent ?? 0;
            if (remain.HasValue && total.HasValue && total.Value > 0 && !percent.HasValue)
                pct = remain.Value / total.Value * 100;

            return new Snapshot
            {
                Ok = true,
                Percent = Math.Clamp(pct, 0, 100),
                Remaining = remain ?? 0,
                Total = total ?? 0,
                ResetText = reset,
            };
        }
        catch (Exception ex)
        {
            return Fail("解析失败：" + ex.GetType().Name);
        }
    }

    private static void Walk(JsonElement element, Action<string, JsonElement> visit, string name = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    Walk(prop.Value, visit, prop.Name);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, visit, name);
                break;
            default:
                if (name.Length > 0) visit(name, element);
                break;
        }
    }

    // ---- 缓存：断网/失败时先显示上次成功的结果 ----

    private static string CachePath => Path.Combine(Config.Dir, "qianfan-credits.json");

    public static void SaveCache(Snapshot snap)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            var payload = new
            {
                percent = snap.Percent,
                remaining = snap.Remaining,
                total = snap.Total,
                resetText = snap.ResetText,
                fetchedAt = snap.FetchedAt,
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(payload));
        }
        catch { }
    }

    public static Snapshot LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            JsonElement root = doc.RootElement;
            return new Snapshot
            {
                Ok = true,
                Percent = root.TryGetProperty("percent", out var p) ? p.GetDouble() : 0,
                Remaining = root.TryGetProperty("remaining", out var r) ? r.GetDouble() : 0,
                Total = root.TryGetProperty("total", out var tt) ? tt.GetDouble() : 0,
                ResetText = root.TryGetProperty("resetText", out var rs) ? rs.GetString() ?? "" : "",
                FetchedAt = root.TryGetProperty("fetchedAt", out var fa) && fa.TryGetDateTime(out var dt) ? dt : DateTime.Now,
            };
        }
        catch
        {
            return null;
        }
    }
}