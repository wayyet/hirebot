using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
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

        var packageRoot = ResolvePackageRoot(candidatePath);
        if (packageRoot is null)
        {
            throw new InvalidOperationException($"Template package root not found for templateId '{normalizedTemplateId}'.");
        }

        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Template manifest not found: {manifestPath}");
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

            var fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            ontologySlices.Add(new TemplateOntologySliceAsset(
                Name: FirstNonEmpty(slice.Name, Path.GetFileNameWithoutExtension(fullPath)),
                RelativePath: relativePath,
                Type: FirstNonEmpty(slice.Type, "digital_employee_slice"),
                Required: slice.Required ?? false,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

        var requiredSkills = new List<TemplateSkillAsset>();
        foreach (var skill in manifest.Skills ?? [])
        {
            if (skill.Required != true)
            {
                continue;
            }

            var relativePath = skill.Path?.Trim();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            requiredSkills.Add(new TemplateSkillAsset(
                Name: FirstNonEmpty(skill.Name, Path.GetFileNameWithoutExtension(fullPath)),
                RelativePath: relativePath,
                Required: true,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

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

        var packageHash = await HiringAssetFileSystem.ComputeDirectoryHashAsync(packageRoot, cancellationToken);
        return new TemplatePackageDefinition(
            RequestedTemplateId: normalizedTemplateId,
            PackageId: FirstNonEmpty(manifest.Name, normalizedTemplateId),
            PackageVersion: FirstNonEmpty(manifest.Version, "v1-placeholder"),
            PackageHash: packageHash,
            PackageRootPath: packageRoot,
            ManifestJson: manifestJson,
            DisplayName: FirstNonEmpty(manifest.DisplayName, manifest.Name, normalizedTemplateId),
            Description: FirstNonEmpty(manifest.Description, manifest.Positioning, "NCrew template package"),
            PackageFiles: packageFiles
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OntologySlices: ontologySlices,
            RequiredSkills: requiredSkills);
    }

    private static string? ResolvePackageRoot(string candidatePath)
    {
        if (File.Exists(Path.Combine(candidatePath, "manifest.json")))
        {
            return candidatePath;
        }

        if (!Directory.Exists(candidatePath))
        {
            return null;
        }

        foreach (var directory in Directory.GetDirectories(candidatePath))
        {
            if (HiringAssetFileSystem.IsIgnoredDirectory(directory))
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, "manifest.json")))
            {
                return directory;
            }
        }

        return null;
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
        [property: JsonPropertyName("ontology_slices")] IReadOnlyList<TemplateOntologySliceDocument>? OntologySlices,
        [property: JsonPropertyName("skills")] IReadOnlyList<TemplateSkillDocument>? Skills);

    private sealed record TemplateOntologySliceDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("required")] bool? Required);

    private sealed record TemplateSkillDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("required")] bool? Required);
}
