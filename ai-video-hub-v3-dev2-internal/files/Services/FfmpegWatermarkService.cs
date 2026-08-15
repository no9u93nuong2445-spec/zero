using System.Diagnostics;

namespace AI.VideoHub.V3.Services;

public sealed class FfmpegWatermarkService
{
    public async Task<string> RemoveAuthorizedWatermarkRegionAsync(string input, string output, int x, int y, int width, int height, CancellationToken ct = default)
    {
        var ffmpeg = MediaProbeService.FindTool("ffmpeg.exe") ?? throw new FileNotFoundException("缺少 Tools/ffmpeg.exe。该功能仅用于本人创作或已获授权素材的本地处理。" );
        if (!File.Exists(input)) throw new FileNotFoundException("输入视频不存在。", input);
        if (width <= 0 || height <= 0 || x < 0 || y < 0) throw new ArgumentOutOfRangeException(nameof(width), "水印区域参数无效。" );

        var (videoWidth, videoHeight) = await new MediaProbeService().GetDimensionsAsync(input);
        var safeX = Math.Clamp(x, 1, Math.Max(1, videoWidth - 2));
        var safeY = Math.Clamp(y, 1, Math.Max(1, videoHeight - 2));
        var safeW = Math.Min(width, videoWidth - safeX - 1);
        var safeH = Math.Min(height, videoHeight - safeY - 1);
        if (safeW < 2 || safeH < 2)
            throw new ArgumentOutOfRangeException(nameof(width), $"水印区域超出视频范围。视频={videoWidth}x{videoHeight}，请求区域=({x},{y},{width},{height})" );

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var args = $"-y -i \"{input}\" -vf \"delogo=x={safeX}:y={safeY}:w={safeW}:h={safeH}:show=0\" -c:v libx264 -crf 18 -preset medium -c:a copy \"{output}\"";
        var psi = new ProcessStartInfo(ffmpeg, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        if (p.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException("FFmpeg 本地处理失败：" + stderr[^Math.Min(stderr.Length, 1500)..]);
        DiagnosticLog.Write($"Authorized local delogo completed: video={videoWidth}x{videoHeight}; requested=({x},{y},{width},{height}); applied=({safeX},{safeY},{safeW},{safeH})");
        return output;
    }
}
