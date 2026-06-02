using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed class FileSystemTemplatePackageProvider(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : ITemplatePackageProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var normalizedTemplateId = string.IsNullOrWhiteSpace(templateId) ? "default" : templateId.Trim();
        var configuredRoot = configuration["HireBot:TemplatePackagesRoot"];
        var packagesRoot = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuredRoot,
            Path.Combine("Assets", "TemplatePackages"));

        var candidatePath = Path.Combine(packagesRoot, HiringAssetFileSystem.SanitizePathSegment(normalizedTemplateId));
        if (!Directory.Exists(candidatePath))
        {
            candidatePath = Path.Combine(packagesRoot, "default");
        }

        return await LoadFromDirectoryAsync(candidatePath, normalizedTemplateId, cancellationToken);
    }

    internal async Task<TemplatePackageDefinition> LoadFromDirectoryAsync(
        string directoryPath,
        string requestedTemplateId,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException($"Template package directory not found for templateId '{requestedTemplateId}': {directoryPath}");
        }

        return await LoadFromPackageRootAsync(directoryPath, requestedTemplateId, cancellationToken);
    }

    private async Task<TemplatePackageDefinition> LoadFromPackageRootAsync(
        string packageRoot,
        string requestedTemplateId,
        CancellationToken cancellationToken)
    {
        // 兼容历史目录：当 packageRoot 下没有 manifest.json，但只存在唯一一个子目录且该子目录是真正的包根时，
        // 自动下沉一层，避免产物里多出 <wrapper>/ 顶层包裹目录。
        packageRoot = ResolveEffectivePackageRoot(packageRoot);

        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return await LoadFromConventionPackageRootAsync(packageRoot, requestedTemplateId, cancellationToken);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<TemplateManifestDocument>(manifestJson, JsonOptions)
                       ?? throw new InvalidOperationException($"Template manifest is invalid: {manifestPath}");

        var ontologySlices = new List<TemplateOntologySliceAsset>();
        foreach (var slice in manifest.OntologySlices ?? [])
        {
            var relativePath = slice.Path?.Trim();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = ResolveManifestAssetFilePath(packageRoot, relativePath);
            if (fullPath is null)
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            ontologySlices.Add(new TemplateOntologySliceAsset(
                Name: FirstNonEmpty(slice.Name, Path.GetFileNameWithoutExtension(fullPath)),
                RelativePath: relativePath.Replace('\\', '/').TrimStart('/'),
                Type: FirstNonEmpty(slice.Type, "digital_employee_slice"),
                Required: slice.Required ?? false,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

        var skills = new List<TemplateSkillAsset>();
        foreach (var skill in manifest.Skills ?? [])
        {
            var relativePath = skill.Path?.Trim();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = ResolveManifestAssetFilePath(packageRoot, relativePath, "SKILL.md");
            if (fullPath is null)
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            skills.Add(new TemplateSkillAsset(
                // manifest 未声明 name 时，从 skills/<slug>/... 提取更稳妥。
                Name: FirstNonEmpty(skill.Name, ExtractSkillName(normalizedRelativePath), Path.GetFileNameWithoutExtension(fullPath)),
                RelativePath: normalizedRelativePath,
                Required: skill.Required ?? false,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }
        var requiredSkills = skills.Where(skill => skill.Required).ToArray();

        var stageRules = (manifest.StageRules ?? [])
            .Where(rule =>
                !string.IsNullOrWhiteSpace(rule.Stage) &&
                !string.IsNullOrWhiteSpace(rule.SkillName) &&
                !string.IsNullOrWhiteSpace(rule.Description))
            .Select(rule => new TemplatePackageStageRule(
                Stage: rule.Stage!.Trim(),
                SkillName: rule.SkillName!.Trim(),
                Description: rule.Description!.Trim(),
                RequiredFields: (rule.RequiredFields ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray()))
            .ToArray();

        var packageFiles = await LoadPackageFilesAsync(packageRoot, cancellationToken);

        var packageHash = await HiringAssetFileSystem.ComputeDirectoryHashAsync(packageRoot, cancellationToken);
        return new TemplatePackageDefinition(
            RequestedTemplateId: requestedTemplateId,
            PackageId: FirstNonEmpty(manifest.Name, requestedTemplateId),
            PackageVersion: FirstNonEmpty(manifest.Version, "v1-placeholder"),
            PackageHash: packageHash,
            SourceArchive: null,
            PackageRootPath: packageRoot,
            ManifestJson: manifestJson,
            DisplayName: FirstNonEmpty(manifest.DisplayName, manifest.Name, requestedTemplateId),
            Description: FirstNonEmpty(manifest.Description, manifest.Positioning, "NCrew template package"),
            PackageFiles: packageFiles
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OntologySlices: ontologySlices,
            Skills: skills,
            RequiredSkills: requiredSkills,
            EntrySkill: NormalizeEntrySkill(manifest.EntrySkill),
            StageRules: stageRules);
    }

    private async Task<TemplatePackageDefinition> LoadFromConventionPackageRootAsync(
        string packageRoot,
        string requestedTemplateId,
        CancellationToken cancellationToken)
    {
        var packageFiles = await LoadPackageFilesAsync(packageRoot, cancellationToken);
        if (packageFiles.Length == 0)
        {
            throw new InvalidOperationException($"Template package directory is empty: {packageRoot}");
        }

        var packageId = ResolveConventionPackageId(packageRoot, requestedTemplateId);
        var ontologySlices = BuildConventionOntologySlices(packageFiles);
        var skills = BuildConventionSkills(packageFiles);
        var entrySkill = ResolveConventionEntrySkill(packageId, skills);
        var stageRules = BuildConventionStageRules(skills);
        var displayName = packageId.Replace('-', ' ');
        var manifestJson = BuildConventionManifestJson(packageId, displayName, entrySkill, ontologySlices, skills, stageRules);
        var packageHash = await HiringAssetFileSystem.ComputeDirectoryHashAsync(packageRoot, cancellationToken);

        return new TemplatePackageDefinition(
            RequestedTemplateId: requestedTemplateId,
            PackageId: packageId,
            PackageVersion: "v1-placeholder",
            PackageHash: packageHash,
            SourceArchive: null,
            PackageRootPath: packageRoot,
            ManifestJson: manifestJson,
            DisplayName: displayName,
            Description: "Convention-based template package",
            PackageFiles: packageFiles,
            OntologySlices: ontologySlices,
            Skills: skills,
            RequiredSkills: skills,
            EntrySkill: entrySkill,
            StageRules: stageRules);
    }

    private static string ResolveConventionPackageId(string packageRoot, string requestedTemplateId)
    {
        var packageDirectoryName = Path.GetFileName(packageRoot);
        return FirstNonEmpty(
            HiringAssetFileSystem.SanitizePathSegment(requestedTemplateId),
            HiringAssetFileSystem.SanitizePathSegment(packageDirectoryName));
    }

    private static TemplateOntologySliceAsset[] BuildConventionOntologySlices(
        IReadOnlyList<TemplatePackageFileAsset> packageFiles)
    {
        return packageFiles
            .Where(file =>
                file.RelativePath.StartsWith("ontology/", StringComparison.OrdinalIgnoreCase) &&
                file.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(file => new TemplateOntologySliceAsset(
                Name: Path.GetFileNameWithoutExtension(file.RelativePath),
                RelativePath: file.RelativePath,
                Type: "digital_employee_slice",
                Required: true,
                Content: Encoding.UTF8.GetString(file.Content),
                ContentHash: file.ContentHash))
            .ToArray();
    }

    private static TemplateSkillAsset[] BuildConventionSkills(
        IReadOnlyList<TemplatePackageFileAsset> packageFiles)
    {
        return packageFiles
            .Where(file =>
                file.RelativePath.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) &&
                file.RelativePath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase))
            .Select(file => new TemplateSkillAsset(
                Name: ExtractSkillName(file.RelativePath),
                RelativePath: file.RelativePath,
                Required: true,
                Content: Encoding.UTF8.GetString(file.Content),
                ContentHash: file.ContentHash))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveConventionEntrySkill(
        string packageId,
        IReadOnlyList<TemplateSkillAsset> requiredSkills)
    {
        var preferredRelativePath = $"skills/{packageId}/SKILL.md";
        var preferred = requiredSkills.FirstOrDefault(skill =>
            string.Equals(skill.RelativePath, preferredRelativePath, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            return $"skills/{preferred.Name}";
        }

        var fallback = requiredSkills.FirstOrDefault();
        return fallback is null
            ? null
            : $"skills/{fallback.Name}";
    }

    private static TemplatePackageStageRule[] BuildConventionStageRules(
        IReadOnlyList<TemplateSkillAsset> requiredSkills)
    {
        static bool HasSkill(IReadOnlyList<TemplateSkillAsset> skills, string relativePath)
            => skills.Any(skill => string.Equals(skill.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        var stageRules = new List<TemplatePackageStageRule>();
        if (HasSkill(requiredSkills, "skills/ontology-extraction/SKILL.md"))
        {
            stageRules.Add(new TemplatePackageStageRule(
                Stage: "material",
                SkillName: "ontology-extraction",
                Description: "Organize material-stage inputs for ontology extraction.",
                RequiredFields: []));
        }

        if (HasSkill(requiredSkills, "skills/skill-generation/SKILL.md"))
        {
            stageRules.Add(new TemplatePackageStageRule(
                Stage: "skill",
                SkillName: "skill-generation",
                Description: "Organize skill-stage inputs for skill generation.",
                RequiredFields: []));
        }

        if (HasSkill(requiredSkills, "skills/external-config/SKILL.md"))
        {
            stageRules.Add(new TemplatePackageStageRule(
                Stage: "external",
                SkillName: "external-config",
                Description: "Organize external-stage inputs for external configuration.",
                RequiredFields: []));
        }

        if (HasSkill(requiredSkills, "skills/diagnosis/SKILL.md"))
        {
            stageRules.Add(new TemplatePackageStageRule(
                Stage: "ready_for_packaging",
                SkillName: "diagnosis",
                Description: "Run diagnosis before packaging.",
                RequiredFields: []));
        }

        if (HasSkill(requiredSkills, "skills/packaging-test-cases/SKILL.md"))
        {
            stageRules.Add(new TemplatePackageStageRule(
                Stage: "ready_for_packaging",
                SkillName: "packaging-test-cases",
                Description: "Optionally generate evaluation test cases; missing testcases must not block packaging.",
                RequiredFields: []));
        }

        return stageRules.ToArray();
    }

    private static string BuildConventionManifestJson(
        string packageId,
        string displayName,
        string? entrySkill,
        IReadOnlyList<TemplateOntologySliceAsset> ontologySlices,
        IReadOnlyList<TemplateSkillAsset> skills,
        IReadOnlyList<TemplatePackageStageRule> stageRules)
    {
        var payload = new
        {
            name = packageId,
            display_name = displayName,
            description = "Convention-based template package",
            version = "v1-placeholder",
            entry_skill = entrySkill,
            ontology_slices = ontologySlices.Select(slice => new
            {
                name = slice.Name,
                path = slice.RelativePath,
                type = slice.Type,
                required = slice.Required
            }),
            skills = skills.Select(skill => new
            {
                name = skill.Name,
                path = skill.RelativePath,
                required = skill.Required
            }),
            stage_rules = stageRules.Select(rule => new
            {
                stage = rule.Stage,
                skill_name = rule.SkillName,
                description = rule.Description,
                required_fields = rule.RequiredFields
            })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string ExtractSkillName(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 ? segments[1] : Path.GetFileNameWithoutExtension(relativePath);
    }

    // 当 packageRoot 下没有 manifest.json，但仅存在单个子目录且该子目录里有 manifest.json 或 skills/、ontology/、config/ 等约定子目录时，
    // 视作包裹目录，自动下沉到该子目录。这样可以让 Assets/TemplatePackages/default/NCrewTemplate/ 也被识别为真正的包根。
    private static string ResolveEffectivePackageRoot(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            return packageRoot;
        }

        // 根目录已经有 manifest 或常见包内容文件 → 不需要下沉。
        if (File.Exists(Path.Combine(packageRoot, "manifest.json")))
        {
            return packageRoot;
        }

        var hasTopLevelFiles = Directory.EnumerateFiles(packageRoot, "*", SearchOption.TopDirectoryOnly)
            .Any(path => !HiringAssetFileSystem.IsIgnoredPath(path));
        if (hasTopLevelFiles)
        {
            return packageRoot;
        }

        var subDirectories = Directory.EnumerateDirectories(packageRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !HiringAssetFileSystem.IsIgnoredPath(path))
            .ToArray();
        if (subDirectories.Length != 1)
        {
            return packageRoot;
        }

        var candidate = subDirectories[0];
        var looksLikePackage = File.Exists(Path.Combine(candidate, "manifest.json"))
            || Directory.Exists(Path.Combine(candidate, "skills"))
            || Directory.Exists(Path.Combine(candidate, "ontology"))
            || Directory.Exists(Path.Combine(candidate, "config"));

        return looksLikePackage ? candidate : packageRoot;
    }

    private static async Task<TemplatePackageFileAsset[]> LoadPackageFilesAsync(
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var packageFiles = new List<TemplatePackageFileAsset>();
        foreach (var filePath in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories))
        {
            if (HiringAssetFileSystem.IsIgnoredPath(filePath))
            {
                continue;
            }

            var rawRelativePath = Path.GetRelativePath(packageRoot, filePath).Replace('\\', '/');
            if (!TryNormalizeArchiveRelativePath(rawRelativePath, out var normalizedRelativePath))
            {
                continue;
            }

            var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            packageFiles.Add(new TemplatePackageFileAsset(
                RelativePath: normalizedRelativePath,
                Content: content,
                ContentHash: ComputeContentHash(content)));
        }

        return packageFiles
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveManifestAssetFilePath(
        string packageRoot,
        string relativePath,
        string? defaultFileName = null)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim('/');
        if (normalizedRelativePath.Length == 0)
        {
            return null;
        }

        var fullPath = Path.Combine(packageRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (string.IsNullOrWhiteSpace(defaultFileName) || !Directory.Exists(fullPath))
        {
            return null;
        }

        var defaultPath = Path.Combine(fullPath, defaultFileName);
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static string? NormalizeEntrySkill(string? entrySkill)
    {
        if (string.IsNullOrWhiteSpace(entrySkill))
        {
            return null;
        }

        return entrySkill.Trim().Replace('\\', '/').Trim('/');
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeArchiveRelativePath(string path, out string normalizedPath)
    {
        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(static segment =>
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            normalizedPath = string.Empty;
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    private static string ComputeContentHash(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private sealed record TemplateManifestDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("positioning")] string? Positioning,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("entry_skill")] string? EntrySkill,
        [property: JsonPropertyName("ontology_slices")] IReadOnlyList<TemplateOntologySliceDocument>? OntologySlices,
        [property: JsonPropertyName("skills")] IReadOnlyList<TemplateSkillDocument>? Skills,
        [property: JsonPropertyName("stage_rules")] IReadOnlyList<TemplateStageRuleDocument>? StageRules);

    private sealed record TemplateOntologySliceDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("required")] bool? Required);

    private sealed record TemplateSkillDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("required")] bool? Required);

    private sealed record TemplateStageRuleDocument(
        [property: JsonPropertyName("stage")] string? Stage,
        [property: JsonPropertyName("skill_name")] string? SkillName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("required_fields")] IReadOnlyList<string>? RequiredFields);
}
