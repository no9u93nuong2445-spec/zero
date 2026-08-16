using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AI.VideoHub.V4.CaptureLab;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "AI Video Hub V4 原版成功轨迹采集器";
        Banner();
        var originalExe = ResolveOriginalExe(args);
        if (originalExe is null) return 2;
        var port = FreePort();
        var sessionRoot = Path.Combine(AppContext.BaseDirectory, "captures", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(sessionRoot);
        var journal = new CaptureJournal(sessionRoot);
        var manifest = new CaptureManifest
        {
            CollectorVersion = "4.0.0-capture1",
            StartedAtUtc = DateTime.UtcNow,
            OriginalExeName = Path.GetFileName(originalExe),
            OriginalExeSha256 = HashFile(originalExe),
            RemoteDebugPort = port,
            PrivacyMode = "credentials-redacted"
        };
        await journal.WriteManifestAsync(manifest);
        var manager = new TargetManager(port, journal);
        try
        {
            Console.WriteLine($"\n[1/4] 原版：{originalExe}");
            Console.WriteLine("[2/4] 正在以只读调试模式启动原版（仅监听 localhost）...");
            var original = LaunchOriginal(originalExe, port);
            manifest.OriginalProcessId = original.Id;
            await journal.WriteManifestAsync(manifest);
            if (!await manager.WaitForDebuggerAsync(TimeSpan.FromSeconds(45)))
            {
                Console.WriteLine("\n没有发现 WebView2 调试目标。请先关闭所有原版窗口后，再重新运行本采集器。\n");
                await journal.WriteErrorAsync("webview2-debug-target-not-found");
                return 3;
            }
            Console.WriteLine("[3/4] 已连接原版 WebView2。现在请在原版里正常操作：");
            Console.WriteLine("      ① 先生成一条原版本来就能成功的视频；");
            Console.WriteLine("      ② 如果原版能选 15 秒/30 秒，再各成功生成一次；");
            Console.WriteLine("      ③ 对成功视频执行原版的保存/无水印/原片操作。\n");
            Console.WriteLine("完成这些操作后，回到这个黑色窗口，按 Enter 停止并导出。\n");
            using var cts = new CancellationTokenSource();
            var captureTask = manager.RunAsync(cts.Token);
            Console.ReadLine();
            cts.Cancel();
            try { await captureTask; } catch (OperationCanceledException) { }
            Console.WriteLine("[4/4] 正在脱敏、汇总并打包...");
            manifest.EndedAtUtc = DateTime.UtcNow;
            manifest.TargetsSeen = manager.TargetsSeen;
            manifest.RequestsCaptured = journal.RequestCount;
            manifest.ResponsesCaptured = journal.ResponseCount;
            manifest.EventSourceMessagesCaptured = journal.EventSourceCount;
            manifest.WebSocketFramesCaptured = journal.WebSocketCount;
            await journal.WriteManifestAsync(manifest);
            await journal.WriteSummaryAsync(manager.BuildSummary());
            await journal.WriteReadmeAsync();
            var zip = ZipSession(sessionRoot);
            Console.WriteLine($"\n采集完成：\n{zip}\n");
            Console.WriteLine("这个 ZIP 可以发回给我。它不包含 Cookie/Authorization/Token/密码原文；敏感值只保留字段名、长度和不可逆 SHA-256 指纹。\n");
            Console.WriteLine("按 Enter 退出。");
            Console.ReadLine();
            return 0;
        }
        catch (Exception ex)
        {
            await journal.WriteErrorAsync(ex.ToString());
            Console.WriteLine("采集器发生错误：" + ex.Message);
            Console.WriteLine($"诊断目录：{sessionRoot}");
            return 10;
        }
        finally { await manager.DisposeAsync(); }
    }

    private static void Banner()
    {
        Console.WriteLine("========================================================");
        Console.WriteLine(" AI Video Hub V4 - 原版成功轨迹采集器 4.0.0-capture1");
        Console.WriteLine("========================================================");
        Console.WriteLine("用途：只读记录原版 WebView2 的真实生成/任务/VID/原片工作流。\n");
        Console.WriteLine("隐私：不保存 Cookie、Authorization、Token、密码、会话密钥原文；");
        Console.WriteLine("      不修改原版程序，不绕过授权，不上传任何数据。所有文件仅保存在本机。\n");
    }

    private static string? ResolveOriginalExe(string[] args)
    {
        if (args.Length > 0)
        {
            var p = NormalizePath(args[0]);
            if (File.Exists(p)) return p;
        }
        foreach (var name in new[] { "DoubaoAccountManager.exe", "无限SD20更新版.exe" })
        {
            var p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p)) return p;
        }
        Console.WriteLine("请把原版主程序 EXE 拖到这个窗口，然后按 Enter：");
        var input = Console.ReadLine();
        var path = NormalizePath(input ?? "");
        if (!File.Exists(path))
        {
            Console.WriteLine("文件不存在：" + path);
            return null;
        }
        return path;
    }

    private static string NormalizePath(string p) => p.Trim().Trim('"').Trim();

    private static Process LaunchOriginal(string exe, int port)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false
        };
        var existing = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? "";
        var debug = $"--remote-debugging-port={port} --remote-debugging-address=127.0.0.1";
        psi.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = string.IsNullOrWhiteSpace(existing) ? debug : existing + " " + debug;
        return Process.Start(psi) ?? throw new InvalidOperationException("无法启动原版程序。");
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static string ZipSession(string sessionRoot)
    {
        var zip = sessionRoot.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(sessionRoot, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zip;
    }
}

