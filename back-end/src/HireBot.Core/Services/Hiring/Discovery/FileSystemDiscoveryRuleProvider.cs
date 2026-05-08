using System.Text;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring.Discovery;

internal sealed class FileSystemDiscoveryRuleProvider(
    IDiscoveryRoleTemplatePackageProvider roleTemplatePackageProvider) : IDiscoveryRuleProvider
{
    public async Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        var package = await roleTemplatePackageProvider.LoadAsync(cancellationToken);
        return BuildDiscoverySkill(package);
    }

    internal static DiscoverySkillDefinition BuildDiscoverySkill(TemplatePackageDefinition package)
    {
        if (package.StageRules.Count == 0)
        {
            throw new InvalidOperationException("Discovery role template must declare stage rules.");
        }

        var files = package.PackageFiles
            .Select(file => new DiscoverySkillFileAsset(
                RelativePath: file.RelativePath,
                Content: Encoding.UTF8.GetString(file.Content),
                ContentHash: file.ContentHash))
            .ToArray();

        return new DiscoverySkillDefinition(
            SkillId: package.PackageId,
            SkillVersion: package.PackageVersion,
            SkillHash: package.PackageHash,
            SkillRootPath: package.PackageRootPath,
            SkillContent: ResolveEntrySkillContent(package),
            Files: files,
            StageRules: package.StageRules
                .Select(rule => new DiscoveryStageRule(
                    Stage: NormalizeStage(rule.Stage),
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());
    }

    private static string ResolveEntrySkillContent(TemplatePackageDefinition package)
    {
        foreach (var candidatePath in EnumerateEntrySkillCandidates(package.EntrySkill))
        {
            var file = package.PackageFiles.FirstOrDefault(item =>
                string.Equals(item.RelativePath, candidatePath, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                return Encoding.UTF8.GetString(file.Content);
            }
        }

        var fallbackSkill = package.RequiredSkills.FirstOrDefault();
        if (fallbackSkill is not null)
        {
            return fallbackSkill.Content;
        }

        return package.ManifestJson;
    }

    private static IEnumerable<string> EnumerateEntrySkillCandidates(string? entrySkill)
    {
        if (string.IsNullOrWhiteSpace(entrySkill))
        {
            yield break;
        }

        var normalized = entrySkill.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            yield break;
        }

        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            yield return normalized;
            if (!normalized.StartsWith("skills/", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"skills/{normalized}";
            }

            yield break;
        }

        yield return $"{normalized}/SKILL.md";
        if (!normalized.StartsWith("skills/", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"skills/{normalized}/SKILL.md";
        }
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
