using System.Text;
using System.Text.Json.Nodes;
using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public static class ProtocolSelfTest
{
    public static string Run()
    {
        var direct = """{"model":"seedance_v2.0","duration_seconds":10,"prompt":"test"}""";
        var p1 = DoubaoProtocolInspector.InspectAndMaybePatchRequest(
            "POST", "https://www.doubao.com/api/video/generate", "application/json",
            Encoding.UTF8.GetBytes(direct), enable15: true);
        Require(p1.IsVideoCandidate && p1.Patched && p1.DurationBefore == 10 && p1.DurationAfter == 15, "direct JSON 10->15 patch failed");
        var directNode = JsonNode.Parse(Encoding.UTF8.GetString(p1.Body))!;
        Require(directNode["duration_seconds"]?.GetValue<int>() == 15, "direct JSON output is not 15");

        var nestedPayload = """{"ability_type":17,"ability_param":"{\"video_model\":\"seedance_v2.0\",\"duration\":10,\"ratio\":\"9:16\"}"}""";
        var p2 = DoubaoProtocolInspector.InspectAndMaybePatchRequest(
            "POST", "https://www.doubao.com/api/creation", "application/json",
            Encoding.UTF8.GetBytes(nestedPayload), enable15: true);
        Require(p2.IsVideoCandidate && p2.Patched && p2.DurationBefore == 10, "nested ability_param patch not detected");
        var p2Root = JsonNode.Parse(Encoding.UTF8.GetString(p2.Body))!;
        var innerText = p2Root["ability_param"]!.GetValue<string>();
        var inner = JsonNode.Parse(innerText)!;
        Require(inner["duration"]?.GetValue<int>() == 15, "nested ability_param was not written back as 15");

        var form = "model=seedance_v2.0&duration_seconds=10&ratio=9%3A16";
        var p3 = DoubaoProtocolInspector.InspectAndMaybePatchRequest(
            "POST", "https://www.doubao.com/api/video/generate", "application/x-www-form-urlencoded",
            Encoding.UTF8.GetBytes(form), enable15: true);
        Require(p3.Patched && Encoding.UTF8.GetString(p3.Body).Contains("duration_seconds=15", StringComparison.Ordinal), "form 10->15 patch failed");

        var context = new CaptureContext("selftest", "acc", "test", "Doubao");
        var heicJson = """{"task_id":"t1","model":"seedance_v2.0","duration":10,"image_url":"https://p3-flow-imagex-sign.byteimg.com/test.heic"}""";
        var s1 = DoubaoProtocolInspector.ScanResponse("https://www.doubao.com/api/task", "application/json", Encoding.UTF8.GetBytes(heicJson), context);
        Require(s1.Media.Count == 0, "HEIC was incorrectly treated as video media");

        var originalJson = """{"task_id":"t2","model":"seedance_v2.0","ability_param":"{\"duration\":15}","status":"completed","result":{"video_url":"https://v.example.com/play.mp4","no_watermark_url":"https://v.example.com/original.mp4"}}""";
        var s2 = DoubaoProtocolInspector.ScanResponse("https://www.doubao.com/api/task", "application/json", Encoding.UTF8.GetBytes(originalJson), context);
        Require(s2.Tasks.Any(t => t.RemoteTaskId == "t2" && t.DurationSeconds == 15), "nested response duration=15 was not parsed");
        Require(s2.Media.Any(m => m.Url.Contains("original.mp4", StringComparison.Ordinal) && m.IsPreferredOriginal && m.IsVerifiedVideo), "explicit no_watermark original was not recognized");
        Require(s2.Media.Any(m => m.Url.Contains("play.mp4", StringComparison.Ordinal) && !m.IsPreferredOriginal), "normal playback resource missing or misclassified");

        var rankBest = MediaRanker.ChooseBestOriginal(s2.Media);
        Require(rankBest?.Url.Contains("original.mp4", StringComparison.Ordinal) == true, "original ranker did not choose explicit no_watermark resource");

        return $"protocol-selftest=PASS direct={p1.DurationBefore}->{p1.DurationAfter} nested={p2.DurationBefore}->15 media={s2.Media.Count}";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
