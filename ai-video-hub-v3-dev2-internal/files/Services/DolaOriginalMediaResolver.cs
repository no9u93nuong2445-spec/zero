using System.Text.Json;
using System.Text.Json.Nodes;
using AI.VideoHub.V3.Models;
using Microsoft.Web.WebView2.Core;

namespace AI.VideoHub.V3.Services;

/// <summary>
/// Resolves original media through the currently logged-in Dola web session.
/// This is intentionally limited to explicit original/no-watermark fields returned
/// to the user's own browser session; it does not rewrite watermark query flags.
/// </summary>
public sealed class DolaOriginalMediaResolver
{
    private readonly CoreWebView2 _core;

    public DolaOriginalMediaResolver(CoreWebView2 core) => _core = core;

    public async Task<MediaResource?> ResolveAsync(string vid)
    {
        if (string.IsNullOrWhiteSpace(vid)) return null;

        var request = JsonSerializer.Serialize(new { vid });
        var script = $$"""
(async () => {
  const input = {{request}};
  try {
    if (!location.hostname || !location.hostname.endsWith('dola.com')) {
      return JSON.stringify({ ok:false, error:'not_dola_page' });
    }
    const aid = '489823';
    const tabId = (globalThis.crypto && crypto.randomUUID) ? crypto.randomUUID() :
      'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0; return (c === 'x' ? r : (r & 3 | 8)).toString(16);
      });
    const u = new URL('/samantha/media/get_play_info', location.origin);
    u.searchParams.set('aid', aid);
    u.searchParams.set('device_platform', 'web');
    u.searchParams.set('samantha_web', '1');
    u.searchParams.set('use-olympus-account', '1');
    u.searchParams.set('version_code', '20800');
    u.searchParams.set('pkg_type', 'release_version');
    u.searchParams.set('web_tab_id', tabId);
    const r = await fetch(u.toString(), {
      method: 'POST',
      credentials: 'include',
      headers: { 'accept':'application/json', 'content-type':'application/json' },
      body: JSON.stringify({ key: input.vid, type: 'video' })
    });
    const text = await r.text();
    return JSON.stringify({ ok:r.ok, status:r.status, url:u.toString(), body:text.slice(0, 200000) });
  } catch (e) {
    return JSON.stringify({ ok:false, status:0, error:String(e && e.message ? e.message : e) });
  }
})()
""";

        try
        {
            var outer = await _core.ExecuteScriptAsync(script);
            var inner = JsonSerializer.Deserialize<string>(outer) ?? "{}";
            var envelope = JsonNode.Parse(inner)?.AsObject();
            var ok = envelope?["ok"]?.GetValue<bool>() ?? false;
            var status = envelope?["status"]?.GetValue<int>() ?? 0;
            var body = envelope?["body"]?.GetValue<string>() ?? "";
            if (!ok)
            {
                var error = envelope?["error"]?.GetValue<string>() ?? "";
                DiagnosticLog.Write($"Dola get_play_info failed: HTTP {status}; {error}");
                return null;
            }

            var root = JsonNode.Parse(body)?.AsObject();
            if (root is null || (root["code"] is JsonValue cv && TryInt(cv) is int code && code != 0))
            {
                DiagnosticLog.Write("Dola get_play_info returned non-success JSON.");
                return null;
            }
            var data = root["data"] as JsonObject;
            if (data is null) return null;

            var candidates = new List<MediaResource>();
            if (data["original_media_info"] is JsonObject original)
            {
                Add(candidates, original["main_url"], "$.data.original_media_info.main_url", vid,
                    original["width"] ?? (original["meta"] as JsonObject)?["width"],
                    original["height"] ?? (original["meta"] as JsonObject)?["height"]);
            }
            Add(candidates, data["no_watermark_url"], "$.data.no_watermark_url", vid, data["width"], data["height"]);
            Add(candidates, data["original_url"], "$.data.original_url", vid, data["width"], data["height"]);

            var best = candidates
                .OrderByDescending(x => SourcePriority(x.SourcePath))
                .ThenByDescending(x => (x.Width ?? 0) * (x.Height ?? 0))
                .FirstOrDefault();
            if (best is not null)
                DiagnosticLog.Write($"Dola explicit original resolved for VID={vid}: {best.SourcePath}");
            else
                DiagnosticLog.Write($"Dola get_play_info had no explicit original/no_watermark field for VID={vid}.");
            return best;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("Dola get_play_info resolver error: " + ex.Message);
            return null;
        }
    }

    public static MediaResource? ParseFixture(string json, string vid)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        var data = root?["data"] as JsonObject;
        if (data is null) return null;
        var candidates = new List<MediaResource>();
        if (data["original_media_info"] is JsonObject original)
            Add(candidates, original["main_url"], "$.data.original_media_info.main_url", vid, original["width"], original["height"]);
        Add(candidates, data["no_watermark_url"], "$.data.no_watermark_url", vid, data["width"], data["height"]);
        Add(candidates, data["original_url"], "$.data.original_url", vid, data["width"], data["height"]);
        return candidates.OrderByDescending(x => SourcePriority(x.SourcePath)).ThenByDescending(x => (x.Width ?? 0) * (x.Height ?? 0)).FirstOrDefault();
    }

    private static void Add(List<MediaResource> list, JsonNode? node, string source, string vid, JsonNode? width, JsonNode? height)
    {
        var raw = node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        var url = DecodeUrl(raw);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) return;
        list.Add(new MediaResource
        {
            Url = url,
            SourcePath = source,
            Vid = vid,
            Width = TryInt(width),
            Height = TryInt(height),
            ExplicitOriginal = true,
            Evidence = "explicit get_play_info original field",
            ObservedAtUtc = DateTime.UtcNow
        });
    }

    private static int SourcePriority(string source)
        => source.Contains("original_media_info", StringComparison.OrdinalIgnoreCase) ? 300
         : source.Contains("no_watermark_url", StringComparison.OrdinalIgnoreCase) ? 200
         : source.Contains("original_url", StringComparison.OrdinalIgnoreCase) ? 100 : 0;

    private static string DecodeUrl(string text)
    {
        var current = (text ?? "").Trim();
        for (var i = 0; i < 2; i++)
        {
            if (current.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || current.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return current;
            try
            {
                var bytes = Convert.FromBase64String(current);
                var decoded = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                if (decoded == current) break;
                current = decoded;
            }
            catch { break; }
        }
        return current;
    }

    private static int? TryInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
        return null;
    }
}
