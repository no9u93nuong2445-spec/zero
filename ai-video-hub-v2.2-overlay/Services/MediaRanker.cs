using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public static class MediaRanker
{
    public static MediaResource? ChooseBest(IEnumerable<MediaResource> media, string? taskId = null)
    {
        var source = media.Where(IsUsableVideo)
            .Where(x => string.IsNullOrWhiteSpace(taskId) || x.TaskId == taskId).ToList();
        if (source.Count == 0 && !string.IsNullOrWhiteSpace(taskId)) source = media.Where(IsUsableVideo).ToList();
        return source.OrderByDescending(EffectiveScore).ThenByDescending(x => x.Time).FirstOrDefault();
    }

    public static MediaResource? ChooseBestOriginal(IEnumerable<MediaResource> media, string? taskId = null)
    {
        var source = media.Where(IsUsableVideo).Where(x => x.IsPreferredOriginal)
            .Where(x => string.IsNullOrWhiteSpace(taskId) || x.TaskId == taskId).ToList();
        if (source.Count == 0 && !string.IsNullOrWhiteSpace(taskId))
            source = media.Where(IsUsableVideo).Where(x => x.IsPreferredOriginal).ToList();
        return source.OrderByDescending(EffectiveScore).ThenByDescending(x => x.Time).FirstOrDefault();
    }

    public static bool IsUsableVideo(MediaResource media)
    {
        if (!media.IsVerifiedVideo) return false;
        if (!Uri.TryCreate(media.Url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".heic") || path.EndsWith(".heif") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
            path.EndsWith(".png") || path.EndsWith(".webp") || path.EndsWith(".gif") || path.EndsWith(".avif")) return false;
        return true;
    }

    public static int EffectiveScore(MediaResource media)
    {
        if (!IsUsableVideo(media)) return int.MinValue / 2;
        var score = media.Score;
        var key = (media.SourceKey + " " + media.ProtocolPath).ToLowerInvariant();
        if (key.Contains("no_watermark") || key.Contains("nowatermark")) score += 180;
        else if (key.Contains("original") || key.Contains("origin_url")) score += 160;
        else if (key.Contains("download")) score += 80;
        else if (key.Contains("main")) score += 55;
        else if (key.Contains("backup")) score += 35;
        else if (key.Contains("video_url")) score += 25;
        else if (key.Contains("play")) score += 15;
        if (media.IsPreferredOriginal) score += 120;
        if (media.IsVerifiedVideo) score += 30;
        if (media.Width is int w && media.Height is int h)
        {
            var pixels = (long)w * h;
            if (pixels >= 1920L * 1080) score += 25;
            else if (pixels >= 1280L * 720) score += 15;
        }
        if (media.Url.Contains(".mp4", StringComparison.OrdinalIgnoreCase)) score += 12;
        return score;
    }

    public static void MarkBest(IEnumerable<MediaResource> media)
    {
        foreach (var item in media) item.IsBestCandidate = false;
        foreach (var group in media.Where(IsUsableVideo).GroupBy(x => string.IsNullOrWhiteSpace(x.TaskId) ? "__session__" : x.TaskId))
        {
            var best = group.OrderByDescending(EffectiveScore).ThenByDescending(x => x.Time).FirstOrDefault();
            if (best is not null) best.IsBestCandidate = true;
        }
    }
}
