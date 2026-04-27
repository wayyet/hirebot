using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring;

internal sealed record HiringRuntimeContext
{
    public required string HireId { get; init; }
    public required string TemplateId { get; init; }
    public required string TemplateName { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public required string SandboxId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string CurrentStage { get; init; } = string.Empty;
    public string CollectionPhase { get; init; } = string.Empty;
    public bool IsConversationPaused { get; init; }
    public bool IsConversationResponding { get; init; }
    public string? EmployeeId { get; init; }
    public required TemplatePackageDefinition TemplatePackage { get; init; }
    public required DiscoverySkillDefinition DiscoverySkill { get; init; }
    public IReadOnlyDictionary<string, string?> StructuredData { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<HiringConversationMaterialDto> Materials { get; init; } = [];
    public IReadOnlyList<HiringConversationMessageDto> Messages { get; init; } = [];
    public IReadOnlyList<HiringAuditLogDto> AuditLogs { get; init; } = [];
    public IReadOnlyList<HiringStageCompletionDto> StageCompletion { get; init; } = [];
    public bool IsTemplateUploadPending { get; init; }
    public int TemplateUploadRetryCount { get; init; }
    public string? TemplateUploadLastError { get; init; }
    public DateTimeOffset? TemplateUploadLastAttemptAt { get; init; }
    public IReadOnlyDictionary<string, byte[]> ArtifactFiles { get; init; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    public byte[]? ArtifactArchive { get; init; }
    public string? ArtifactArchiveFileName { get; init; }
}
