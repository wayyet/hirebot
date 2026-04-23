using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.Hiring.Discovery;

internal sealed class FileSystemDiscoveryRuleProvider(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IDiscoveryRuleProvider
{
    public async Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuredRoot = configuration["HireBot:DiscoverySkillRoot"];
        var rootPath = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuredRoot,
            Path.Combine("Assets", "SystemSkills", "digital-employee-discovery"));

        var skillRoot = ResolveSkillRoot(rootPath);
        if (skillRoot is null)
        {
            throw new InvalidOperationException($"Discovery skill root not found: {rootPath}");
        }

        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        if (!File.Exists(skillPath))
        {
            throw new InvalidOperationException($"Discovery skill entry not found: {skillPath}");
        }

        var skillContent = await File.ReadAllTextAsync(skillPath, cancellationToken);
        var version = ParseMetadataValue(skillContent, "version") ?? "1.0";
        var files = await LoadSkillFilesAsync(skillRoot, cancellationToken);
        var skillHash = await HiringAssetFileSystem.ComputeDirectoryHashAsync(skillRoot, cancellationToken);

        return new DiscoverySkillDefinition(
            SkillId: "digital-employee-discovery",
            SkillVersion: version,
            SkillHash: skillHash,
            SkillRootPath: skillRoot,
            SkillContent: skillContent,
            Files: files,
            StageRules: BuildStageRules());
    }

    private static async Task<IReadOnlyList<DiscoverySkillFileAsset>> LoadSkillFilesAsync(
        string skillRoot,
        CancellationToken cancellationToken)
    {
        var files = new List<DiscoverySkillFileAsset>();
        foreach (var filePath in Directory
                     .EnumerateFiles(skillRoot, "*", SearchOption.AllDirectories)
                     .Where(file => !HiringAssetFileSystem.IsIgnoredPath(file))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            files.Add(new DiscoverySkillFileAsset(
                RelativePath: Path.GetRelativePath(skillRoot, filePath).Replace('\\', '/'),
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

        return files;
    }

    private static string? ResolveSkillRoot(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "SKILL.md")))
        {
            return rootPath;
        }

        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        foreach (var directory in Directory.GetDirectories(rootPath))
        {
            if (HiringAssetFileSystem.IsIgnoredDirectory(directory))
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, "SKILL.md")))
            {
                return directory;
            }
        }

        return null;
    }

    private static string? ParseMetadataValue(string markdown, string key)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var match = Regex.Match(
            markdown,
            $"^\\s*{Regex.Escape(key)}\\s*:\\s*\\\"?(?<value>[^\\r\\n\\\"]+)\\\"?\\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static IReadOnlyList<DiscoveryStageRule> BuildStageRules()
    {
        return
        [
            new DiscoveryStageRule(
                Stage: HiringCollectionStage.Goal,
                SkillName: "system.discovery.goal-alignment",
                Description: "锁定雇佣目标、负责人和成功标准。",
                RequiredFields: ["business_goal", "owner", "success_metric"]),
            new DiscoveryStageRule(
                Stage: HiringCollectionStage.Scenario,
                SkillName: "system.discovery.scenario-framing",
                Description: "澄清业务场景、触发条件和预期结果。",
                RequiredFields: ["user_profile", "trigger_event", "expected_outcome"]),
            new DiscoveryStageRule(
                Stage: HiringCollectionStage.Systems,
                SkillName: "system.discovery.system-boundary",
                Description: "确认系统清单、权限范围和数据来源。",
                RequiredFields: ["system_list", "permission_scope", "data_sources"]),
            new DiscoveryStageRule(
                Stage: HiringCollectionStage.Gaps,
                SkillName: "system.discovery.gap-synthesis",
                Description: "明确阻塞项、风险等级和回退方案。",
                RequiredFields: ["blockers", "risk_level", "fallback_plan"]),
            new DiscoveryStageRule(
                Stage: HiringCollectionStage.Package,
                SkillName: "system.discovery.package-readiness",
                Description: "确认运行手册、验收标准和交付窗口。",
                RequiredFields: ["runbook", "acceptance_criteria", "delivery_window"])
        ];
    }
}
