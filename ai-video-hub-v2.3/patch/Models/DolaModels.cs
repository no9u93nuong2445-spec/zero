namespace AI.VideoHub.Models;

public sealed class DolaRuntimeProtocolSnapshot
{
    public string CompletionPath { get; set; } = "";
    public string CompletionMethod { get; set; } = "POST";
    public string ContentType { get; set; } = "";
    public string CompletionTemplateJson { get; set; } = "";
    public string AbilityTemplateJson { get; set; } = "";
    public string AbilityParameterTemplateJson { get; set; } = "";
    public bool HasVideoAbility { get; set; }
    public bool HasDurationField { get; set; }
    public string VideoModel { get; set; } = "";
    public string DurationPath { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTime LearnedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record DolaRequestInspection(
    ProtocolPatchResult Patch,
    DolaRuntimeProtocolSnapshot? Snapshot);
