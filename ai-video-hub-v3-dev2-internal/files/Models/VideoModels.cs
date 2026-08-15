using System.Text.Json.Nodes;

namespace AI.VideoHub.V3.Models;

public sealed class VideoGenerationRequest
{
    public string Prompt { get; set; } = "";
    public string AspectRatio { get; set; } = "16:9";
    public int DurationSeconds { get; set; } = 10;
}

public sealed class ObservedRequestTemplate
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public string DurationPath { get; set; } = "";
    public string PromptPath { get; set; } = "";
    public string RatioPath { get; set; } = "";
    public bool IsVideoRequest { get; set; }
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DolaProtocolState
{
    public bool ServerAdvertised15 { get; set; }
    public string Capability15Evidence { get; set; } = "";
    public ObservedRequestTemplate? LastVideoRequest { get; set; }
    public string LastKnownVid { get; set; } = "";
    public string LastTaskId { get; set; } = "";
    public string LastTaskStatus { get; set; } = "";
    public int? LastTaskDurationSeconds { get; set; }
    public DateTime? LastTaskAcceptedAtUtc { get; set; }
    public bool HasGeneratingTask { get; set; }
    public int? RemainingVideoCount { get; set; }
    public string VideoQuotaStatus { get; set; } = "";
    public DateTime? VideoCooldownUntilUtc { get; set; }
    public string LastLifecycleEvidence { get; set; } = "";
    public string LastGetPlayInfoUrl { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MediaResource
{
    public string Url { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Vid { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool ExplicitOriginal { get; set; }
    public string Evidence { get; set; } = "";
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SubmissionResult
{
    public bool Success { get; set; }
    public int? HttpStatus { get; set; }
    public string BodyPreview { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string TaskStatus { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class VideoVerificationResult
{
    public bool Success { get; set; }
    public double? DurationSeconds { get; set; }
    public long FileSize { get; set; }
    public string Message { get; set; } = "";
}
