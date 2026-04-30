using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.SystemSkills;

namespace HireBot.Core.Services.Hiring.Discovery;

internal sealed class FileSystemDiscoveryRuleProvider(
    ISystemSkillRegistry systemSkillRegistry) : IDiscoveryRuleProvider
{
    public async Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        var package = await systemSkillRegistry.LoadRequiredAsync(
            "digital-employee-discovery",
            cancellationToken: cancellationToken);
        if (package.StageRules.Count == 0)
        {
            throw new InvalidOperationException("Discovery system skill must declare stage rules.");
        }

        return new DiscoverySkillDefinition(
            SkillId: package.SkillId,
            SkillVersion: package.Version,
            SkillHash: package.SkillHash,
            SkillRootPath: package.RootPath,
            SkillContent: package.EntryContent,
            Files: package.Files
                .Select(file => new DiscoverySkillFileAsset(
                    RelativePath: file.RelativePath,
                    Content: file.Content,
                    ContentHash: file.ContentHash))
                .ToArray(),
            StageRules: package.StageRules
                .Select(rule => new DiscoveryStageRule(
                    Stage: NormalizeStage(rule.Stage),
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());
    }

    private static string NormalizeStage(string stage)
    {
        return stage.Trim().ToUpperInvariant() switch
        {
            "GOAL" or "MATERIAL" => HiringCollectionStage.Material,
            "SCENARIO" or "SKILL" => HiringCollectionStage.Skill,
            "SYSTEMS" or "GAPS" or "EXTERNAL" => HiringCollectionStage.External,
            "PACKAGE" or "READY_FOR_PACKAGING" => HiringCollectionStage.ReadyForPackaging,
            var value => value
        };
    }
}
