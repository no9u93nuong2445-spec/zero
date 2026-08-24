using System.Text.Json;
using AI.VideoHub.Models;

namespace AI.VideoHub.Services;

public sealed class CaptureMessageParser
{
    public event Action<ObservedRequest>? RequestObserved;
    public event Action<GeneratedTask>? TaskObserved;
    public event Action<MediaResource>? MediaObserved;
    public event Action<MediaResource>? OriginalMediaObserved;
    public event Action<AccountTelemetry>? TelemetryObserved;
    public event Action<Capability15Signal>? Capability15Observed;
    public event Action<ProtocolPatchSignal>? ProtocolPatchObserved;
    public event Action<DolaRuntimeProtocolSnapshot>? DolaProtocolLearned;
    public event Action<CaptureContext, string>? DolaVideoIdObserved;
    public event Action<CaptureContext, string, JsonElement>? DolaPlayInfoObserved;
    public event Action<CaptureContext, string>? StatusReceived;
    public event Action<CaptureContext>? ReadyReceived;

    public void Handle(CaptureContext context, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = ReadString(root, "type");
            switch (type)
            {
                case "ready": ReadyReceived?.Invoke(context); break;
                case "status": StatusReceived?.Invoke(context, ReadString(root, "message")); break;
                case "request": RequestObserved?.Invoke(new ObservedRequest { SessionId=context.SessionId, AccountId=context.AccountId, AccountName=context.AccountName, Method=ReadString(root,"method"), Url=ReadString(root,"url"), Body=ReadString(root,"body"), DurationSeconds=ReadInt(root,"duration"), TimeUtc=DateTime.UtcNow }); break;
                case "task": TaskObserved?.Invoke(ParseTask(context, root)); break;
                case "media": MediaObserved?.Invoke(ParseMedia(context, root)); break;
                case "telemetry": TelemetryObserved?.Invoke(new AccountTelemetry(context.SessionId, context.AccountId, context.AccountName, ReadInt(root,"remaining"), ReadString(root,"quotaStatus"), ReadInt(root,"cooldownSeconds"), ReadBool(root,"hasGenerating"), DateTime.UtcNow)); break;
                case "capability15": Capability15Observed?.Invoke(new Capability15Signal(context.SessionId, context.AccountId, context.AccountName, ReadString(root,"evidence"), DateTime.UtcNow)); break;
                case "protocolPatch": ProtocolPatchObserved?.Invoke(new ProtocolPatchSignal(context.SessionId, context.AccountId, context.AccountName, ReadString(root,"url"), ReadString(root,"method"), ReadInt(root,"before") ?? 0, ReadInt(root,"after") ?? 0, ReadString(root,"path"), ReadString(root,"bodyType"), ReadBool(root,"patched"), ReadString(root,"summary"), DateTime.UtcNow)); break;
                case "dolaVid": { var vid=ReadString(root,"vid"); if(!string.IsNullOrWhiteSpace(vid)) DolaVideoIdObserved?.Invoke(context, vid); break; }
                case "dolaPlayInfo": { var vid=ReadString(root,"vid"); if(root.TryGetProperty("payload",out var p)) DolaPlayInfoObserved?.Invoke(context, vid, p.Clone()); break; }
            }
        }
        catch (Exception ex) { StatusReceived?.Invoke(context, "捕获消息解析失败：" + ex.Message); }
    }

    public void PublishRequest(ObservedRequest request) => RequestObserved?.Invoke(request);
    public void PublishTask(GeneratedTask task) => TaskObserved?.Invoke(task);
    public void PublishMedia(MediaResource media) => MediaObserved?.Invoke(media);
    public void PublishOriginalMedia(MediaResource media) => OriginalMediaObserved?.Invoke(media);
    public void PublishTelemetry(AccountTelemetry telemetry) => TelemetryObserved?.Invoke(telemetry);
    public void PublishCapability15(Capability15Signal signal) => Capability15Observed?.Invoke(signal);
    public void PublishProtocolPatch(ProtocolPatchSignal signal) => ProtocolPatchObserved?.Invoke(signal);
    public void PublishDolaProtocol(DolaRuntimeProtocolSnapshot snapshot) => DolaProtocolLearned?.Invoke(snapshot);
    public void PublishDolaVid(CaptureContext context, string vid) => DolaVideoIdObserved?.Invoke(context, vid);
    public void PublishDolaPlayInfo(CaptureContext context, string vid, JsonElement payload) => DolaPlayInfoObserved?.Invoke(context, vid, payload.Clone());
    public void PublishStatus(CaptureContext context, string message) => StatusReceived?.Invoke(context, message);

    private static GeneratedTask ParseTask(CaptureContext c, JsonElement r) => new() { SessionId=c.SessionId, AccountId=c.AccountId, AccountName=c.AccountName, Platform=c.Platform, Prompt=ReadString(r,"prompt"), Model=ReadString(r,"model"), AspectRatio=ReadString(r,"ratio"), DurationSeconds=ReadInt(r,"duration"), RemoteTaskId=ReadString(r,"taskId"), Status=ReadString(r,"status"), StatusMessage=ReadString(r,"statusMessage"), MediaUrl=ReadString(r,"mediaUrl"), RequestedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
    private static MediaResource ParseMedia(CaptureContext c, JsonElement r) => new() { SessionId=c.SessionId, AccountId=c.AccountId, AccountName=c.AccountName, Kind=ReadString(r,"kind"), SourceKey=ReadString(r,"key"), Url=ReadString(r,"url"), TaskId=ReadString(r,"taskId"), Width=ReadInt(r,"width"), Height=ReadInt(r,"height"), IsVerifiedVideo=ReadBool(r,"verified"), IsPreferredOriginal=ReadBool(r,"preferredOriginal"), Verification=ReadString(r,"verification"), ProtocolPath=ReadString(r,"protocolPath"), SafeDisplay=ReadString(r,"safeDisplay") };
    private static string ReadString(JsonElement r,string n)=>r.TryGetProperty(n,out var p)?p.ValueKind==JsonValueKind.String?p.GetString()??"":p.ToString():"";
    private static int? ReadInt(JsonElement r,string n){if(!r.TryGetProperty(n,out var p))return null;if(p.ValueKind==JsonValueKind.Number&&p.TryGetInt32(out var i))return i;if(p.ValueKind==JsonValueKind.String&&int.TryParse(p.GetString(),out i))return i;return null;}
    private static bool ReadBool(JsonElement r,string n)=>r.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.True;
}
