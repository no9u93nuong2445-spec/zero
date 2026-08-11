using System.Net.Http;
using Microsoft.Web.WebView2.Core;

namespace AI.VideoHub.Services;

public sealed class DownloadService
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    }) { Timeout = TimeSpan.FromMinutes(15) };

    public async Task<string> DownloadOwnMediaAsync(CoreWebView2 core, string url, string downloadDirectory, string? suggestedName = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("无效的视频地址。");

        Directory.CreateDirectory(downloadDirectory);
        var cookies = await core.CookieManager.GetCookiesAsync(uri.GetLeftPart(UriPartial.Authority));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        if (cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var ext = ChooseExtension(uri, response.Content.Headers.ContentType?.MediaType);
        var fileName = SanitizeFileName(suggestedName ?? $"video_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        if (!Path.HasExtension(fileName)) fileName += ext;
        var path = UniquePath(Path.Combine(downloadDirectory, fileName));
        var partial = path + ".part";
        var total = response.Content.Headers.ContentLength;

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true);
            var buffer = new byte[256 * 1024];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0) progress?.Report(done * 100d / total.Value);
            }
            await output.FlushAsync(ct);
            if (done == 0) throw new IOException("下载结果为空文件。");
            File.Move(partial, path, true);
            progress?.Report(100);
            return path;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    private static string ChooseExtension(Uri uri, string? mediaType)
    {
        var ext = Path.GetExtension(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 8) return ext;
        return mediaType?.ToLowerInvariant() switch
        {
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            _ => ".mp4"
        };
    }

    private static string UniquePath(string p)
    {
        if (!File.Exists(p)) return p;
        var d = Path.GetDirectoryName(p)!; var n = Path.GetFileNameWithoutExtension(p); var e = Path.GetExtension(p);
        for (var i = 2; ; i++) { var c = Path.Combine(d, $"{n}_{i}{e}"); if (!File.Exists(c)) return c; }
    }

    private static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();
        return string.IsNullOrWhiteSpace(s) ? $"video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4" : s;
    }
}
