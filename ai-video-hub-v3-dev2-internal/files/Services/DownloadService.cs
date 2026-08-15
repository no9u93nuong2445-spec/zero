using AI.VideoHub.V3.Models;
using Microsoft.Web.WebView2.Core;
using System.Net;
using System.Net.Http;

namespace AI.VideoHub.V3.Services;

public sealed class DownloadService
{
    public async Task<string> DownloadAsync(CoreWebView2 core, MediaResource media, string outputDirectory, CancellationToken ct = default)
    {
        if (!media.ExplicitOriginal)
            throw new InvalidOperationException("V3 原片按钮只接受响应中有明确 original/no_watermark 证据的资源；普通播放地址不会冒充原片。");
        if (!Uri.TryCreate(media.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("原片地址不是有效 HTTP/HTTPS URL。");

        Directory.CreateDirectory(outputDirectory);
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = false
        };

        var cookies = await core.CookieManager.GetCookiesAsync(media.Url);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        if (cookies.Count > 0)
        {
            var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "video/*,application/octet-stream;q=0.9,*/*;q=0.5");
        return await DownloadWithClient(client, media, outputDirectory, ct);
    }

    private static async Task<string> DownloadWithClient(HttpClient client, MediaResource media, string outputDirectory, CancellationToken ct)
    {
        var ext = GuessExtension(media.Url);
        var baseName = $"dola_{(string.IsNullOrWhiteSpace(media.Vid) ? "video" : media.Vid)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";
        foreach (var bad in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(bad, '_');
        var final = GetUniquePath(outputDirectory, baseName, ext);
        var temp = final + "." + Guid.NewGuid().ToString("N") + ".part";

        try
        {
            using var response = await client.GetAsync(media.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"下载地址返回的不是视频文件：Content-Type={mediaType}");

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, ct);

            var size = new FileInfo(temp).Length;
            if (size < 32 * 1024)
                throw new InvalidDataException($"下载文件异常小：{size} bytes");

            File.Move(temp, final, false);
            DiagnosticLog.Write($"Original-evidence download saved: {final} ({size} bytes); evidence={media.Evidence}");
            return final;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private static string GetUniquePath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, baseName + extension);
        if (!File.Exists(path)) return path;
        for (var i = 2; i < 10000; i++)
        {
            path = Path.Combine(directory, $"{baseName}_{i}{extension}");
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(directory, baseName + "_" + Guid.NewGuid().ToString("N")[..8] + extension);
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
