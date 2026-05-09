using System.Text.Json;
using System.Text.Json.Serialization;

namespace HireBot.Abstraction.Models.Hiring;

public static class HiringHandoffStatus
{
    public const string Drafting = "drafting";
    public const string ReadyToDispatch = "ready_to_dispatch";
    public const string Dispatched = "dispatched";
    public const string Dirty = "dirty";
    public const string Confirmed = "confirmed";
    public const string NeedsReview = "needs_review";
    public const string Dismissed = "dismissed";
}

public static class HiringHandoffKind
{
    public const string HandoffTodo = "handoff_todo";
}

public static class HiringDiagnosticPriority
{
    public const string Required = "required";
    public const string Recommended = "recommended";
    public const string Optional = "optional";
}

public static class HiringDiagnosticStatus
{
    public const string Pass = "pass";
    public const string Warning = "warning";
    public const string Blocked = "blocked";
}

public static class HiringStageReadinessStatus
{
    public const string Missing = "missing";
    public const string Partial = "partial";
    public const string Complete = "complete";
    public const string Skipped = "skipped";
}

public static class HiringCredentialBindingStatus
{
    public const string Pending = "pending";
    public const string Bound = "bound";
    public const string NotRequired = "not_required";
    public const string Failed = "failed";
}

public static class HiringConfigFileKeys
{
    public const string Soul = "soul";
    public const string Identity = "identity";
    public const string Agents = "agents";
}

public sealed record HiringWorkflowHandoffDto(
    string SessionId,
    string WorkflowId,
    string HandoffId,
    string Title,
    string Kind,
    string Stage,
    string TargetSkill,
    string? Intent,
    string? Category,
    JsonElement Payload,
    string? Source,
    string? Acceptance,
    string Status,
    string Fingerprint,
    IReadOnlyList<string> RelatedHandoffIds,
    IReadOnlyList<string> RelatedFiles,
    int Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? DispatchId,
    string? CallbackSummary);

public sealed record HiringDispatchArtifactDto(
    string Path,
    string Kind,
    string Encoding,
    string Sha256);

public sealed record HiringDispatchHandoffResultDto(
    string HandoffId,
    string Status,
    IReadOnlyList<HiringDispatchArtifactDto> Artifacts,
    IReadOnlyList<string> Errors);

public sealed record HiringDispatchRecordDto(
    string DispatchId,
    string Target,
    string Status,
    IReadOnlyList<string> HandoffIds,
    string? To,
    string? Note,
    string? UserSummary,
    IReadOnlyList<HiringDispatchArtifactDto> Artifacts,
    IReadOnlyList<HiringDispatchHandoffResultDto> TodoResults,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<string> Errors);

public sealed record HiringStageReadinessDto(
    string Stage,
    string Status,
    string Reason,
    IReadOnlyList<string> BlockingHandoffIds);

public sealed record HiringDiagnosticTodoDto(
    string Id,
    string Stage,
    string Level,
    string Category,
    string Question,
    string Evidence,
    string SuggestedAction,
    [property: JsonPropertyName("related_handoff_ids")]
    IReadOnlyList<string> RelatedHandoffIds);

public sealed record HiringDiagnosticReportDto(
    string Status,
    string Confidence,
    string CurrentStage,
    bool ReadyForPackaging,
    IReadOnlyList<HiringStageReadinessDto> StageReadiness,
    IReadOnlyList<HiringDiagnosticTodoDto> DiagnosticTodos,
    IReadOnlyList<string> HandoffCorrelation,
    IReadOnlyList<string> OpenQuestions,
    string UserSummary,
    DateTimeOffset GeneratedAtUtc);

public sealed record HiringCredentialSlotDto(
    string CredentialSlot,
    string? SecretRef,
    string? AuthKind,
    string? TargetSystem,
    string? HandoffId,
    string BindingStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record HiringConfigGovernanceFileDto(
    string ConfigKey,
    string DisplayName,
    string RelativePath,
    string Content,
    string Summary,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> AffectedHandoffIds);

public sealed record HiringConfigGovernanceStateDto(
    IReadOnlyList<HiringConfigGovernanceFileDto> Files,
    IReadOnlyList<string> PendingReviewHandoffIds,
    DateTimeOffset? UpdatedAtUtc);