internal sealed class TargetManager : IAsyncDisposable
{
    private readonly int _port;
    private readonly CaptureJournal _journal;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly ConcurrentDictionary<string, CdpTargetCapture> _connections = new();
    public int TargetsSeen { get; private set; }
    public TargetManager(int port, CaptureJournal journal) { _port = port; _journal = journal; }

    public async Task<bool> WaitForDebuggerAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try { if ((await ListTargetsAsync()).Count > 0) return true; } catch { }
            await Task.Delay(400);
        }
        return false;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var target in await ListTargetsAsync())
                {
                    if (string.IsNullOrWhiteSpace(target.Id) || string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)) continue;
                    if (_connections.ContainsKey(target.Id)) continue;
                    var capture = new CdpTargetCapture(target, _journal);
                    if (_connections.TryAdd(target.Id, capture))
                    {
                        TargetsSeen++;
                        _ = capture.RunAsync(ct).ContinueWith(_ =>
                        {
                            _connections.TryRemove(target.Id, out _);
                            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        }, TaskScheduler.Default);
                    }
                }
            }
            catch (Exception ex) { await _journal.WriteErrorAsync("target-poll: " + ex.Message); }
            await Task.Delay(700, ct);
        }
    }

    private async Task<List<DevToolsTarget>> ListTargetsAsync()
    {
        var json = await _http.GetStringAsync($"http://127.0.0.1:{_port}/json/list");
        return JsonSerializer.Deserialize<List<DevToolsTarget>>(json, JsonOptions.Default) ?? [];
    }

    public object BuildSummary() => new
    {
        targetsSeen = TargetsSeen,
        activeTargetsAtStop = _connections.Count,
        capturedAtUtc = DateTime.UtcNow,
        note = "Use network.jsonl to diff original successful workflow against V4 implementation. Sensitive credential values are redacted."
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _connections.Values) await c.DisposeAsync();
        _http.Dispose();
    }
}

internal sealed class CdpTargetCapture : IAsyncDisposable
{
    private readonly DevToolsTarget _target;
    private readonly CaptureJournal _journal;
    private readonly ClientWebSocket _ws = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly ConcurrentDictionary<string, ResponseMeta> _responses = new();
    private long _id;

    public CdpTargetCapture(DevToolsTarget target, CaptureJournal journal) { _target = target; _journal = journal; }

