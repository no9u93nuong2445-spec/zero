using System.Text.Json;
using System.Text.Json.Nodes;
using AI.VideoHub.V3.Models;
using Microsoft.Web.WebView2.Core;

namespace AI.VideoHub.V3.Services;

public sealed class DolaProtocolObserver : IAsyncDisposable
{
    private readonly CoreWebView2 _core;
    private readonly string _profileId;
    private CoreWebView2DevToolsProtocolEventReceiver? _requestReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _responseReceiver;
    private readonly HashSet<string> _responseIds = new();
    public DolaProtocolState State { get; private set; } = new();
    public event Action<DolaProtocolState>? StateChanged;
    public event Action<MediaResource>? MediaObserved;

    private static readonly string[] CapabilityArrays = ["supported_durations", "duration_options", "durations", "video_durations", "duration_list"];
    private static readonly string[] MaxDurationKeys = ["max_duration", "max_video_duration", "maxDuration", "maxVideoDuration"];

    public DolaProtocolObserver(CoreWebView2 core, string profileId)
    {
        _core = core;
        _profileId = profileId;
    }

    public async Task StartAsync()
    {
        State = await JsonStore.LoadAsync(AppPaths.ProtocolFile(_profileId), new DolaProtocolState());
        await _core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        _requestReceiver = _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _responseReceiver = _core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _requestReceiver.DevToolsProtocolEventReceived += OnRequestWillBeSent;
        _responseReceiver.DevToolsProtocolEventReceived += OnResponseReceived;
        DiagnosticLog.Write("Dola CDP protocol observer started (observe-only; no request mutation).");
    }

    private void OnRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            var root = JsonNode.Parse(e.ParameterObjectAsJson)?.AsObject();
            var request = root?["request"]?.AsObject();
            var url = request?["url"]?.GetValue<string>() ?? "";
            if (!IsDola(url)) return;

            var method = request?["method"]?.GetValue<string>() ?? "GET";
            var postData = request?["postData"]?.GetValue<string>() ?? "";
            var headers = ReadHeaders(request?["headers"] as JsonObject);
            var contentType = headers.TryGetValue("content-type", out var ct) ? ct : "";

            if (url.Contains("get_play_info", StringComparison.OrdinalIgnoreCase)) State.LastGetPlayInfoUrl = url;
            if (string.IsNullOrWhiteSpace(postData)) { Touch(); return; }

            JsonNode? bodyNode = null;
            try { bodyNode = JsonNode.Parse(postData); } catch { }
            if (bodyNode is null) return;
            var discovered = JsonPathTools.DiscoverPaths(bodyNode);
            var relevantUrl = url.Contains("completion", StringComparison.OrdinalIgnoreCase) || url.Contains("chain/single", StringComparison.OrdinalIgnoreCase);
            if (!discovered.video && !relevantUrl) return;

