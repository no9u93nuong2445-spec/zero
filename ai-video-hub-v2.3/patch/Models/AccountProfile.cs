namespace AI.VideoHub.Models;

public enum PlatformKind { Doubao, Qianwen, Dola }

public sealed class AccountProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新账号";
    public PlatformKind Platform { get; set; } = PlatformKind.Doubao;
    public string HomeUrl { get; set; } = "https://www.doubao.com/chat/";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string StatusText { get; set; } = "未打开";
    public int? LastRemainingQuota { get; set; }
    public string LastQuotaStatus { get; set; } = "未知";
    public DateTime? LastCooldownUntilUtc { get; set; }
    public string LastCapabilityEvidence { get; set; } = "";
    public bool LastCapability15Advertised { get; set; }
    public DolaRuntimeProtocolSnapshot? RuntimeProtocolSnapshot { get; set; }
    public string DolaHealthStatus { get; set; } = "未知";
    public string DolaHealthMessage { get; set; } = "";
    public bool DolaSignedBridgeReady { get; set; }
    public string DolaVideoQuotaStatus { get; set; } = "未知";
    public int? DolaVideoRemainingCount { get; set; }
    public bool DolaVideoHasGeneratingTask { get; set; }
    public DateTime? DolaVideoCooldownUntilUtc { get; set; }
    public string DolaActiveLeaseJobId { get; set; } = "";
    public override string ToString() => Name;
}
