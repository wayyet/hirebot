using System.Text.Json;
using System.Text.Json.Serialization;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxSessionDetailDto(
    string SessionId,
    IReadOnlyList<HiringConversationMessageDto> Messages,
    IReadOnlyList<SandboxSessionHandoffItemDto> HandoffItems,
    bool IsActive);

public sealed record SandboxSessionHandoffItemDto(
    [property: JsonPropertyName("session_id")]
    string SessionId,
    [property: JsonPropertyName("workflow_id")]
    string WorkflowId,
    [property: JsonPropertyName("handoff_id")]
    string HandoffId,
    string Title,
    string Kind,
    string Stage,
    [property: JsonPropertyName("target_skill")]
    string TargetSkill,
    string? Intent,
    string? Category,
    JsonElement Payload,
    string? Source,
    string? Acceptance,
    string Status,
    string Fingerprint,
    [property: JsonPropertyName("related_todos")]
    IReadOnlyList<string> RelatedHandoffIds,
    IReadOnlyList<string> RelatedFiles,
    int Revision,
    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at")]
    DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("dispatch_id")]
    string? DispatchId,
    [property: JsonPropertyName("callback_summary")]
    string? CallbackSummary);
