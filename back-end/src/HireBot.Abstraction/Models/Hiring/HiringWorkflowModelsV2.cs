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
    /// <summary>需要用户在前端上传文件材料（PDF/DOCX/XLSX/MD等）的 handoff 工单，前端面板会显示上传按钮。</summary>
    public const string FileRequest = "file_request";
    /// <summary>需要用户在前端上传技能包（.zip）的 handoff 工单，前端面板会显示技能包上传表单。</summary>
    public const string SkillUpload = "skill_upload";
    /// <summary>需要用户在前端填写外部系统接入配置（API URL / 密钥 / 服务名等）的 handoff 工单。</summary>
    public const string ExternalConfig = "external_config";
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
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("workflow_id")] string WorkflowId,
    [property: JsonPropertyName("handoff_id")] string HandoffId,
    string Title,
    string Kind,
    string Stage,
    [property: JsonPropertyName("target_skill")] string TargetSkill,
    string? Intent,
    string? Category,
    JsonElement Payload,
    string? Source,
    string? Acceptance,
    string Status,
    string Fingerprint,
    [property: JsonPropertyName("related_todos")] IReadOnlyList<string> RelatedHandoffIds,
    [property: JsonPropertyName("related_files")] IReadOnlyList<string> RelatedFiles,
    int Revision,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("dispatch_id")] string? DispatchId,
    [property: JsonPropertyName("callback_summary")] string? CallbackSummary);

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