            State.LastVideoRequest = new ObservedRequestTemplate
            {
                Url = url,
                Method = method,
                Headers = headers,
                Body = postData,
                ContentType = contentType,
                DurationPath = discovered.duration,
                PromptPath = discovered.prompt,
                RatioPath = discovered.ratio,
                IsVideoRequest = discovered.video,
                ObservedAtUtc = DateTime.UtcNow
            };
            DiagnosticLog.Write($"Observed Dola video request template: {new Uri(url).AbsolutePath}; durationPath={discovered.duration}; promptPath={discovered.prompt}; ratioPath={discovered.ratio}");
            Touch();
        }
        catch (Exception ex) { DiagnosticLog.Write("Dola request observer error: " + ex.Message); }
    }

    private async void OnResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            var root = JsonNode.Parse(e.ParameterObjectAsJson)?.AsObject();
            var response = root?["response"]?.AsObject();
            var url = response?["url"]?.GetValue<string>() ?? "";
            if (!IsDola(url)) return;
            var requestId = root?["requestId"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(requestId) || !_responseIds.Add(requestId)) return;
            if (!(url.Contains("completion", StringComparison.OrdinalIgnoreCase) || url.Contains("chain", StringComparison.OrdinalIgnoreCase) || url.Contains("media", StringComparison.OrdinalIgnoreCase) || url.Contains("video", StringComparison.OrdinalIgnoreCase))) return;

            var args = JsonSerializer.Serialize(new { requestId });
            var bodyEnvelope = await _core.CallDevToolsProtocolMethodAsync("Network.getResponseBody", args);
            var bodyJson = JsonNode.Parse(bodyEnvelope)?.AsObject();
            var body = bodyJson?["body"]?.GetValue<string>() ?? "";
            var base64Encoded = bodyJson?["base64Encoded"]?.GetValue<bool>() ?? false;
            if (base64Encoded && body.Length > 0)
            {
                try { body = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
                catch { return; }
            }
            if (body.Length == 0) return;
            InspectResponseText(body, url);
        }
        catch { }
    }

    public void InspectResponseText(string text, string url)
    {
        foreach (var candidate in EnumerateJsonDocuments(text))
        {
            ScanCapability(candidate, "$", 0);
            ScanIdentifiersAndMedia(candidate, "$", "", 0);
        }
        Touch();
    }

    private void ScanCapability(JsonNode? node, string path, int depth)
    {
        if (node is null || depth > 20) return;
        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                var p = path + "." + key;
                if (CapabilityArrays.Contains(key, StringComparer.OrdinalIgnoreCase) && child is JsonArray arr)
                {
                    var values = arr.Select(ToInt).Where(x => x is not null).Select(x => x!.Value).ToList();
                    if (values.Contains(15)) Advertise15($"{p} includes 15");
                }
                if (MaxDurationKeys.Contains(key, StringComparer.OrdinalIgnoreCase) && ToInt(child) is int max && max >= 15)
                    Advertise15($"{p}={max}");
                ScanCapability(child, p, depth + 1);
            }
        }
        else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) ScanCapability(array[i], $"{path}[{i}]", depth + 1);
    }

    private void ScanIdentifiersAndMedia(JsonNode? node, string path, string inheritedVid, int depth)
    {
        if (node is null || depth > 24) return;
        if (node is JsonObject obj)
        {
            var vid = FirstString(obj, "vid", "video_id", "video_key", "video_vid") ?? inheritedVid;
            var taskId = FirstString(obj, "task_id", "taskId", "job_id", "jobId", "creation_id");
            if (!string.IsNullOrWhiteSpace(vid)) State.LastKnownVid = vid;
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                State.LastTaskId = taskId!;
                State.LastLifecycleEvidence = $"{path} exposed task id {taskId}";
            }
            ScanTaskLifecycle(obj, path, taskId ?? State.LastTaskId, vid);

            TryEmitExplicit(obj, "original_media_info", "main_url", path, vid, true);
            TryEmit(obj, "no_watermark_url", path, vid, true);
            TryEmit(obj, "original_url", path, vid, true);
            TryExtractVideoModel(obj, path, vid);

            foreach (var (key, child) in obj) ScanIdentifiersAndMedia(child, path + "." + key, vid, depth + 1);
        }
        else if (node is JsonArray arr) for (var i = 0; i < arr.Count; i++) ScanIdentifiersAndMedia(arr[i], $"{path}[{i}]", inheritedVid, depth + 1);
    }

    private void ScanTaskLifecycle(JsonObject obj, string path, string taskId, string vid)
    {
        var status = FirstString(obj, "status", "task_status", "taskStatus", "state");
        if (!string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(taskId))
        {
            State.LastTaskStatus = status!;
            var normalized = status!.Trim().ToLowerInvariant();
            State.HasGeneratingTask = normalized is "accepted" or "queued" or "pending" or "running" or "processing" or "generating" or "in_progress";
            if (normalized is "accepted" or "queued" && State.LastTaskAcceptedAtUtc is null) State.LastTaskAcceptedAtUtc = DateTime.UtcNow;
            if (normalized is "success" or "succeeded" or "completed" or "done" or "failed" or "error" or "cancelled" or "canceled") State.HasGeneratingTask = false;
            State.LastLifecycleEvidence = $"{path}: task={taskId}; status={status}; vid={vid}";
        }

        foreach (var key in new[] { "duration_seconds", "video_duration_seconds", "video_duration", "duration" })
            if (ToInt(obj[key]) is int duration && duration is >= 1 and <= 60) { State.LastTaskDurationSeconds = duration; break; }

        foreach (var key in new[] { "remaining_count", "remaining_video_count", "video_remaining_count", "remaining" })
            if (ToInt(obj[key]) is int remaining && remaining >= 0) { State.RemainingVideoCount = remaining; break; }

        var quota = FirstString(obj, "quota_status", "video_quota_status", "quotaStatus");
        if (!string.IsNullOrWhiteSpace(quota)) State.VideoQuotaStatus = quota!;

        foreach (var key in new[] { "cooldown_until", "cooldown_until_utc", "video_cooldown_until", "next_available_at" })
        {
            if (obj[key] is JsonValue v && v.TryGetValue<string>(out var text) && DateTime.TryParse(text, out var dt))
            {
                State.VideoCooldownUntilUtc = dt.ToUniversalTime();
                break;
            }
        }
    }

    private void TryEmitExplicit(JsonObject obj, string parentKey, string urlKey, string path, string vid, bool explicitOriginal)
    {
        if (obj[parentKey] is not JsonObject parent) return;
        var url = parent[urlKey]?.GetValue<string>() ?? "";
        Emit(url, $"{path}.{parentKey}.{urlKey}", vid, explicitOriginal, parent["width"], parent["height"]);
    }

    private void TryEmit(JsonObject obj, string key, string path, string vid, bool explicitOriginal)
    {
        var url = obj[key]?.GetValue<string>() ?? "";
        Emit(url, $"{path}.{key}", vid, explicitOriginal, obj["width"], obj["height"]);
    }

    private void TryExtractVideoModel(JsonObject obj, string path, string vid)
    {
        if (obj["video_model"] is not JsonValue vm || !vm.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var model = JsonNode.Parse(text)?.AsObject();
            var list = model?["video_list"]?.AsObject();
            if (list is null) return;
            foreach (var (quality, item) in list)
            {
                if (item is not JsonObject info) continue;
                foreach (var key in new[] { "main_url", "backup_url_1" })
                {
                    var raw = info[key]?.GetValue<string>() ?? "";
                    var decoded = DecodePossibleUrl(raw);
                    if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        Emit(decoded, $"{path}.video_model.video_list.{quality}.{key}", vid, false, info["width"] ?? info["vwidth"], info["height"] ?? info["vheight"]);
                }
            }
        }
        catch { }
    }

    private void Emit(string url, string sourcePath, string vid, bool explicitOriginal, JsonNode? width, JsonNode? height)
    {
        url = DecodePossibleUrl(url);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        var likelyWatermarked = url.Contains("watermark=1", StringComparison.OrdinalIgnoreCase) || url.Contains("logo=", StringComparison.OrdinalIgnoreCase) || url.Contains("watermark", StringComparison.OrdinalIgnoreCase) && !url.Contains("watermark=0", StringComparison.OrdinalIgnoreCase);
        var resource = new MediaResource
        {
            Url = url,
            SourcePath = sourcePath,
            Vid = vid,
            Width = ToInt(width), Height = ToInt(height),
            ExplicitOriginal = explicitOriginal && !likelyWatermarked,
            Evidence = explicitOriginal ? $"Response exposed explicit original field at {sourcePath}" : $"Video model candidate at {sourcePath}",
            ObservedAtUtc = DateTime.UtcNow
        };
        MediaObserved?.Invoke(resource);
    }

    private void Advertise15(string evidence)
    {
        if (State.ServerAdvertised15 && State.Capability15Evidence == evidence) return;
        State.ServerAdvertised15 = true;
        State.Capability15Evidence = evidence;
        DiagnosticLog.Write("Dola server advertised 15s: " + evidence);
    }

    private void Touch()
    {
        State.UpdatedAtUtc = DateTime.UtcNow;
        _ = JsonStore.SaveAsync(AppPaths.ProtocolFile(_profileId), State);
        StateChanged?.Invoke(State);
    }

    private static IEnumerable<JsonNode> EnumerateJsonDocuments(string text)
    {
        text = text.Trim();
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            JsonNode? direct = null; try { direct = JsonNode.Parse(text); } catch { }
            if (direct is not null) yield return direct;
            yield break;
        }
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) s = s[5..].Trim();
            if (!(s.StartsWith('{') || s.StartsWith('['))) continue;
            JsonNode? node = null; try { node = JsonNode.Parse(s); } catch { }
            if (node is not null) yield return node;
        }
    }

    private static Dictionary<string, string> ReadHeaders(JsonObject? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is null) return result;
        foreach (var (k, v) in headers) result[k] = v?.ToString() ?? "";
        return result;
    }

    private static string? FirstString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys) if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) return s;
        return null;
    }

    private static int? ToInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out i) ? i : null;
    }

    private static string DecodePossibleUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var current = raw.Trim();
        for (var i = 0; i < 2; i++)
        {
            if (current.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return current;
            try
            {
                var bytes = Convert.FromBase64String(current);
                var decoded = System.Text.Encoding.UTF8.GetString(bytes);
                if (decoded.Length == 0) break;
                current = decoded;
            }
            catch { break; }
        }
        return current;
    }

    private static bool IsDola(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Host.Equals("dola.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".dola.com", StringComparison.OrdinalIgnoreCase));

    public ValueTask DisposeAsync()
    {
        if (_requestReceiver is not null) _requestReceiver.DevToolsProtocolEventReceived -= OnRequestWillBeSent;
        if (_responseReceiver is not null) _responseReceiver.DevToolsProtocolEventReceived -= OnResponseReceived;
        return ValueTask.CompletedTask;
    }
}
