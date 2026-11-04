using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DeskPanel.Metrics;

/// <summary>
/// 点一下按钮 → 弹出 Edge（独立 profile，登录一次会被记住）→ 从浏览器里取 console.bce.baidu.com 的 Cookie
/// → 拼成一条对余量接口的请求保存下来。不抓响应体、不解析页面，稳定得多。
///
/// 余量接口（2026-11 实测）：
///   GET https://console.bce.baidu.com/api/qianfan/charge/tokenPlanPersonal/resource
///   {"result":{"items":[{"totalCredits":"45000.00","usedCredits":"515.81","expiresAt":"..."}]}}
/// </summary>
public static class QianfanCapture
{
    /// <summary>余量接口，用登录 Cookie 鉴权。</summary>
    public const string ResourceUrl = "https://console.bce.baidu.com/api/qianfan/charge/tokenPlanPersonal/resource";

    /// <summary>登录后浏览器会打开这个页面（Token Plan 个人版）。</summary>
    public const string StartUrl = "https://console.bce.baidu.com/qianfan/resource/token-plan";

    private const int DebugPort = 9333;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Curl { get; init; } = "";
        public string Url { get; init; } = "";
        public string Error { get; init; } = "";
        public string Summary { get; init; } = "";
    }

    private static string ProfileDir => Path.Combine(Config.Dir, "capture-profile");

    private static string? FindEdge()
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<Result> CaptureAsync(int timeoutSeconds, Action<string> progress, CancellationToken token)
    {
        string? edge = FindEdge();
        if (edge == null) return new Result { Ok = false, Error = "找不到 msedge.exe" };

        Directory.CreateDirectory(ProfileDir);

        try
        {
            progress("正在打开浏览器…");
            var process = Process.Start(new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                Arguments = $"--remote-debugging-port={DebugPort} " +
                            $"--user-data-dir=\"{ProfileDir}\" " +
                            "--no-first-run --no-default-browser-check " +
                            $"{StartUrl}",
            });

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            progress("请在浏览器里登录（已登录会自动继续）…");

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                string? pageWs = await FindConsolePageAsync(token);
                if (pageWs == null)
                {
                    await Task.Delay(2000, token);
                    continue;
                }

                progress("已找到控制台页面，正在读取登录状态…");
                string? cookie = await TryReadCookiesAsync(pageWs, token);
                if (string.IsNullOrWhiteSpace(cookie))
                {
                    await Task.Delay(2000, token);
                    continue;
                }

                string curl = BuildCurl(ResourceUrl, cookie);
                progress("正在验证余量接口…");
                Result? verified = await VerifyAsync(curl, token);
                if (verified != null)
                {
                    Kill(process);
                    Log.Write($"capture: 成功 {verified.Summary}");
                    return new Result
                    {
                        Ok = true,
                        Url = ResourceUrl,
                        Curl = curl,
                        Summary = verified.Summary,
                    };
                }

                progress("Cookie 还不可用（可能还没登录完），继续等…");
                await Task.Delay(3000, token);
            }

            Kill(process);
            return new Result { Ok = false, Error = "超时：请确认浏览器里已登录，并停在这个 Token Plan 页面" };
        }
        catch (OperationCanceledException)
        {
            return new Result { Ok = false, Error = "已取消" };
        }
        catch (Exception ex)
        {
            Log.Ex(ex);
            return new Result { Ok = false, Error = ex.GetType().Name + " " + ex.Message };
        }
    }

    /// <summary>用这条 Cookie 请求一次余量接口，确认能用并给出摘要。</summary>
    private static async Task<Result?> VerifyAsync(string curl, CancellationToken token)
    {
        QianfanCredits.Snapshot snap = await QianfanCredits.FetchAsync(curl);
        if (!snap.Ok) return null;

        return new Result
        {
            Ok = true,
            Summary = $"{snap.Percent:0.##}%（剩余 {snap.Remaining:#,0.##} / 总量 {snap.Total:#,0.##} 积分）",
        };
    }

    private static string BuildCurl(string url, string cookie)
    {
        return $"curl '{url}' " +
               $"-H 'Accept: application/json' " +
               $"-H 'Cookie: {cookie.Replace("'", "")}'";
    }

    private static void Kill(Process? process)
    {
        try { process?.Kill(entireProcessTree: true); } catch { }
    }

    private static async Task<string?> FindConsolePageAsync(CancellationToken token)
    {
        try
        {
            string json = await Http.GetStringAsync($"http://127.0.0.1:{DebugPort}/json/list", token);
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement target in doc.RootElement.EnumerateArray())
            {
                if (!target.TryGetProperty("type", out JsonElement typeEl) || typeEl.GetString() != "page") continue;
                if (!target.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsEl)) continue;
                string url = target.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? "" : "";
                if (url.Contains("login.bce.baidu.com", StringComparison.OrdinalIgnoreCase)) continue;
                if (url.Contains("console.bce.baidu.com", StringComparison.OrdinalIgnoreCase))
                    return wsEl.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadCookiesAsync(string webSocketUrl, CancellationToken token)
    {
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(webSocketUrl), token);

            string payload = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "Network.getCookies",
                @params = new { urls = new[] { "https://console.bce.baidu.com" } },
            });
            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, token);

            var buffer = new byte[1 << 18];
            using var ms = new MemoryStream();
            while (!token.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                using JsonDocument doc = JsonDocument.Parse(ms.ToArray());
                ms.SetLength(0);

                if (!doc.RootElement.TryGetProperty("id", out JsonElement idEl) || idEl.GetInt32() != 1) continue;
                if (!doc.RootElement.TryGetProperty("result", out JsonElement resultEl)) return null;
                if (!resultEl.TryGetProperty("cookies", out JsonElement cookies) ||
                    cookies.ValueKind != JsonValueKind.Array) return null;

                var pairs = new List<string>();
                foreach (JsonElement cookie in cookies.EnumerateArray())
                {
                    string name = cookie.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                    string value = cookie.TryGetProperty("value", out JsonElement v) ? v.GetString() ?? "" : "";
                    if (name.Length > 0) pairs.Add(name + "=" + value);
                }
                return pairs.Count == 0 ? null : string.Join("; ", pairs);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}