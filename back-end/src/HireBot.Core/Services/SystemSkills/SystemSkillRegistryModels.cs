namespace HireBot.Core.Services.SystemSkills;

internal interface ISystemSkillRegistry
{
    Task<IReadOnlyList<SystemSkillPackage>> ListAsync(CancellationToken cancellationToken = default);
    Task<SystemSkillPackage?> FindAsync(string skillId, CancellationToken cancellationToken = default);
    Task<SystemSkillPackage> LoadRequiredAsync(
        string skillId,
        string? configuredPath = null,
        CancellationToken cancellationToken = default);
}

internal sealed record SystemSkillPackage(
    string SkillId,
    string DisplayName,
    string Description,
    string Level,
    string Status,
    string Version,
    string EntrySkill,
    string SkillHash,
    string RootPath,
    string EntryContent,
    string InputExample,
    string OutputExample,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> BoundTemplates,
    IReadOnlyList<SystemSkillStageRule> StageRules,
    IReadOnlyList<SystemSkillFileAsset> Files);

internal sealed record SystemSkillStageRule(
    string Stage,
    string SkillName,
    string Description,
    IReadOnlyList<string> RequiredFields);

internal sealed record SystemSkillFileAsset(
    string RelativePath,
    string Content,
    string ContentHash);
