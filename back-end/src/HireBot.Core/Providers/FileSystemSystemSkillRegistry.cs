using System.Text.Json;
using System.Text.Json.Serialization;
using HireBot.Abstraction.Models.SkillCatalog;
using HireBot.Abstraction.Providers;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.SystemSkills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Providers;

internal sealed class FileSystemSystemSkillRegistry(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : ISystemSkillRegistry, ISkillCatalogProvider
{
    private const string SystemSkillsRootKey = "HireBot:SystemSkillsRoot";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<SystemSkillPackage>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rootPath = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuration[SystemSkillsRootKey],
            Path.Combine("Assets", "SystemSkills"));
        if (!Directory.Exists(rootPath))
        {
            throw new InvalidOperationException($"System skills root not found: {rootPath}");
        }

        var packages = new List<SystemSkillPackage>();
        foreach (var directory in EnumerateSkillPackageDirectories(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.Add(await LoadPackageAsync(directory, cancellationToken));
        }

        return packages
            .OrderBy(item => item.SkillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SystemSkillPackage?> FindAsync(string skillId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        var matches = (await ListAsync(cancellationToken))
            .Where(item => item.SkillId.Equals(skillId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Duplicate system skill id detected: {skillId.Trim()}")
        };
    }

    public async Task<SystemSkillPackage> LoadRequiredAsync(
        string skillId,
        string? configuredPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            throw new InvalidOperationException("System skill id is required.");
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolvedPath = HiringAssetFileSystem.ResolveDirectory(
                hostEnvironment.ContentRootPath,
                configuredPath,
                Path.Combine("Assets", "SystemSkills"));
            if (!Directory.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"Configured system skill path not found: {resolvedPath}");
            }

            var matches = new List<SystemSkillPackage>();
            foreach (var directory in EnumerateSkillPackageDirectories(resolvedPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = await LoadPackageAsync(directory, cancellationToken);
                if (package.SkillId.Equals(skillId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(package);
                }
            }

            return matches.Count switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException(
                    $"Configured system skill path does not contain skill '{skillId.Trim()}': {resolvedPath}"),
                _ => throw new InvalidOperationException(
                    $"Configured system skill path contains duplicate skill '{skillId.Trim()}': {resolvedPath}")
            };
        }

        return await FindAsync(skillId, cancellationToken)
               ?? throw new InvalidOperationException($"System skill not found: {skillId.Trim()}");
    }

    async Task<IReadOnlyList<SkillSummaryDto>> ISkillCatalogProvider.GetSkillsAsync(
        string? q,
        string? level,
        string? status,
        CancellationToken cancellationToken)
    {
        var packages = await ListAsync(cancellationToken);
        var query = packages.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            query = query.Where(item =>
                item.SkillId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            query = query.Where(item => item.Level.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderBy(item => item.SkillId, StringComparer.OrdinalIgnoreCase)
            .Select(MapSummary)
            .ToArray();
    }

    async Task<SkillDetailDto?> ISkillCatalogProvider.GetSkillAsync(string skillId, CancellationToken cancellationToken)
    {
        var package = await FindAsync(skillId, cancellationToken);
        return package is null ? null : MapDetail(package);
    }

    private static IEnumerable<string> EnumerateSkillPackageDirectories(string rootPath)
    {
        if (IsSkillPackageDirectory(rootPath))
        {
            yield return rootPath;
            yield break;
        }

        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var stack = new Stack<string>(
            Directory.GetDirectories(rootPath)
                .Where(directory => !ShouldSkipDirectory(directory))
                .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase));
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsSkillPackageDirectory(current))
            {
                yield return current;
                continue;
            }

            foreach (var child in Directory
                         .GetDirectories(current)
                         .Where(directory => !ShouldSkipDirectory(directory))
                         .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase))
            {
                stack.Push(child);
            }
        }
    }

    private static bool IsSkillPackageDirectory(string directoryPath)
    {
        return File.Exists(Path.Combine(directoryPath, "manifest.json")) &&
               File.Exists(Path.Combine(directoryPath, "SKILL.md"));
    }

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        return HiringAssetFileSystem.IsIgnoredDirectory(directoryPath) ||
               name.Equals(".venv", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("__pycache__", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipFile(string filePath)
    {
        if (HiringAssetFileSystem.IsIgnoredPath(filePath))
        {
            return true;
        }

        var normalizedPath = filePath.Replace('\\', '/');
        return normalizedPath.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/__pycache__/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SystemSkillPackage> LoadPackageAsync(string skillRoot, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(skillRoot, "manifest.json");
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"System skill manifest not found: {manifestPath}");
        }

        if (!File.Exists(skillPath))
        {
            throw new InvalidOperationException($"System skill entry not found: {skillPath}");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<SystemSkillManifestDocument>(manifestJson, JsonOptions)
                       ?? throw new InvalidOperationException($"System skill manifest is invalid: {manifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.SkillId))
        {
            throw new InvalidOperationException($"System skill manifest missing skill_id: {manifestPath}");
        }

        var rootContent = await File.ReadAllTextAsync(skillPath, cancellationToken);
        var loadedFiles = new List<(SystemSkillFileAsset Asset, DateTimeOffset UpdatedAtUtc)>();
        foreach (var filePath in Directory
                     .EnumerateFiles(skillRoot, "*", SearchOption.AllDirectories)
                     .Where(file => !ShouldSkipFile(file))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            loadedFiles.Add((
                new SystemSkillFileAsset(
                    RelativePath: Path.GetRelativePath(skillRoot, filePath).Replace('\\', '/'),
                    Content: content,
                    ContentHash: HiringAssetFileSystem.ComputeContentHash(content)),
                File.GetLastWriteTimeUtc(filePath)));
        }

        if (loadedFiles.Count == 0)
        {
            throw new InvalidOperationException($"System skill package is empty: {skillRoot}");
        }

        var files = loadedFiles
            .OrderBy(item => item.Asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Asset)
            .ToArray();
        var hashSeed = string.Join('\n', files.Select(file => $"{file.RelativePath}:{file.ContentHash}"));
        var hasExplicitEntrySkill = !string.IsNullOrWhiteSpace(manifest.EntrySkill);
        var entrySkill = FirstNonEmpty(manifest.EntrySkill, manifest.SkillId);
        var entryContent = ResolveEntryContent(files, entrySkill, manifestPath, hasExplicitEntrySkill);
        var stageRules = (manifest.StageRules ?? [])
            .Select(rule => new SystemSkillStageRule(
                Stage: RequireValue(rule.Stage, manifestPath, "stage_rules[].stage"),
                SkillName: RequireValue(rule.SkillName, manifestPath, "stage_rules[].skill_name"),
                Description: RequireValue(rule.Description, manifestPath, "stage_rules[].description"),
                RequiredFields: (rule.RequiredFields ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray()))
            .ToArray();

        return new SystemSkillPackage(
            SkillId: manifest.SkillId.Trim(),
            DisplayName: FirstNonEmpty(manifest.DisplayName, manifest.SkillId),
            Description: FirstNonEmpty(manifest.Description, manifest.SkillId),
            Level: FirstNonEmpty(manifest.Level, "system"),
            Status: FirstNonEmpty(manifest.Status, "active"),
            Version: FirstNonEmpty(manifest.Version, "1.0"),
            EntrySkill: entrySkill,
            SkillHash: HiringAssetFileSystem.ComputeContentHash(hashSeed),
            RootPath: skillRoot,
            EntryContent: entryContent,
            InputExample: manifest.InputExample?.Trim() ?? string.Empty,
            OutputExample: manifest.OutputExample?.Trim() ?? string.Empty,
            UpdatedAtUtc: loadedFiles.Max(item => item.UpdatedAtUtc),
            Tags: (manifest.Tags ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            BoundTemplates: (manifest.BoundTemplates ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StageRules: stageRules,
            Files: files);
    }

    private static string RequireValue(string? value, string manifestPath, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        throw new InvalidOperationException($"System skill manifest missing {fieldName}: {manifestPath}");
    }

    private static string ResolveEntryContent(
        IReadOnlyList<SystemSkillFileAsset> files,
        string entrySkill,
        string manifestPath,
        bool hasExplicitEntrySkill)
    {
        var rootSkill = files.FirstOrDefault(file =>
            file.RelativePath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase));
        if (!hasExplicitEntrySkill)
        {
            return rootSkill?.Content
                   ?? throw new InvalidOperationException($"System skill entry not found: {manifestPath}");
        }

        var normalizedEntrySkill = entrySkill.Trim().Replace('\\', '/').Trim('/');
        var candidatePath = normalizedEntrySkill.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalizedEntrySkill
            : $"{normalizedEntrySkill}/SKILL.md";
        var entryFile = files.FirstOrDefault(file =>
            file.RelativePath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase));
        return entryFile?.Content
               ?? throw new InvalidOperationException(
                   $"System skill entry_skill target not found: {entrySkill} ({manifestPath})");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static SkillSummaryDto MapSummary(SystemSkillPackage package)
    {
        return new SkillSummaryDto(
            SkillId: package.SkillId,
            Name: package.DisplayName,
            Description: package.Description,
            Level: package.Level,
            Status: package.Status,
            Version: package.Version,
            UpdatedAt: package.UpdatedAtUtc.ToString("O"));
    }

    private static SkillDetailDto MapDetail(SystemSkillPackage package)
    {
        return new SkillDetailDto(
            SkillId: package.SkillId,
            Name: package.DisplayName,
            Description: package.Description,
            Level: package.Level,
            Status: package.Status,
            Version: package.Version,
            UpdatedAt: package.UpdatedAtUtc.ToString("O"),
            InputExample: package.InputExample,
            OutputExample: package.OutputExample,
            Tags: package.Tags,
            BoundTemplates: package.BoundTemplates,
            Files: package.Files
                .Select(file => file.RelativePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private sealed record SystemSkillManifestDocument(
        [property: JsonPropertyName("skill_id")] string? SkillId,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("level")] string? Level,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("entry_skill")] string? EntrySkill,
        [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags,
        [property: JsonPropertyName("input_example")] string? InputExample,
        [property: JsonPropertyName("output_example")] string? OutputExample,
        [property: JsonPropertyName("bound_templates")] IReadOnlyList<string>? BoundTemplates,
        [property: JsonPropertyName("stage_rules")] IReadOnlyList<SystemSkillStageRuleDocument>? StageRules);

    private sealed record SystemSkillStageRuleDocument(
        [property: JsonPropertyName("stage")] string? Stage,
        [property: JsonPropertyName("skill_name")] string? SkillName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("required_fields")] IReadOnlyList<string>? RequiredFields);
}
