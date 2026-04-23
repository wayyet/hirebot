namespace HireBot.Core.Services.Hiring.Discovery;

internal sealed record DiscoverySkillDefinition(
    string SkillId,
    string SkillVersion,
    string SkillHash,
    string SkillRootPath,
    string SkillContent,
    IReadOnlyList<DiscoverySkillFileAsset> Files,
    IReadOnlyList<DiscoveryStageRule> StageRules);

internal sealed record DiscoverySkillFileAsset(
    string RelativePath,
    string Content,
    string ContentHash);

internal sealed record DiscoveryStageRule(
    string Stage,
    string SkillName,
    string Description,
    IReadOnlyList<string> RequiredFields);
