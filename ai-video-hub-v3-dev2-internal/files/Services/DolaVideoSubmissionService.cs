using System.Text.Json;
using System.Text.Json.Nodes;
using AI.VideoHub.V3.Models;
using Microsoft.Web.WebView2.Core;

namespace AI.VideoHub.V3.Services;

public sealed class DolaVideoSubmissionService
{
    private readonly CoreWebView2 _core;
    private readonly DolaProtocolObserver _observer;

    public DolaVideoSubmissionService(CoreWebView2 core, DolaProtocolObserver observer)
    {
        _core = core;
        _observer = observer;
    }

    public async Task<SubmissionResult> SubmitAsync(VideoGenerationRequest request)
    {
        var state = _observer.State;
        var template = state.LastVideoRequest;
        if (template is null || !template.IsVideoRequest)
            return Fail("尚未学习到真实 Dola 视频提交模板。请先在当前账号正常提交一次可用的视频任务，让软件观察真实协议。\nV3 不再猜接口。" );
        if (request.DurationSeconds == 15 && !state.ServerAdvertised15)
            return Fail("当前账号/页面响应尚未明确暴露 15 秒能力，V3 拒绝伪造 15 秒权限。" );
        if (string.IsNullOrWhiteSpace(template.DurationPath))
            return Fail("已观察到视频请求，但没有定位到真实时长字段路径。" );

        JsonNode? root;
        try { root = JsonNode.Parse(template.Body); }
        catch (Exception ex) { return Fail("提交模板不是可编辑 JSON：" + ex.Message); }
        if (root is null) return Fail("提交模板为空。" );

        if (!JsonPathTools.Set(root, template.DurationPath, request.DurationSeconds))
            return Fail("无法写入已学习的时长字段：" + template.DurationPath);
        if (!string.IsNullOrWhiteSpace(template.PromptPath)) JsonPathTools.Set(root, template.PromptPath, request.Prompt);
        if (!string.IsNullOrWhiteSpace(template.RatioPath)) JsonPathTools.Set(root, template.RatioPath, request.AspectRatio);


        // New submission must not inherit task/media evidence from a previous generation.
        state.LastTaskId = "";
        state.LastTaskStatus = "";
        state.LastTaskDurationSeconds = null;
        state.LastTaskAcceptedAtUtc = null;
        state.HasGeneratingTask = false;
        state.LastKnownVid = "";
        state.LastLifecycleEvidence = "";

        var safeHeaders = template.Headers
            .Where(kv => IsSafeReplayHeader(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (!safeHeaders.ContainsKey("content-type")) safeHeaders["content-type"] = "application/json";

        var payload = new
        {
            url = template.Url,
            method = template.Method,
            headers = safeHeaders,
            body = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var script = $$"""
(async () => {
  const p = {{payloadJson}};
  try {
    const r = await fetch(p.url, { method: p.method, headers: p.headers, body: p.body, credentials: 'include' });
    const text = await r.text();
    return JSON.stringify({ ok: r.ok, status: r.status, body: text.slice(0, 12000) });
  } catch (e) {
    return JSON.stringify({ ok: false, status: 0, error: String(e && e.message ? e.message : e) });
  }
})()
""";
        DiagnosticLog.Write($"Submitting Dola task through learned browser template: duration={request.DurationSeconds}, ratio={request.AspectRatio}, durationPath={template.DurationPath}");
        try
        {
            var raw = await _core.ExecuteScriptAsync(script);
            var first = JsolSerializer.Deserialize<string>(raw) ?? "{}";
            var result = JsonNode.Parse(first)?.AsObject();
            var ok = result?["ok"]?.GetValue<bool>() ?? false;
            var status = result?["status"]?.GetValue<int>() ?? 0;
            var body = result?["body"]?.GetValue<string>() ?? "";
            var error = result?["error"]?.GetValue<string>() ?? "";
            if (!ok) DiagnosticLog.Write($"Dola submission rejected: HTTP {status} {error} {body[..Math.Min(body.Length, 400)]}");
            else
            {
                DiagnosticLog.Write($"Dola submission accepted by HTTP layer: HTTP {status}. This is NOT yet counted as 15s success until task lifecycle and output duration are verified.");
                if (!string.IsNullOrWhiteSpace(body)) _observer.InspectResponseText(body, template.Url);
            }
            return new SubmissionResult
            {
                Success = ok, HttpStatus = status, BodyPreview = body[..Math.Min(body.Length, 1000)],
                TaskId = _observer.State.LastTaskId, TaskStatus = _observer.State.LastTaskStatus, Error = error
            };
        }
        catch (Exception ex) { return Fail("页面内提交失败：" + ex.Message); }
    }

    private static bool IsSafeReplayHeader(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n is "cookie" or "host" or "content-length" or "origin" or "referer" or "user-agent") return false;
        if (n.StartsWith("sec-") || n.StartsWith(":")) return false;
        return n is "accept" or "content-type" or "x-requested-with" || n.StartsWith("x-") || n.StartsWith("tt-") || n.StartsWith("tea-");
    }

    private static SubmissionResult Fail(string error)
    {
        DiagnosticLog.Write(error);
        return new SubmissionResult { Success = false, Error = error };
    }
}
