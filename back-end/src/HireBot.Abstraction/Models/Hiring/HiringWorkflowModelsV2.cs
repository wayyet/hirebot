using System.Text.Json;
using System.Text.Json.Serialization;

namespace HireBot.Abstraction.Models.Hiring;

public static class HiringTodoStatus
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string NeedsReview = "needs_review";
    public const string Dismissed = "dismissed";
    public const string Resolved = "resolved";
}

public static class HiringTodoKind
{
    public const string Gap = "gap";
    public const string Diagnosis = "diagnosis";
}

public static class HiringTodoPriority
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

public sealed record HiringWorkflowTodoDto(
    string Id,
    string Title,
    string Stage,
    string Kind,
    string Status,
    string? GapType,
    string? Priority,
    string? CurrentState,
    string? ExpectedState,
    string? AcceptanceCriteria,
    string? AcceptanceEvidence,
    string Source,
    string? Fingerprint,
    string? Category,
    JsonElement? Payload,
    string? Level,
    string? Question,
    string? Evidence,
    string? SuggestedAction,
    IReadOnlyList<string> RelatedTodoIds,
    IReadOnlyList<string> RelatedFiles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record HiringDispatchArtifactDto(
    string Path,
    string Kind,
    string Encoding,
    string Sha256);

public sealed record HiringDispatchTodoResultDto(
    string TodoId,
    string Status,
    IReadOnlyList<HiringDispatchArtifactDto> Artifacts,
    IReadOnlyList<string> Errors);

public sealed record HiringDispatchRecordDto(
    string DispatchId,
    string Target,
    string Status,
    IReadOnlyList<string> TodoIds,
    string? Note,
    string? UserSummary,
    IReadOnlyList<HiringDispatchArtifactDto> Artifacts,
    IReadOnlyList<HiringDispatchTodoResultDto> TodoResults,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<string> Errors);

public sealed record HiringStageReadinessDto(
    string Stage,
    string Status,
    string Reason,
    IReadOnlyList<string> BlockingTodoIds);

public sealed record HiringDiagnosticTodoDto(
    string Id,
    string Stage,
    string Level,
    string Category,
    string Question,
    string Evidence,
    string SuggestedAction,
    [property: JsonPropertyName("related_todos")]
    IReadOnlyList<string> RelatedTodoIds);

public sealed record HiringDiagnosticReportDto(
    string Status,
    string Confidence,
    string CurrentStage,
    bool ReadyForPackaging,
    IReadOnlyList<HiringStageReadinessDto> StageReadiness,
    IReadOnlyList<HiringDiagnosticTodoDto> DiagnosticTodos,
    IReadOnlyList<string> TodoCorrelation,
    IReadOnlyList<string> OpenQuestions,
    string UserSummary,
    DateTimeOffset GeneratedAtUtc);

public sealed record HiringCredentialSlotDto(
    string CredentialSlot,
    string? SecretRef,
    string? AuthKind,
    string? TargetSystem,
    string? TodoId,
    string BindingStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record HiringConfigGovernanceFileDto(
    string ConfigKey,
    string DisplayName,
    string RelativePath,
    string Content,
    string Summary,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> AffectedTodoIds);

public sealed record HiringConfigGovernanceStateDto(
    IReadOnlyList<HiringConfigGovernanceFileDto> Files,
    IReadOnlyList<string> PendingReviewTodoIds,
    DateTimeOffset? UpdatedAtUtc);
