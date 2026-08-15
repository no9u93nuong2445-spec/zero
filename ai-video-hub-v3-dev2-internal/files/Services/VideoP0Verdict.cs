using AI.VideoHub.V3.Models;

namespace AI.VideoHub.V3.Services;

public sealed record VideoP0VerdictResult(bool Passed, string Message);

public static class VideoP0Verdict
{
    private static readonly HashSet<string> Completed = new(StringComparer.OrdinalIgnoreCase)
    {
        "success", "succeeded", "completed", "done"
    };

    public static VideoP0VerdictResult Evaluate(
        DolaProtocolState state,
        int expectedDuration,
        VideoVerificationResult probe,
        string expectedTaskId,
        MediaResource media)
    {
        if (string.IsNullOrWhiteSpace(expectedTaskId))
            return Fail("本次提交响应没有冻结 task_id，不能确认后续状态属于这一次提交。");
        if (string.IsNullOrWhiteSpace(state.LastTaskId))
            return Fail("缺少当前 task_id，不能确认这是本次任务。");
        if (!string.Equals(state.LastTaskId, expectedTaskId, StringComparison.Ordinal))
            return Fail($"任务身份已变化：本次提交={expectedTaskId}，当前观察={state.LastTaskId}。拒绝把其他/旧任务结果算成本次成功。");
        if (!Completed.Contains(state.LastTaskStatus ?? ""))
            return Fail($"任务尚未完成：{state.LastTaskStatus}");
        if (state.LastTaskDurationSeconds != expectedDuration)
            return Fail($"服务端任务时长={state.LastTaskDurationSeconds?.ToString() ?? "未知"}，请求={expectedDuration}。");
        if (string.IsNullOrWhiteSpace(state.LastKnownVid))
            return Fail("任务完成但缺少 VID，不能绑定最终媒体。");
        if (!media.ExplicitOriginal)
            return Fail("最终媒体没有明确 original/no_watermark 证据。");
        if (string.IsNullOrWhiteSpace(media.Vid) || !string.Equals(media.Vid, state.LastKnownVid, StringComparison.Ordinal))
            return Fail($"下载媒体 VID={media.Vid} 与本次任务 VID={state.LastKnownVid} 不一致。");
        if (!probe.Success || probe.DurationSeconds is null)
            return Fail("最终媒体时长验证未通过：" + probe.Message);
        if (Math.Abs(probe.DurationSeconds.Value - expectedDuration) > 1.25)
            return Fail($"最终媒体实际 {probe.DurationSeconds.Value:F2}s，不符合 {expectedDuration}s。");
        return new(true, $"任务 {expectedTaskId} 已完成；服务端时长={expectedDuration}s；VID={state.LastKnownVid}；原片VID匹配；实际成片={probe.DurationSeconds.Value:F2}s。P0 PASS");
    }

    private static VideoP0VerdictResult Fail(string message) => new(false, "P0 未认证：" + message);
}
