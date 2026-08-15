using System.Text.Json.Nodes;
using AI.VideoHub.V3.Models;

namespace AI.VideoHub.V3.Services;

public static class DolaLifecycleInspector
{
    public static DolaProtocolState ExtractFromText(string text)
    {
        var state = new DolaProtocolState();
        foreach (var document in EnumerateJsonDocuments(text))
            Walk(document, state, "$", "", "", 0);
        return state;
    }

    public static void ApplyObject(JsonObject obj, DolaProtocolState state, string path, string taskId, string vid)
    {
        var resolvedTaskId = FirstString(obj, "task_id", "taskId", "job_id", "jobId", "creation_id") ?? taskId;
        var resolvedVid = FirstString(obj, "vid", "video_id", "video_key", "video_vid") ?? vid;
        if (!string.IsNullOrWhiteSpace(resolvedTaskId)) state.LastTaskId = resolvedTaskId;
        if (!string.IsNullOrWhiteSpace(resolvedVid)) state.LastKnownVid = resolvedVid;

        var status = FirstString(obj, "status", "task_status", "taskStatus", "state");
        if (!string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(resolvedTaskId))
        {
            state.LastTaskStatus = status!;
            var normalized = status!.Trim().ToLowerInvariant();
            state.HasGeneratingTask = normalized is "accepted" or "queued" or "pending" or "running" or "processing" or "generating" or "in_progress";
            if (normalized is "accepted" or "queued" && state.LastTaskAcceptedAtUtc is null)
                state.LastTaskAcceptedAtUtc = DateTime.UtcNow;
            if (normalized is "success" or "succeeded" or "completed" or "done" or "failed" or "error" or "cancelled" or "canceled")
                state.HasGeneratingTask = false;
            state.LastLifecycleEvidence = $"{path}: task={resolvedTaskId}; status={status}; vid={resolvedVid}";
        }

        foreach (var key in new[] { "duration_seconds", "video_duration_seconds", "video_duration", "duration" })
            if (ToInt(obj[key]) is int duration && duration is >= 1 and <= 60)
            {
                state.LastTaskDurationSeconds = duration;
                break;
            }

        foreach (var key in new[] { "remaining_count", "remaining_video_count", "video_remaining_count", "remaining" })
            if (ToInt(obj[key]) is int remaining && remaining >= 0)
            {
                state.RemainingVideoCount = remaining;
                break;
            }

        var quota = FirstString(obj, "quota_status", "video_quota_status", "quotaStatus");
        if (!string.IsNullOrWhiteSpace(quota)) state.VideoQuotaStatus = quota!;

        foreach (var key in new[] { "cooldown_until", "cooldown_until_utc", "video_cooldown_until", "next_available_at" })
        {
            if (obj[key] is JsonValue v && v.TryGetValue<string>(out var text) && DateTime.TryParse(text, out var dt))
            {
                state.VideoCooldownUntilUtc = dt.ToUniversalTime();
                break;
            }
        }
    }

    private static void Walk(JsonNode? node, DolaProtocolState state, string path, string inheritedTaskId, string inheritedVid, int depth)
    {
        if (node is null || depth > 24) return;
        if (node is JsonObject obj)
        {
            var localTaskId = FirstString(obj, "task_id", "taskId", "job_id", "jobId", "creation_id") ?? inheritedTaskId;
            var localVid = FirstString(obj, "vid", "video_id", "video_key", "video_vid") ?? inheritedVid;
            ApplyObject(obj, state, path, inheritedTaskId, inheritedVid);
            foreach (var (key, child) in obj)
                Walk(child, state, path + "." + key, localTaskId, localVid, depth + 1);
            return;
        }

        if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                Walk(array[i], state, $"{path}[{i}]", inheritedTaskId, inheritedVid, depth + 1);
    }

    private static IEnumerable<JsonNode> EnumerateJsonDocuments(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) yield break;
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            JsonNode? direct = null;
            try { direct = JsonNode.Parse(text); } catch { }
            if (direct is not null) yield return direct;
            yield break;
        }

        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) s = s[5..].Trim();
            if (!(s.StartsWith('{') || s.StartsWith('['))) continue;
            JsonNode? node = null;
            try { node = JsonNode.Parse(s); } catch { }
            if (node is not null) yield return node;
        }
    }

    private static string? FirstString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is JsonValue v)
            {
                if (v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) return s;
                if (v.TryGetValue<long>(out var l)) return l.ToString();
                if (v.TryGetValue<int>(out var i)) return i.ToString();
            }
        }
        return null;
    }

    private static int? ToInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out i) ? i : null;
    }
}
