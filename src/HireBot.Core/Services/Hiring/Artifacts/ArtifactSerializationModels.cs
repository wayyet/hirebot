using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring.Artifacts;

internal sealed record ArtifactSerializationRequest(
    string HireId,
    string TemplateId,
    string TemplateName,
    string? EmployeeId,
    string OwnerSubject,
    string TenantId,
    string OperatorId,
    string SandboxId,
    string SessionId,
    string CurrentStage,
    string CollectionPhase,
    TemplatePackageDefinition TemplatePackage,
    DiscoverySkillDefinition DiscoverySkill,
    IReadOnlyDictionary<string, string?> StructuredData,
    IReadOnlyList<HiringConversationMaterialDto> Materials,
    IReadOnlyList<HiringStageCompletionDto> StageCompletion,
    DateTimeOffset GeneratedAtUtc);

internal sealed record ArtifactSerializationResult(
    IReadOnlyDictionary<string, byte[]> Files,
    byte[] Archive,
    string ArchiveFileName);
