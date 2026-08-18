using System.Collections.ObjectModel;

namespace AI.VideoHub.Models;

public enum Capability15State { Unknown, Advertised, Enabled, ObservedRequest, Rejected }
public enum VideoTaskStatus { Observed, Submitted, Processing, Completed, Failed, Unknown }

public sealed class CapabilityInfo
{
    public Capability15State State { get; set; } = Capability15State.Unknown;
    public string Message { get; set; } = "尚未发现服务端15秒能力";
    public string Evidence { get; set; } = "";
    public DateTime? LastUpdatedUtc { get; set; }
}

public sealed record CaptureContext(string SessionId, string AccountId, string AccountName, string Platform);

public sealed class RequestObservation
{
    public string SessionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Platform { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Source { get; set; } = "PageJS";
    public string BodyKind { get; set; } = "";
    public string DurationPath { get; set; } = "";
    public int? Duration { get; set; }
    public bool WasPatched { get; set; }
    public int? ResponseStatus { get; set; }
}

public sealed class MediaResource
{
    public string SessionId { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "video";
    public string SourceKey { get; set; } = "";
    public string Url { get; set; } = "";
    public string SafeDisplay { get; set; } = "";
    public bool IsVerifiedVideo { get; set; }
    public string Verification { get; set; } = "";
    public string ProtocolPath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public bool IsPreferredOriginal { get; set; }
    public bool IsBestCandidate { get; set; }
    public int Score { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? TaskId { get; set; }
    public string? LocalPath { get; set; }
}

public sealed class AccountTelemetry
{
    public string SessionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Platform { get; set; } = "";
    public int? RemainingCount { get; set; }
    public string QuotaStatus { get; set; } = "未知";
    public string CooldownText { get; set; } = "";
    public bool? HasGeneratingTask { get; set; }
    public string PageState { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class VideoTaskRecord
{
    public string SessionId { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Platform { get; set; } = "Doubao";
    public string? RemoteTaskId { get; set; }
    public string? PromptPreview { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Model { get; set; }
    public string? Ratio { get; set; }
    public VideoTaskStatus Status { get; set; } = VideoTaskStatus.Observed;
    public string StatusMessage { get; set; } = "已观察";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? MediaUrl { get; set; }
    public string? LocalPath { get; set; }
    public bool RestoredFromDisk { get; set; }
    public string? LastError { get; set; }
}

public sealed class SessionViewModel
{
    public ObservableCollection<RequestObservation> Requests { get; } = new();
    public ObservableCollection<MediaResource> Media { get; } = new();
    public ObservableCollection<VideoTaskRecord> Tasks { get; } = new();
    public CapabilityInfo Capability { get; } = new();
    public AccountTelemetry Telemetry { get; } = new();
}
