using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public static class DolaProtocolInspector
{
    private static readonly HashSet<string> DurationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "duration", "video_duration", "duration_seconds", "video_duration_seconds"
    };
    private static readonly HashSet<string> ModelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "video_model", "model_name", "modelName"
    };
    private static readonly HashSet<string> AbilityParamKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ability_param", "ability_parameter", "abilityParam", "abilityParameter"
    };

    public static DolaRequestInspection InspectAndMaybePatchRequest(
        string method,
        string uri,
        string contentType,
        byte[] body,
        bool enable15,
        bool serverAdvertised15)
    {
        if (!IsDolaUri(uri) || body.Length == 0)
            return new(new(body, false, false, null, null, "", "other", "not a Dola request"), null);

        var kind = DetectBodyKind(contentType, body);
        return kind switch
        {
            "json" => InspectJson(method, uri, body, enable15, serverAdvertised15),
            "form" => InspectForm(method, uri, body, enable15, serverAdvertised15),
            _ => new(new(body, false, false, null, null, "", kind, $"Dola {kind}; {body.Length} bytes"), null)
        };
    }

    private static DolaRequestInspection InspectJson(string method, string uri, byte[] body, bool enable15, bool serverAdvertised15)
    {
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(body));
            if (root is null) return Empty(body, "json", "invalid json");

            var scan = new ScanState();
            Scan(root, "$", scan, 0);
            var isCompletion = UriHas(uri, "completion") || UriHas(uri, "chat") || UriHas(uri, "message") || UriHas(uri, "chain");
            var isVideo = scan.HasVideoAbility || scan.Duration is not null || scan.VideoSignals.Count > 0;
            var snapshot = BuildSnapshot(method, uri, "application/json", root, scan, isCompletion || isVideo);

            if (enable15 && serverAdvertised15 && isVideo && scan.Duration is not null && IsMutable(method))
            {
                var changed = PatchVideoDurations(root, false, 0);
                if (changed)
                {
                    var patched = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                    return new(new(patched, true, true, scan.Duration, 15, scan.DurationPath, "json",
                        $"DOLA HOST json; duration {scan.Duration}->15; path={scan.DurationPath}; model={scan.Model}"), snapshot);
                }
            }

            return new(new(body, isVideo, false, scan.Duration, null, scan.DurationPath, "json",
                $"DOLA HOST json; video={isVideo}; duration={scan.Duration?.ToString() ?? "?"}; path={scan.DurationPath}; model={scan.Model}"), snapshot);
        }
        catch (Exception ex)
        {
            return Empty(body, "json", "Dola json parse failed: " + ex.Message);
        }
    }

    private static DolaRequestInspection InspectForm(string method, string uri, byte[] body, bool enable15, bool serverAdvertised15)
    {
        try
        {
            var text = Encoding.UTF8.GetString(body);
            var parts = text.Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
            var duration = (int?)null;
            var path = "";
            var model = "";
            var hasVideo = false;
            var abilityJson = "";
            var abilityParam = "";
            var changed = false;

            for (var i = 0; i < parts.Count; i++)
            {
                var eq = parts[i].IndexOf('=');
                var rawKey = eq >= 0 ? parts[i][..eq] : parts[i];
                var rawValue = eq >= 0 ? parts[i][(eq + 1)..] : "";
                var key = WebUtility.UrlDecode(rawKey) ?? "";
                var value = WebUtility.UrlDecode(rawValue) ?? "";
                if (ContainsVideoSignal(key) || ContainsVideoSignal(value)) hasVideo = true;
                if (key.Equals("ability_type", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var abilityType) && abilityType == 17) hasVideo = true;
                if (ModelKeys.Contains(key)) model = value;
                if (DurationKeys.Contains(key) && int.TryParse(value, out var d) && d is >= 1 and <= 30)
                {
                    duration ??= d; path = "$form." + key; hasVideo = true;
                    if (enable15 && serverAdvertised15 && IsMutable(method)) { parts[i] = Uri.EscapeDataString(key) + "=15"; changed = true; }
                }
                else if (LooksLikeJson(value))
                {
                    try
                    {
                        var nested = JsonNode.Parse(value);
                        if (nested is null) continue;
                        var scan = new ScanState(); Scan(nested, "$", scan, 0);
                        hasVideo |= scan.HasVideoAbility || scan.Duration is not null || scan.VideoSignals.Count > 0;
                        duration ??= scan.Duration;
                        if (scan.Duration is not null) path = "$form." + key + scan.DurationPath;
                        if (string.IsNullOrWhiteSpace(model)) model = scan.Model;
                        if (AbilityParamKeys.Contains(key)) abilityParam = value;
                        if (scan.HasVideoAbility) abilityJson = value;
                        if (enable15 && serverAdvertised15 && hasVideo && PatchVideoDurations(nested, false, 0))
                        {
                            parts[i] = Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(nested.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                            changed = true;
                        }
                    }
                    catch { }
                }
            }

            var snapshot = new DolaRuntimeProtocolSnapshot
            {
                CompletionPath = SafePath(uri), CompletionMethod = method, ContentType = "application/x-www-form-urlencoded",
                CompletionTemplateJson = text, AbilityTemplateJson = abilityJson, AbilityParameterTemplateJson = abilityParam,
                HasVideoAbility = hasVideo, HasDurationField = duration is not null, VideoModel = model,
                DurationPath = path, Evidence = $"form; video={hasVideo}; duration={duration}; path={path}; model={model}", LearnedAtUtc = DateTime.UtcNow
            };

            var output = changed ? Encoding.UTF8.GetBytes(string.Join('&', parts)) : body;
            return new(new(output, hasVideo, changed, duration, changed ? 15 : null, path, "form",
                changed ? $"DOLA HOST form; duration {duration}->15; path={path}" : $"DOLA HOST form; duration={duration}; path={path}"), snapshot);
        }
        catch (Exception ex)
        {
            return Empty(body, "form", "Dola form parse failed: " + ex.Message);
        }
    }

    private static DolaRuntimeProtocolSnapshot? BuildSnapshot(string method, string uri, string contentType, JsonNode root, ScanState scan, bool relevant)
    {
        if (!relevant) return null;
        return new DolaRuntimeProtocolSnapshot
        {
            CompletionPath = SafePath(uri), CompletionMethod = method, ContentType = contentType,
            CompletionTemplateJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            AbilityTemplateJson = scan.AbilityTemplateJson,
            AbilityParameterTemplateJson = scan.AbilityParameterTemplateJson,
            HasVideoAbility = scan.HasVideoAbility || scan.VideoSignals.Count > 0,
            HasDurationField = scan.Duration is not null,
            VideoModel = scan.Model,
            DurationPath = scan.DurationPath,
            Evidence = $"path={SafePath(uri)}; video={scan.HasVideoAbility || scan.VideoSignals.Count > 0}; duration={scan.Duration}; durationPath={scan.DurationPath}; model={scan.Model}",
            LearnedAtUtc = DateTime.UtcNow
        };
    }

    private static void Scan(JsonNode node, string path, ScanState state, int depth)
    {
        if (depth > 18) return;
        if (node is JsonObject obj)
        {
            var looksAbility = obj.Any(kv => kv.Key.Contains("ability", StringComparison.OrdinalIgnoreCase));
            if (looksAbility && string.IsNullOrWhiteSpace(state.AbilityTemplateJson))
                state.AbilityTemplateJson = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            foreach (var kv in obj)
            {
                var childPath = path + "." + kv.Key;
                var key = kv.Key;
                if (ContainsVideoSignal(key)) state.VideoSignals.Add(key);
                if (key.Equals("ability_type", StringComparison.OrdinalIgnoreCase) && TryInt(kv.Value, out var abilityType) && abilityType == 17)
                    state.HasVideoAbility = true;
                if (DurationKeys.Contains(key) && TryInt(kv.Value, out var d) && d is >= 1 and <= 30)
                {
                    state.Duration ??= d; state.DurationPath = childPath;
                }
                if (ModelKeys.Contains(key) && kv.Value is JsonValue mv && mv.TryGetValue<string>(out var model) && !string.IsNullOrWhiteSpace(model))
                {
                    state.Model = model;
                    if (ContainsVideoSignal(model)) state.HasVideoAbility = true;
                }
                if (AbilityParamKeys.Contains(key))
                {
                    if (kv.Value is JsonValue av && av.TryGetValue<string>(out var aps) && !string.IsNullOrWhiteSpace(aps)) state.AbilityParameterTemplateJson = aps;
                    else if (kv.Value is JsonNode an) state.AbilityParameterTemplateJson = an.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                }

                if (kv.Value is JsonValue sv && sv.TryGetValue<string>(out var nestedText) && LooksLikeJson(nestedText))
                {
                    try
                    {
                        var nested = JsonNode.Parse(nestedText);
                        if (nested is not null) Scan(nested, childPath + "<json>", state, depth + 1);
                    }
                    catch { }
                }
                else if (kv.Value is JsonNode child) Scan(child, childPath, state, depth + 1);
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++) if (arr[i] is JsonNode child) Scan(child, $"{path}[{i}]", state, depth + 1);
        }
    }

    private static bool PatchVideoDurations(JsonNode node, bool inVideoContext, int depth)
    {
        if (depth > 18) return false;
        var changed = false;
        if (node is JsonObject obj)
        {
            var localVideo = inVideoContext || obj.Any(kv => ContainsVideoSignal(kv.Key)) ||
                             obj.Any(kv => kv.Key.Equals("ability_type", StringComparison.OrdinalIgnoreCase) && TryInt(kv.Value, out var t) && t == 17) ||
                             obj.Any(kv => ModelKeys.Contains(kv.Key) && kv.Value is JsonValue mv && mv.TryGetValue<string>(out var model) && ContainsVideoSignal(model));

            foreach (var key in obj.Select(x => x.Key).ToList())
            {
                var child = obj[key];
                if (DurationKeys.Contains(key) && localVideo && TryInt(child, out var d) && d is >= 1 and <= 30)
                {
                    if (child is JsonValue v && v.TryGetValue<string>(out _)) obj[key] = "15"; else obj[key] = 15;
                    changed = true; continue;
                }
                if (child is JsonValue sv && sv.TryGetValue<string>(out var text) && LooksLikeJson(text))
                {
                    try
                    {
                        var nested = JsonNode.Parse(text);
                        if (nested is not null && PatchVideoDurations(nested, localVideo || ContainsVideoSignal(key), depth + 1))
                        {
                            obj[key] = nested.ToJsonString(new JsonSerializerOptions { WriteIndented = false }); changed = true;
                        }
                    }
                    catch { }
                }
                else if (child is JsonNode n && PatchVideoDurations(n, localVideo || ContainsVideoSignal(key), depth + 1)) changed = true;
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++) if (arr[i] is JsonNode child && PatchVideoDurations(child, inVideoContext, depth + 1)) changed = true;
        }
        return changed;
    }

    private static bool TryInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue<int>(out value)) return true;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out value);
    }

    private static bool ContainsVideoSignal(string? text)
    {
        var s = (text ?? "").ToLowerInvariant();
        return s.Contains("video") || s.Contains("seedance") || s.Contains("creation") || s.Contains("aigc") || s.Contains("camera_movement");
    }

    private static bool IsMutable(string method) => method.Equals("POST", StringComparison.OrdinalIgnoreCase) || method.Equals("PUT", StringComparison.OrdinalIgnoreCase) || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
    private static bool UriHas(string uri, string hint) => uri.Contains(hint, StringComparison.OrdinalIgnoreCase);
    private static bool IsDolaUri(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Host.Equals("dola.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".dola.com", StringComparison.OrdinalIgnoreCase));
    private static string SafePath(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.AbsolutePath : uri;
    private static bool LooksLikeJson(string? text) { var s = (text ?? "").Trim(); return s.Length > 1 && ((s[0] == '{' && s[^1] == '}') || (s[0] == '[' && s[^1] == ']')); }
    private static string DetectBodyKind(string contentType, byte[] body)
    {
        var ct = (contentType ?? "").ToLowerInvariant();
        if (ct.Contains("json")) return "json";
        if (ct.Contains("x-www-form-urlencoded")) return "form";
        var head = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 256)).TrimStart();
        if (head.StartsWith('{') || head.StartsWith('[')) return "json";
        if (head.Contains('=') && head.Contains('&')) return "form";
        return "binary";
    }

    private static DolaRequestInspection Empty(byte[] body, string kind, string summary) => new(new(body, false, false, null, null, "", kind, summary), null);

    private sealed class ScanState
    {
        public int? Duration { get; set; }
        public string DurationPath { get; set; } = "";
        public string Model { get; set; } = "";
        public bool HasVideoAbility { get; set; }
        public string AbilityTemplateJson { get; set; } = "";
        public string AbilityParameterTemplateJson { get; set; } = "";
        public HashSet<string> VideoSignals { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