    public async Task RunAsync(CancellationToken ct)
    {
        await _journal.WriteTargetAsync(_target);
        await _ws.ConnectAsync(new Uri(_target.WebSocketDebuggerUrl), ct);
        var receiver = ReceiveLoopAsync(ct);
        await SendAsync("Network.enable", new { maxTotalBufferSize = 100_000_000, maxResourceBufferSize = 5_000_000 }, ct);
        await SendAsync("Runtime.enable", new { }, ct);
        try { await receiver; } catch (OperationCanceledException) { }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[128 * 1024];
        using var message = new MemoryStream();
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult part;
            do
            {
                part = await _ws.ReceiveAsync(buffer, ct);
                if (part.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, part.Count);
                if (message.Length > 8 * 1024 * 1024) { message.SetLength(0); break; }
            } while (!part.EndOfMessage);
            if (message.Length == 0) continue;
            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            JsonObject? root;
            try { root = JsonNode.Parse(text)?.AsObject(); } catch { continue; }
            if (root is null) continue;
            if (root["id"]?.GetValue<long>() is long responseId && _pending.TryRemove(responseId, out var waiter))
            {
                waiter.TrySetResult(root["result"]);
                continue;
            }
            var method = root["method"]?.GetValue<string>() ?? "";
            var p = root["params"] as JsonObject;
            if (p is null) continue;
            await HandleEventAsync(method, p, ct);
        }
    }

    private async Task HandleEventAsync(string method, JsonObject p, CancellationToken ct)
    {
        switch (method)
        {
            case "Network.requestWillBeSent": await HandleRequestAsync(p); break;
            case "Network.responseReceived": HandleResponseMeta(p); break;
            case "Network.loadingFinished": await HandleLoadingFinishedAsync(p, ct); break;
            case "Network.eventSourceMessageReceived": await HandleEventSourceAsync(p); break;
            case "Network.webSocketFrameSent":
            case "Network.webSocketFrameReceived": await HandleWebSocketAsync(method, p); break;
        }
    }

    private async Task HandleRequestAsync(JsonObject p)
    {
        var request = p["request"] as JsonObject;
        if (request is null) return;
        var url = request["url"]?.GetValue<string>() ?? "";
        var postData = request["postData"]?.GetValue<string>() ?? "";
        if (!CaptureFilter.IsInteresting(url, p["type"]?.GetValue<string>() ?? "", postData)) return;
        var item = new JsonObject
        {
            ["kind"] = "request",
            ["atUtc"] = DateTime.UtcNow,
            ["targetId"] = _target.Id,
            ["requestId"] = p["requestId"]?.DeepClone(),
            ["resourceType"] = p["type"]?.DeepClone(),
            ["method"] = request["method"]?.DeepClone(),
            ["url"] = Redactor.SanitizeUrl(url),
            ["headerNames"] = Redactor.HeaderNames(request["headers"] as JsonObject),
            ["safeHeaders"] = Redactor.SafeHeaders(request["headers"] as JsonObject),
            ["postData"] = Redactor.SanitizeBody(postData)
        };
        await _journal.WriteNetworkAsync(item, false);
    }

    private void HandleResponseMeta(JsonObject p)
    {
        var response = p["response"] as JsonObject;
        if (response is null) return;
        var url = response["url"]?.GetValue<string>() ?? "";
        var mime = response["mimeType"]?.GetValue<string>() ?? "";
        var type = p["type"]?.GetValue<string>() ?? "";
        if (!CaptureFilter.IsInteresting(url, type, "") && !mime.Contains("json", StringComparison.OrdinalIgnoreCase) && !mime.Contains("event-stream", StringComparison.OrdinalIgnoreCase)) return;
        var id = p["requestId"]?.GetValue<string>() ?? "";
        if (id.Length == 0) return;
        _responses[id] = new ResponseMeta { Url = url, MimeType = mime, Status = response["status"]?.GetValue<double>() ?? 0, Headers = response["headers"] as JsonObject, ResourceType = type };
    }

    private async Task HandleLoadingFinishedAsync(JsonObject p, CancellationToken ct)
    {
        var id = p["requestId"]?.GetValue<string>() ?? "";
        if (!_responses.TryRemove(id, out var meta)) return;
        string body = "";
        if (CaptureFilter.ShouldCaptureBody(meta.Url, meta.MimeType))
        {
            try
            {
                var result = await SendAsync("Network.getResponseBody", new { requestId = id }, ct);
                body = result?["body"]?.GetValue<string>() ?? "";
                var b64 = result?["base64Encoded"]?.GetValue<bool>() ?? false;
                if (b64 && body.Length > 0 && body.Length < 4_000_000)
                {
                    try { body = Encoding.UTF8.GetString(Convert.FromBase64String(body)); } catch { body = "<binary-base64-redacted>"; }
                }
            }
            catch { body = "<body-unavailable>"; }
        }
        var item = new JsonObject
        {
            ["kind"] = "response", ["atUtc"] = DateTime.UtcNow, ["targetId"] = _target.Id, ["requestId"] = id,
            ["resourceType"] = meta.ResourceType, ["status"] = meta.Status, ["mimeType"] = meta.MimeType,
            ["url"] = Redactor.SanitizeUrl(meta.Url), ["headerNames"] = Redactor.HeaderNames(meta.Headers),
            ["safeHeaders"] = Redactor.SafeHeaders(meta.Headers), ["body"] = Redactor.SanitizeBody(body)
        };
        await _journal.WriteNetworkAsync(item, true);
    }

    private async Task HandleEventSourceAsync(JsonObject p)
    {
        var item = new JsonObject
        {
            ["kind"] = "eventSource", ["atUtc"] = DateTime.UtcNow, ["targetId"] = _target.Id,
            ["requestId"] = p["requestId"]?.DeepClone(), ["eventName"] = p["eventName"]?.DeepClone(),
            ["eventId"] = Redactor.Fingerprint(p["eventId"]?.ToString() ?? ""), ["data"] = Redactor.SanitizeBody(p["data"]?.GetValue<string>() ?? "")
        };
        await _journal.WriteStreamAsync(item, false);
    }

    private async Task HandleWebSocketAsync(string method, JsonObject p)
    {
        var response = p["response"] as JsonObject;
        var payload = response?["payloadData"]?.GetValue<string>() ?? "";
        if (!CaptureFilter.PayloadInteresting(payload)) return;
        var item = new JsonObject
        {
            ["kind"] = method.EndsWith("Sent", StringComparison.Ordinal) ? "websocketSent" : "websocketReceived",
            ["atUtc"] = DateTime.UtcNow, ["targetId"] = _target.Id, ["requestId"] = p["requestId"]?.DeepClone(),
            ["opcode"] = response?["opcode"]?.DeepClone(), ["payload"] = Redactor.SanitizeBody(payload)
        };
        await _journal.WriteStreamAsync(item, true);
    }

    private async Task<JsonNode?> SendAsync(string method, object parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _id);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, method, @params = parameters }));
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        using var reg = timeout.Token.Register(() => tcs.TrySetCanceled(timeout.Token));
        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_ws.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "capture-stop", CancellationToken.None); } catch { }
        _ws.Dispose();
    }
}

