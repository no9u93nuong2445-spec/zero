using AI.VideoHub.V3.Models;
using Microsoft.Web.WebView2.Core;
using System.Net;
using System.Net.Http;

namespace AI.VideoHub.V3.Services;

public sealed class DownloadService
{
    public async Task<string> DownloadAsync(CoreWebView2 core, MediaResource media, string outputDirectory, CancellationToken ct = default)
    {
        if (!media.ExplicitOriginal) throw new InvalidOperationException("原片按钮只接受明确 original/no_watermark 证据的资源；普通播放地址不会冒充原片。" );
        Directory.CreateDirectory(outputDirectory);
        var uri = new Uri(media.Url);
        using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, UseCookies = false };
        var cookies = await core.CookieManager.GetCookiesAsync(uri.GetLeftPart(UriPartial.Authority));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        if (cookies.Count > 0)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        return await DownloadWithClient(client, media, outputDirectory, ct);
    }

    internal static async Task<string> DownloadWithClient(HttpClient client, MediaResource media, string outputDirectory, CancellationToken ct)
    {
        var ext = GuessExtension(media.Url);
        var name = $"dola_{(string.IsNullOrWhiteSpace(media.Vid) ? "video" : media.Vid)}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        var final = Path.Combine(outputDirectory, name);
        var temp = final + ".part";
        using var response = await client.GetAsync(media.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"资源返回 {mediaType}，不是视频文件。" );
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(temp)) await input.CopyToAsync(output, ct);
        var size = new FileInfo(temp).Length;
        if (size < 32 * 1024) { File.Delete(temp); throw new InvalidDataException($"下载文件异常小：{size} bytes"); }
        if (!LooksLikeMediaFile(temp)) { File.Delete(temp); throw new InvalidDataException("下载结果缺少常见媒体文件头，拒绝当作原片保存。" ); }
        File.Move(temp, final, true);
        DiagnosticLog.Write($"Explicit-original download saved: {final} ({size} bytes); evidence={media.Evidence}");
        return final;
    }

    private static bool LooksLikeMediaFile(string file)
    {
        Span<byte> head = stackalloc byte[16];
        using var s = File.OpenRead(file);
        var n = s.Read(head);
        if (n < 4) return false;
        if (n >= 8 && head[4] == (byte)'f' && head[5] == (byte)'t' && head[6] == (byte)'y' && head[7] == (byte)'p') return true;
        if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return true;
        return false;
    }

    private static string GuessExtension(string url)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            if (ext is ".mp4" or ".mov" or ".webm" or ".m4v") return ext;
        }
        catch { }
        return ".mp4";
    }
}
