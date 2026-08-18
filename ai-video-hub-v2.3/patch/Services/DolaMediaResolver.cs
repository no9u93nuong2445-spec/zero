using System.Text;
using System.Text.Json;
using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public static class DolaMediaResolver
{
    public static List<MediaResource> ParsePlayInfo(JsonElement payload, string vid, CaptureContext context)
    {
        var result = new List<MediaResource>();
        JsonElement data = payload;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("payload", out var wrapped)) data = wrapped;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var dataNode)) data = dataNode;
        if (data.ValueKind != JsonValueKind.Object) return result;

        void Add(string key, string source, string? raw, int? width, int? height, bool preferred, int score, string verification)
        {
            var url = DecodeMaybeBase64(raw);
            if (string.IsNullOrWhiteSpace(url) || !DoubaoProtocolInspector.IsStrongVideoUrl(url, key + " " + source)) return;
            if (result.Any(x => x.Url == url)) return;
            result.Add(new MediaResource
            {
                SessionId = context.SessionId, AccountId = context.AccountId, AccountName = context.AccountName,
                Kind = "video", SourceKey = key, Url = url, TaskId = vid, Width = width, Height = height,
                IsVerifiedVideo = true, IsPreferredOriginal = preferred, Score = score,
                Verification = verification, ProtocolPath = "dola.get_play_info." + source,
                SafeDisplay = SafeDisplay(key, url)
            });
        }

        if (data.TryGetProperty("original_media_info", out var om) && om.ValueKind == JsonValueKind.Object)
        {
            Add("original_media_info.main_url", "original_media_info", GetString(om, "main_url"), GetInt(om, "width") ?? GetNestedInt(om, "meta", "width"), GetInt(om, "height") ?? GetNestedInt(om, "meta", "height"), true, 520,
                "Dola get_play_info 明确返回 original_media_info.main_url");
        }
        Add("no_watermark_url", "data", GetString(data, "no_watermark_url"), GetInt(data, "width"), GetInt(data, "height"), true, 500,
            "Dola get_play_info 明确返回 no_watermark_url");
        Add("original_url", "data", GetString(data, "original_url"), GetInt(data, "width"), GetInt(data, "height"), true, 480,
            "Dola get_play_info 明确返回 original_url");

        if (data.TryGetProperty("video_list", out var vl) && vl.ValueKind == JsonValueKind.Object)
            ParseVideoList(vl, Add);
        if (data.TryGetProperty("video_info", out var vi) && vi.ValueKind == JsonValueKind.Object && vi.TryGetProperty("data", out var vidata) && vidata.ValueKind == JsonValueKind.Object && vidata.TryGetProperty("video_list", out var nestedVl) && nestedVl.ValueKind == JsonValueKind.Object)
            ParseVideoList(nestedVl, Add);

        if (data.TryGetProperty("play_info", out var pi) && pi.ValueKind == JsonValueKind.Object)
            ParsePlayInfoObject(pi, Add);
        if (data.TryGetProperty("play_infos", out var pis) && pis.ValueKind == JsonValueKind.Array)
            foreach (var item in pis.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) ParsePlayInfoObject(item, Add);

        return result.OrderByDescending(MediaRanker.EffectiveScore).ToList();
    }

    private static void ParseVideoList(JsonElement list, Action<string,string,string?,int?,int?,bool,int,string> add)
    {
        foreach (var p in list.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            var v = p.Value;
            var w = GetInt(v, "vwidth") ?? GetInt(v, "width"); var h = GetInt(v, "vheight") ?? GetInt(v, "height");
            add("video_list." + p.Name + ".main_url", "video_list", GetString(v, "main_url"), w, h, false, 180, "Dola get_play_info video_list 普通播放资源");
            add("video_list." + p.Name + ".backup_url_1", "video_list", GetString(v, "backup_url_1"), w, h, false, 150, "Dola get_play_info video_list 备用播放资源");
        }
    }

    private static void ParsePlayInfoObject(JsonElement p, Action<string,string,string?,int?,int?,bool,int,string> add)
    {
        var w = GetInt(p, "width"); var h = GetInt(p, "height");
        add("play_info.main", "play_info", GetString(p, "main"), w, h, false, 170, "Dola get_play_info 普通播放资源");
        add("play_info.main_url", "play_info", GetString(p, "main_url"), w, h, false, 170, "Dola get_play_info 普通播放资源");
    }

    public static string DecodeMaybeBase64(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return value;
        for (var i = 0; i < 2; i++)
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return decoded;
                value = decoded;
            }
            catch { break; }
        }
        return value;
    }

    private static string SafeDisplay(string key, string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{key} · {u.Host}{u.AbsolutePath}" : key;
    private static string? GetString(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static int? GetInt(JsonElement e, string n)
    {
        if (!e.TryGetProperty(n, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var x)) return x;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out x)) return x;
        return null;
    }
    private static int? GetNestedInt(JsonElement e, string parent, string n) => e.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object ? GetInt(p, n) : null;
}