internal static class CaptureFilter
{
    private static readonly string[] Keywords = ["completion", "chain/single", "get_play_info", "video", "media", "task", "generate", "creation", "ability", "quota", "cooldown", "seedance"];
    public static bool IsInteresting(string url, string resourceType, string body)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            if (u.Host.Equals("dola.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".dola.com", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.Contains(".mp4", StringComparison.OrdinalIgnoreCase) || url.Contains("video", StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (Keywords.Any(k => url.Contains(k, StringComparison.OrdinalIgnoreCase))) return true;
        if (body.Length > 0 && Keywords.Any(k => body.Contains(k, StringComparison.OrdinalIgnoreCase))) return true;
        return resourceType is "Media" or "WebSocket";
    }
    public static bool ShouldCaptureBody(string url, string mime) => mime.Contains("json", StringComparison.OrdinalIgnoreCase) || mime.Contains("text", StringComparison.OrdinalIgnoreCase) || mime.Contains("event-stream", StringComparison.OrdinalIgnoreCase) || Keywords.Any(k => url.Contains(k, StringComparison.OrdinalIgnoreCase));
    public static bool PayloadInteresting(string payload) => payload.Length < 2_000_000 && Keywords.Any(k => payload.Contains(k, StringComparison.OrdinalIgnoreCase));
}

internal static class Redactor
{
    private static readonly Regex SensitiveKey = new("(?i)(cookie|authorization|token|secret|password|passwd|credential|session|csrf|xsrf|sign|signature|access[_-]?key|private[_-]?key|refresh[_-]?token|email|phone|mobile|user[_-]?id|uid)", RegexOptions.Compiled);
    private static readonly Regex SensitiveQuery = new("(?i)(token|auth|authorization|cookie|sign|signature|session|key|secret|uid|user_id|device_id|did)", RegexOptions.Compiled);

