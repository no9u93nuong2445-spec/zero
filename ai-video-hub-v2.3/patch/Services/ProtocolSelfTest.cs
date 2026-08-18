using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public static class ProtocolSelfTest
{
    public static string Run()
    {
        var dolaDirect = """{"ability_type":17,"model":"seedance_v2.0","duration":10,"ratio":"9:16","prompt":"selftest"}""";
        var denied = DolaProtocolInspector.InspectAndMaybePatchRequest("POST", "https://www.dola.com/api/chat/completion", "application/json", Encoding.UTF8.GetBytes(dolaDirect), enable15: true, serverAdvertised15: false);
        Require(!denied.Patch.Patched && denied.Patch.DurationBefore == 10, "Dola must not patch 15 seconds without explicit server capability evidence");
        var d1 = DolaProtocolInspector.InspectAndMaybePatchRequest("POST", "https://www.dola.com/api/chat/completion", "application/json", Encoding.UTF8.GetBytes(dolaDirect), enable15: true, serverAdvertised15: true);
        Require(d1.Patch.IsVideoCandidate && d1.Patch.Patched && d1.Patch.DurationBefore == 10 && d1.Patch.DurationAfter == 15, "Dola direct 10->15 patch failed after explicit capability evidence");
        Require(d1.Snapshot?.HasVideoAbility == true && d1.Snapshot.HasDurationField, "Dola runtime protocol snapshot not learned");
        var d1Node = JsonNode.Parse(Encoding.UTF8.GetString(d1.Patch.Body))!;
        Require(d1Node["duration"]?.GetValue<int>() == 15, "Dola direct request was not serialized as 15");
        var nested = """{"message":{"text":"hello"},"ability":{"ability_type":17,"ability_param":"{\"video_model\":\"seedance_v2.0\",\"duration\":10,\"ratio\":\"16:9\",\"camera_movement\":\"fixed\"}"}}""";
        var d2 = DolaProtocolInspector.InspectAndMaybePatchRequest("POST", "https://www.dola.com/chat/completion", "application/json", Encoding.UTF8.GetBytes(nested), enable15: true, serverAdvertised15: true);
        Require(d2.Patch.Patched && d2.Patch.DurationBefore == 10, "Dola nested ability_param patch failed");
        var d2Root = JsonNode.Parse(Encoding.UTF8.GetString(d2.Patch.Body))!;
        var inner = JsonNode.Parse(d2Root["ability"]!["ability_param"]!.GetValue<string>())!;
        Require(inner["duration"]?.GetValue<int>() == 15, "Dola nested ability_param was not written back");
        var form = "ability_type=17&video_model=seedance_v2.0&duration=10&ratio=9%3A16";
        var d3 = DolaProtocolInspector.InspectAndMaybePatchRequest("POST", "https://www.dola.com/api/completion", "application/x-www-form-urlencoded", Encoding.UTF8.GetBytes(form), enable15: true, serverAdvertised15: true);
        Require(d3.Patch.Patched && Encoding.UTF8.GetString(d3.Patch.Body).Contains("duration=15", StringComparison.Ordinal), "Dola form 10->15 patch failed");
        var context = new CaptureContext("selftest", "dola-account", "Dola test", "Dola");
        var playInfo = """{"code":0,"data":{"original_media_info":{"main_url":"https://v.example.com/original.mp4","width":1080,"height":1920},"no_watermark_url":"https://v.example.com/no-wm.mp4","video_list":{"video_2":{"main_url":"aHR0cHM6Ly92LmV4YW1wbGUuY29tL3BsYXkubXA0P2xyPWNpY2lfYWk=","vwidth":1080,"vheight":1920}},"play_info":{"main_url":"https://v.example.com/play2.mp4?watermark=1&logo=dola","width":720,"height":1280}}}""";
        using var doc = JsonDocument.Parse(playInfo);
        var media = DolaMediaResolver.ParsePlayInfo(doc.RootElement, "v0selftest123456", context);
        Require(media.Any(x => x.SourceKey.Contains("original_media_info", StringComparison.OrdinalIgnoreCase) && x.IsPreferredOriginal), "Dola original_media_info not recognized");
        Require(media.Any(x => x.SourceKey == "no_watermark_url" && x.IsPreferredOriginal), "Dola no_watermark_url not recognized");
        Require(media.Any(x => x.Url.Contains("play.mp4", StringComparison.OrdinalIgnoreCase) && !x.IsPreferredOriginal), "Dola playback candidate missing");
        Require(media.All(x => !x.SourceKey.Contains("derived", StringComparison.OrdinalIgnoreCase)), "V2.3 must not synthesize no-watermark URLs");
        var best = MediaRanker.ChooseBestOriginal(media);
        Require(best?.SourceKey.Contains("original_media_info", StringComparison.OrdinalIgnoreCase) == true, "Dola original priority mismatch");
        var doubao = """{"model":"seedance_v2.0","duration_seconds":10,"prompt":"test"}""";
        var dp = DoubaoProtocolInspector.InspectAndMaybePatchRequest("POST", "https://www.doubao.com/api/video/generate", "application/json", Encoding.UTF8.GetBytes(doubao), enable15: false);
        Require(dp.IsVideoCandidate && !dp.Patched && dp.DurationBefore == 10, "Doubao observer regression failed");
        return $"protocol-selftest=PASS gated-deny={denied.Patch.Patched} dola={d1.Patch.DurationBefore}->{d1.Patch.DurationAfter} media={media.Count}";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
