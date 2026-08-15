using System.Diagnostics;
using System.Globalization;
using AI.VideoHub.V3.Models;

namespace AI.VideoHub.V3.Services;

public sealed class MediaProbeService
{
    public async Task<VideoVerificationResult> VerifyVideoAsync(string filePath, int? expectedDuration = null)
    {
        if (!File.Exists(filePath)) return new() { Success = false, Message = "文件不存在。" };
        var size = new FileInfo(filePath).Length;
        if (size < 32 * 1024) return new() { Success = false, FileSize = size, Message = "文件过小，疑似下载失败。" };
        var ffprobe = FindTool("ffprobe.exe");
        if (ffprobe is null)
            return new() { Success = false, FileSize = size, Message = "缺少 Tools/ffprobe.exe，不能把视频或 15 秒功能标记为 PASS。" };

        var psi = new ProcessStartInfo(ffprobe, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0 || !double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            return new() { Success = false, FileSize = size, Message = "ffprobe 失败：" + stderr.Trim() };
        var pass = expectedDuration is null || Math.Abs(duration - expectedDuration.Value) <= 1.25;
        return new() { Success = pass, FileSize = size, DurationSeconds = duration, Message = pass ? $"实际时长 {duration:F2}s，验证通过。" : $"请求 {expectedDuration}s，但实际 {duration:F2}s，验证失败。" };
    }

    public async Task<(int Width, int Height)> GetDimensionsAsync(string filePath)
    {
        var ffprobe = FindTool("ffprobe.exe") ?? throw new FileNotFoundException("缺少 Tools/ffprobe.exe，无法验证水印区域。" );
        var psi = new ProcessStartInfo(ffprobe, $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{filePath}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var parts = stdout.Trim().Split('x');
        if (p.ExitCode != 0 || parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h) || w <= 0 || h <= 0)
            throw new InvalidOperationException("ffprobe 无法读取视频尺寸：" + stderr.Trim());
        return (w, h);
    }

    public static string? FindTool(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", name),
            Path.Combine(AppContext.BaseDirectory, name)
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