    public static JsonNode? SanitizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        if (body.Length > 2_000_000) return JsonValue.Create($"<body-too-large len={body.Length} sha256={Sha(body)}>");
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return SanitizeNode(JsonNode.Parse(trimmed)); } catch { }
        }
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("\ndata:", StringComparison.OrdinalIgnoreCase))
        {
            var arr = new JsonArray();
            foreach (var line in trimmed.Split('\n').Take(2000))
            {
                var s = line.Trim();
                if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) s = s[5..].Trim();
                if (s.Length == 0) continue;
                try { arr.Add(SanitizeNode(JsonNode.Parse(s))); } catch { arr.Add(SanitizeText(s)); }
            }
            return arr;
        }
        return JsonValue.Create(SanitizeText(trimmed));
    }

    private static JsonNode? SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var clean = new JsonObject();
            foreach (var kv in obj) clean[kv.Key] = SensitiveKey.IsMatch(kv.Key) ? Fingerprint(kv.Value?.ToJsonString() ?? "") : SanitizeNode(kv.Value);
            return clean;
        }
        if (node is JsonArray arr)
        {
            var clean = new JsonArray();
            foreach (var x in arr) clean.Add(SanitizeNode(x));
            return clean;
        }
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return JsonValue.Create(s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? SanitizeUrl(s) : SanitizeText(s));
        return node?.DeepClone();
    }

    private static string SanitizeText(string s)
    {
        if (s.Length > 200_000) return $"<text-too-large len={s.Length} sha256={Sha(s)}>";
        s = Regex.Replace(s, "(?i)(bearer\\s+)[A-Za-z0-9._~+/-]+=*", "$1<redacted>");
        s = Regex.Replace(s, "(?i)(cookie\\s*[:=]\\s*)[^\\s;,]+", "$1<redacted>");
        return s;
    }

    public static string SanitizeUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return raw;
        if (string.IsNullOrEmpty(uri.Query)) return raw;
        var clean = new List<string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            var k = i >= 0 ? part[..i] : part;
            var v = i >= 0 ? part[(i + 1)..] : "";
            clean.Add(SensitiveQuery.IsMatch(Uri.UnescapeDataString(k)) ? $"{k}=<redacted:{Sha(v)[..12]}>" : part);
        }
        return new UriBuilder(uri) { Query = string.Join('&', clean) }.Uri.ToString();
    }

    public static JsonArray HeaderNames(JsonObject? headers)
    {
        var arr = new JsonArray();
        if (headers is null) return arr;
        foreach (var k in headers.Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) arr.Add(k);
        return arr;
    }

    public static JsonObject SafeHeaders(JsonObject? headers)
    {
        var obj = new JsonObject();
        if (headers is null) return obj;
        foreach (var (k, v) in headers)
        {
            if (k.Equals("content-type", StringComparison.OrdinalIgnoreCase) || k.Equals("accept", StringComparison.OrdinalIgnoreCase) || k.Equals("origin", StringComparison.OrdinalIgnoreCase) || k.Equals("referer", StringComparison.OrdinalIgnoreCase)) obj[k] = v?.ToString();
            else if (SensitiveKey.IsMatch(k) || k.StartsWith("x-", StringComparison.OrdinalIgnoreCase) || k.StartsWith("tt-", StringComparison.OrdinalIgnoreCase) || k.StartsWith("tea-", StringComparison.OrdinalIgnoreCase)) obj[k] = Fingerprint(v?.ToString() ?? "");
        }
        return obj;
    }

    public static string Fingerprint(string value) => string.IsNullOrEmpty(value) ? "<redacted:empty>" : $"<redacted len={value.Length} sha256={Sha(value)[..16]}>";
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed class CaptureJournal
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public long RequestCount, ResponseCount, EventSourceCount, WebSocketCount;
    public CaptureJournal(string root) => _root = root;
    public Task WriteManifestAsync(CaptureManifest manifest) => WriteJsonAsync("manifest.json", manifest);
    public Task WriteSummaryAsync(object summary) => WriteJsonAsync("summary.json", summary);
    public Task WriteTargetAsync(DevToolsTarget target) => AppendJsonLineAsync("targets.jsonl", new { atUtc = DateTime.UtcNow, target.id, target.type, title = Redactor.Fingerprint(target.title ?? ""), url = Redactor.SanitizeUrl(target.url ?? "") });
    public Task WriteErrorAsync(string error) => AppendJsonLineAsync("errors.jsonl", new { atUtc = DateTime.UtcNow, error });
    public async Task WriteNetworkAsync(JsonObject item, bool isResponse) { if (isResponse) Interlocked.Increment(ref ResponseCount); else Interlocked.Increment(ref RequestCount); await AppendJsonLineAsync("network.jsonl", item); }
    public async Task WriteStreamAsync(JsonObject item, bool websocket) { if (websocket) Interlocked.Increment(ref WebSocketCount); else Interlocked.Increment(ref EventSourceCount); await AppendJsonLineAsync(websocket ? "websocket.jsonl" : "eventsource.jsonl", item); }
    public async Task WriteReadmeAsync()
    {
        var text = "AI Video Hub V4 原版成功轨迹采集包\n\n用途：对比原版已成功工作流与 V4 的请求顺序、任务状态、VID、媒体解析。\n\n隐私：不保存 Cookie / Authorization / Token / Password / Session / Signature 等敏感值原文。敏感字段只保存字段名、长度和不可逆 SHA-256 指纹。\n\n核心文件：network.jsonl、eventsource.jsonl、websocket.jsonl、targets.jsonl、manifest.json、summary.json。\n";
        await File.WriteAllTextAsync(Path.Combine(_root, "README.txt"), text, Encoding.UTF8);
    }
    private async Task WriteJsonAsync(string name, object value) { await _gate.WaitAsync(); try { await File.WriteAllTextAsync(Path.Combine(_root, name), JsonSerializer.Serialize(value, JsonOptions.Pretty), Encoding.UTF8); } finally { _gate.Release(); } }
    private async Task AppendJsonLineAsync(string name, object value) { var line = JsonSerializer.Serialize(value, JsonOptions.Default) + Environment.NewLine; await _gate.WaitAsync(); try { await File.AppendAllTextAsync(Path.Combine(_root, name), line, Encoding.UTF8); } finally { _gate.Release(); } }
}

internal sealed class CaptureManifest
{
    public string CollectorVersion { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string OriginalExeName { get; set; } = "";
    public string OriginalExeSha256 { get; set; } = "";
    public int OriginalProcessId { get; set; }
    public int RemoteDebugPort { get; set; }
    public string PrivacyMode { get; set; } = "";
    public int TargetsSeen { get; set; }
    public long RequestsCaptured { get; set; }
    public long ResponsesCaptured { get; set; }
    public long EventSourceMessagesCaptured { get; set; }
    public long WebSocketFramesCaptured { get; set; }
}

internal sealed class DevToolsTarget
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string WebSocketDebuggerUrl { get; set; } = "";
}

internal sealed class ResponseMeta
{
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public double Status { get; set; }
    public JsonObject? Headers { get; set; }
    public string ResourceType { get; set; } = "";
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new() { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
