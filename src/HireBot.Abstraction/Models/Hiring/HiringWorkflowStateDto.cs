namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringWorkflowStateDto(
    string HireId,
    string SessionId,
    string CurrentStage,
    bool RequiresAudit,
    string CollectionPhase,
    IReadOnlyList<StageSkillMappingDto> StageSkills,
    IReadOnlyList<HiringAuditLogDto> AuditLogs,
    string? TemplatePackageId = null,
    string? TemplatePackageVersion = null,
    string? DiscoverySkillId = null,
    string? DiscoverySkillVersion = null,
    IReadOnlyList<HiringStageCompletionDto>? StageCompletion = null);
