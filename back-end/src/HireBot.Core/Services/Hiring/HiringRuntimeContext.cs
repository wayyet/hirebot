using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring;

internal sealed record HiringRuntimeContext
{
    // ── 身份与路由 ────────────────────────────────────────────────────────────
    public required string HireId { get; init; }
    public required string TemplateId { get; init; }
    public required string TemplateName { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public required string SandboxId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string? EmployeeId { get; init; }

    // ── 阶段状态 ──────────────────────────────────────────────────────────────
    public string CurrentStage { get; init; } = string.Empty;
    public string CollectionPhase { get; init; } = string.Empty;

    // ── 并发控制 ──────────────────────────────────────────────────────────────
    public bool IsConversationPaused { get; init; }
    public bool IsConversationResponding { get; init; }

    // ── 运行时加载的模板包（会话期间保持不变；ReferenceTemplatePackage 体积大且仅初始化时使用，不持久化）
    public required TemplatePackageDefinition RoleTemplatePackage { get; init; }
    public required TemplatePackageDefinition WorkingTemplatePackage { get; init; }
    public required DiscoverySkillDefinition DiscoverySkill { get; init; }

    // ── 工作流数据 ────────────────────────────────────────────────────────────
    public IReadOnlyDictionary<string, string?> StructuredData { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    /// <summary>会话期间累积的物料，供产物包构建时写入 materials.json。</summary>
    public IReadOnlyList<HiringConversationMaterialDto> Materials { get; init; } = [];
    public IReadOnlyList<HiringStageCompletionDto> StageCompletion { get; init; } = [];
    public IReadOnlyList<HiringWorkflowHandoffDto> HandoffItems { get; init; } = [];
    public IReadOnlyList<HiringDispatchRecordDto> LatestDispatches { get; init; } = [];
    public IReadOnlyList<HiringCredentialSlotDto> CredentialSlots { get; init; } = [];
    public HiringConfigGovernanceStateDto? ConfigGovernance { get; init; }
    public IReadOnlyList<HiringStageReadinessDto> StageReadiness { get; init; } = [];

    // ── 模板包上传重试状态 ────────────────────────────────────────────────────
    public bool IsTemplateUploadPending { get; init; }
    public int TemplateUploadRetryCount { get; init; }
    public string? TemplateUploadLastError { get; init; }
    public DateTimeOffset? TemplateUploadLastAttemptAt { get; init; }
}
