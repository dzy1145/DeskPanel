using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DeskPanel.Metrics;

/// <summary>
/// 点一下按钮 → 弹出 Edge（独立 profile，登录一次会被记住）→ 用 CDP 监听网络 →
/// 自动挑出「套餐余量」那条 JSON → 还原成 cURL 交给面板。用户不需要复制粘贴任何东西。
/// </summary>
public static class QianfanCapture
{
    private const int DebugPort = 9333;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly string[] Keywords =
    {
        "credit", "积分", "remain", "quota", "balance", "tokenplan", "token_plan", "point", "surplus", "usage",
    };

    private static readonly string[] StrongKeywords = { "credit", "积分", "quota", "tokenplan", "token_plan" };

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Curl { get; init; } = "";
        public string Url { get; init; } = "";
        public string Error { get; init; } = "";
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

    /// <summary>
    /// 打开登录页并等抓到余量请求。<paramref name="progress"/> 会在 UI 线程外的线程上被调用，需自行切回。
    /// </summary>
    public static async Task<Result> CaptureAsync(string startUrl, int timeoutSeconds, Action<string> progress,
        CancellationToken token)
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
                            $"{startUrl}",
            });

            string? wsUrl;
            try
            {
                wsUrl = await WaitForTargetAsync(startUrl, token);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return new Result { Ok = false, Error = "已取消" };
            }

            if (wsUrl == null)
            {
                Kill(process);
                return new Result { Ok = false, Error = "浏览器调试端口没起来" };
            }

            progress("请在浏览器里登录，并打开「Token Plan / 我的订阅」页面…");
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("User-Agent", "DeskPanel-Capture");
            await socket.ConnectAsync(new Uri(wsUrl), token);

            using var session = new CdpSession(socket);
            await session.SendAsync("Network.enable", new { maxTotalBufferSize = 200_000_000 }, token);
            await session.SendAsync("Page.enable", null, token);
            await session.SendAsync("Network.setCacheDisabled", new { cacheDisabled = true }, token);
            // 明确导航到目标页，避免挂到 newtab / 别的标签
            try { await session.SendAsync("Page.navigate", new { url = startUrl }, token); } catch { }
            await Task.Delay(1500, token);

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            int messageCount = 0;
            int jsonSeen = 0;
            var requests = new Dictionary<string, (string url, string method, Dictionary<string, string> headers, string? body)>();
            var tried = new HashSet<string>();
            var pendingJson = new Dictionary<string, string>();

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                JsonDocument? message = await session.ReceiveAsync(token);
                if (message == null) break;

                using (message)
                {
                    JsonElement root = message.RootElement;
                    if (!root.TryGetProperty("method", out JsonElement methodEl)) continue;
                    string method = methodEl.GetString() ?? "";
                    messageCount++;
                    if (process != null && process.HasExited)
                    {
                        Log.Write("capture: Edge 进程已退出");
                        return new Result { Ok = false, Error = "浏览器被关掉了（别关那个窗口）" };
                    }

                    if (method == "Network.requestWillBeSent")
                    {
                        JsonElement p = root.GetProperty("params");
                        string id = p.GetProperty("requestId").GetString() ?? "";
                        JsonElement req = p.GetProperty("request");
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (req.TryGetProperty("headers", out JsonElement h) && h.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty prop in h.EnumerateObject())
                                headers[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        requests[id] = (
                            req.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? "" : "",
                            req.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? "GET" : "GET",
                            headers,
                            req.TryGetProperty("postData", out JsonElement pd) ? pd.GetString() : null);
                    }
                    else if (method == "Network.responseReceived")
                    {
                        JsonElement p = root.GetProperty("params");
                        string id = p.GetProperty("requestId").GetString() ?? "";
                        JsonElement resp = p.GetProperty("response");
                        string mime = resp.TryGetProperty("mimeType", out JsonElement mi) ? mi.GetString() ?? "" : "";
                        if (mime.Contains("json"))
                        {
                            jsonSeen++;
                            pendingJson[id] = resp.TryGetProperty("url", out JsonElement pu) ? pu.GetString() ?? "" : "";
                        }
                    }
                    else if (method == "Network.loadingFinished")
                    {
                        JsonElement p = root.GetProperty("params");
                        string id = p.GetProperty("requestId").GetString() ?? "";
                        if (!pendingJson.ContainsKey(id) || !tried.Add(id)) continue;

                        JsonDocument? bodyDoc = null;
                        for (int attempt = 0; attempt < 3 && bodyDoc == null; attempt++)
                        {
                            try
                            {
                                bodyDoc = await session.CallAsync("Network.getResponseBody", new { requestId = id }, token);
                            }
                            catch { await Task.Delay(300, token); }
                            if (bodyDoc == null) await Task.Delay(300, token);
                        }
                        if (bodyDoc == null) continue;

                        string body;
                        using (bodyDoc)
                        {
                            body = bodyDoc.RootElement.TryGetProperty("body", out JsonElement b)
                                ? b.GetString() ?? ""
                                : "";
                        }
                        if (body.Length == 0 || body.Length > 2_000_000) continue;

                        int score = Score(body);
                        if (score < 8) continue;

                        string url = pendingJson.TryGetValue(id, out string? ju) ? ju : "";
                        var (method2, headers2, postData) =
                            requests.TryGetValue(id, out var rq) ? (rq.method, rq.headers, rq.body) : ("GET", new Dictionary<string, string>(), null);

                        progress($"抓到了：{url}");
                        Kill(process);
                        return new Result
                        {
                            Ok = true,
                            Url = url,
                            Curl = BuildCurl(method2, string.IsNullOrEmpty(url) ? rq.url : url, headers2, postData),
                        };
                    }
                }
            }

            Kill(process);
            Log.Write($"capture: 超时（消息 {messageCount} 条，JSON 响应 {jsonSeen} 条）");
            return new Result { Ok = false, Error = $"超时（收到 {messageCount} 条 CDP 消息、{jsonSeen} 条 JSON 响应）" };
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

    private static void Kill(Process? process)
    {
        try { process?.Kill(entireProcessTree: true); } catch { }
    }

    private static async Task<string?> WaitForTargetAsync(string startUrl, CancellationToken token)
    {
        for (int i = 0; i < 80 && !token.IsCancellationRequested; i++)
        {
            try
            {
                string json = await Http.GetStringAsync($"http://127.0.0.1:{DebugPort}/json/list", token);
                using JsonDocument doc = JsonDocument.Parse(json);
                string? fallback = null;
                foreach (JsonElement target in doc.RootElement.EnumerateArray())
                {
                    if (!target.TryGetProperty("type", out JsonElement typeEl) || typeEl.GetString() != "page") continue;
                    if (!target.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsEl)) continue;
                    string ws = wsEl.GetString() ?? "";
                    if (ws.Length == 0) continue;
                    string url = target.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? "" : "";
                    if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || url.Contains("newtab")) continue;
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return ws;
                    fallback ??= ws;
                }
                if (fallback != null) return fallback;
            }
            catch { }
            await Task.Delay(500, token);
        }
        return null;
    }

    private static int Score(string body)
    {
        string low = body.ToLowerInvariant();
        int score = 0;
        foreach (string k in Keywords) if (low.Contains(k.ToLowerInvariant())) score += 3;
        foreach (string k in StrongKeywords) if (low.Contains(k.ToLowerInvariant())) score += 5;

        var numbers = System.Text.RegularExpressions.Regex.Matches(body, @"-?\d+(?:\.\d+)?");
        if (numbers.Count >= 2) score += 2;
        if (System.Text.RegularExpressions.Regex.IsMatch(body, @"\b(45\d{3}|4[3-6]\d{3})\b")) score += 4;
        return score;
    }

    private static string BuildCurl(string method, string url, Dictionary<string, string> headers, string? postData)
    {
        var sb = new StringBuilder();
        sb.Append("curl '").Append(url).Append('\'');
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && method.Length > 0)
            sb.Append(" -X ").Append(method);

        foreach (var kv in headers)
        {
            string name = kv.Key;
            if (name.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            if (kv.Value.Length == 0) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9-]+$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            sb.Append(" -H '").Append(name).Append(": ").Append(kv.Value.Replace("'", "")).Append('\'');
        }

        if (!string.IsNullOrEmpty(postData))
            sb.Append(" --data-raw '").Append(postData.Replace("'", "")).Append('\'');

        return sb.ToString();
    }

    /// <summary>最小 CDP 客户端：发命令 + 收消息。</summary>
    private sealed class CdpSession : IDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly byte[] _buffer = new byte[1 << 20];
        private int _nextId = 1;

        public CdpSession(ClientWebSocket socket) => _socket = socket;

        public async Task SendAsync(string method, object? parameters, CancellationToken token)
        {
            var payload = new Dictionary<string, object?> { ["id"] = _nextId++, ["method"] = method };
            if (parameters != null) payload["params"] = parameters;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }

        public async Task<JsonDocument?> CallAsync(string method, object? parameters, CancellationToken token)
        {
            await SendAsync(method, parameters, token);
            int wantedId = _nextId - 1;
            while (!token.IsCancellationRequested)
            {
                JsonDocument? doc = await ReceiveAsync(token);
                if (doc == null) return null;
                if (doc.RootElement.TryGetProperty("id", out JsonElement idEl) && idEl.GetInt32() == wantedId)
                    return doc;
                doc.Dispose();
            }
            return null;
        }

        public async Task<JsonDocument?> ReceiveAsync(CancellationToken token)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(_buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (ms.Length == 0) return null;
            return JsonDocument.Parse(ms.ToArray());
        }

        public void Dispose()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .GetAwaiter().GetResult();
            }
            catch { }
            _socket.Dispose();
        }
    }
}