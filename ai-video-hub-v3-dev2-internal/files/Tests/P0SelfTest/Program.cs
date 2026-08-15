using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using AI.VideoHub.V3.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("ASSERT: " + message);
}

Console.WriteLine("P0 self-test starting...");

var fixture1 = """{"code":0,"data":{"original_media_info":{"main_url":"https://cdn.example/a.mp4","width":1920,"height":1080},"no_watermark_url":"https://cdn.example/b.mp4","original_url":"https://cdn.example/c.mp4"}}""";
var r1 = DolaOriginalMediaResolver.ParseFixture(fixture1, "vid-1");
Assert(r1 is not null, "explicit original fixture must resolve");
Assert(r1!.SourcePath.Contains("original_media_info"), "original_media_info must have top priority");
Assert(r1.ExplicitOriginal, "resolved media must be explicit original");

var fixture2 = """{"code":0,"data":{"no_watermark_url":"https://cdn.example/clean.mp4","width":1080,"height":1920}}""";
var r2 = DolaOriginalMediaResolver.ParseFixture(fixture2, "vid-2");
Assert(r2?.Url == "https://cdn.example/clean.mp4", "no_watermark_url must resolve when original_media_info absent");

var fixture3 = """{"code":0,"data":{"play_info":{"main_url":"https://cdn.example/play.mp4"},"video_list":{"1080p":{"main_url":"https://cdn.example/video.mp4"}}}}""";
Assert(DolaOriginalMediaResolver.ParseFixture(fixture3, "vid-3") is null, "play/video_list must not masquerade as original");

var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("https://cdn.example/base64.mp4"))));
var fixture4 = "{\"code\":0,\"data\":{\"original_url\":\"" + encoded + "\"}}";
Assert(DolaOriginalMediaResolver.ParseFixture(fixture4, "vid-4")?.Url == "https://cdn.example/base64.mp4", "double-base64 original URL must decode");

var requestJson = JsonNode.Parse("""{"ability_type":17,"ability_parameter":{"video_model":"seedance","duration_seconds":10,"prompt":"old","aspect_ratio":"16:9"}}""")!;
var paths = JsonPathTools.DiscoverPaths(requestJson);
Assert(paths.video, "video request must be detected");
Assert(paths.duration.EndsWith("duration_seconds"), "duration_seconds path must be detected");
Assert(JsonPathTools.Set(requestJson, paths.duration, 15), "duration path must be writable");
Assert(requestJson["ability_parameter"]?["duration_seconds"]?.GetValue<int>() == 15, "duration must become 15 exactly");

var nonVideo = JsonNode.Parse("""{"duration":10,"prompt":"hello"}""")!;
Assert(string.IsNullOrWhiteSpace(JsonPathTools.DiscoverPaths(nonVideo).duration), "non-video duration must not be patched");

var lifecycle = new AI.VideoHub.V3.Models.DolaProtocolState();
var accepted = JsonNode.Parse("""{"task_id":"task-15","status":"accepted","duration_seconds":15,"remaining_count":4,"quota_status":"available"}""")!.AsObject();
DolaLifecycleInspector.ApplyObject(accepted, lifecycle, "$.data", "", "");
Assert(lifecycle.LastTaskId == "task-15", "accepted lifecycle task id must be captured");
Assert(lifecycle.LastTaskStatus == "accepted" && lifecycle.HasGeneratingTask, "accepted task must be generating");
Assert(lifecycle.LastTaskDurationSeconds == 15, "accepted task must retain server duration 15");
Assert(lifecycle.RemainingVideoCount == 4 && lifecycle.VideoQuotaStatus == "available", "quota fields must be captured");
var completed = JsonNode.Parse("""{"task_id":"task-15","status":"completed","duration_seconds":15,"vid":"vid-finished"}""")!.AsObject();
DolaLifecycleInspector.ApplyObject(completed, lifecycle, "$.data", lifecycle.LastTaskId, lifecycle.LastKnownVid);
Assert(lifecycle.LastTaskStatus == "completed" && !lifecycle.HasGeneratingTask, "completed task must stop generating state");
Assert(lifecycle.LastKnownVid == "vid-finished", "completed lifecycle must capture VID");
var fakeProbe15 = new AI.VideoHub.V3.Models.VideoVerificationResult { Success = true, DurationSeconds = 15.02, FileSize = 100000, Message = "ok" };
Assert(VideoP0Verdict.Evaluate(lifecycle, 15, fakeProbe15).Passed, "completed task + server duration 15 + VID + 15s media must certify");
var wrongServerDuration = new AI.VideoHub.V3.Models.DolaProtocolState { LastTaskId = "x", LastTaskStatus = "completed", LastTaskDurationSeconds = 10, LastKnownVid = "v" };
Assert(!VideoP0Verdict.Evaluate(wrongServerDuration, 15, fakeProbe15).Passed, "server duration 10 must never certify as 15");
var staleMissingTask = new AI.VideoHub.V3.Models.DolaProtocolState { LastTaskStatus = "completed", LastTaskDurationSeconds = 15, LastKnownVid = "v" };
Assert(!VideoP0Verdict.Evaluate(staleMissingTask, 15, fakeProbe15).Passed, "missing task id must never certify");

if (args.Contains("--video-test"))
{
    var tools = Path.Combine(AppContext.BaseDirectory, "Tools");
    var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
    var ffprobe = Path.Combine(tools, "ffprobe.exe");
    Assert(File.Exists(ffmpeg) && File.Exists(ffprobe), "Windows ffmpeg/ffprobe must be present for video test");
    var work = Path.Combine(Path.GetTempPath(), "ai-video-hub-v3-p0-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    var input = Path.Combine(work, "input.mp4");
    var output = Path.Combine(work, "output.mp4");
    Run(ffmpeg, $"-y -f lavfi -i testsrc2=size=720x1280:rate=30 -f lavfi -i sine=frequency=880:sample_rate=44100 -t 3 -vf \"drawbox=x=1:y=1:w=199:h=79:color=white@0.85:t=fill\" -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{input}\"");
    var originalProbe = await new MediaProbeService().VerifyVideoAsync(input, 3);
    Assert(originalProbe.Success, "synthetic input must probe as 3 seconds");

    // Deliberately request a border-touching area; service must clamp to FFmpeg-safe bounds.
    await new FfmpegWatermarkService().RemoveAuthorizedWatermarkRegionAsync(input, output, 0, 0, 200, 80);
    var outputProbe = await new MediaProbeService().VerifyVideoAsync(output, 3);
    Assert(outputProbe.Success, "processed output must remain ~3 seconds");
    var audio = Capture(ffprobe, $"-v error -select_streams a:0 -show_entries stream=codec_type -of default=nw=1:nk=1 \"{output}\"").Trim();
    Assert(audio.Contains("audio", StringComparison.OrdinalIgnoreCase), "processed output must preserve audio stream");
    Console.WriteLine($"Video P0 test PASS: {outputProbe.DurationSeconds:F2}s, audio preserved");
}

Console.WriteLine("P0 self-test PASS");
return;

static void Run(string exe, string arguments)
{
    var p = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true })!;
    var err = p.StandardError.ReadToEnd();
    var output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"Process failed ({p.ExitCode}): {exe}\n{output}\n{err}");
}

static string Capture(string exe, string arguments)
{
    var p = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true })!;
    var output = p.StandardOutput.ReadToEnd();
    var err = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"Process failed ({p.ExitCode}): {err}");
    return output;
}
