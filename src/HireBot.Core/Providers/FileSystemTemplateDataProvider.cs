using System.Text.Json;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Providers;

internal sealed class FileSystemTemplateDataProvider(
    FileSystemTemplatePackageProvider templatePackageProvider,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : ITemplateDataProvider
{
    public async Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<EmployeeTemplateDefinition>();
        foreach (var templateId in EnumerateTemplateIds())
        {
            var definition = await GetByIdAsync(templateId, cancellationToken);
            if (definition is not null)
            {
                result.Add(definition);
            }
        }

        return result;
    }

    public async Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        try
        {
            var package = await templatePackageProvider.LoadAsync(templateId.Trim(), cancellationToken);
            return MapPackageToDefinition(package);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateTemplateIds()
    {
        var configuredRoot = configuration["HireBot:TemplatePackagesRoot"];
        var packagesRoot = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuredRoot,
            Path.Combine("Assets", "TemplatePackages"));

        if (!Directory.Exists(packagesRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(packagesRoot))
        {
            if (HiringAssetFileSystem.IsIgnoredDirectory(directory))
            {
                continue;
            }

            yield return Path.GetFileName(directory);
        }
    }

    private static EmployeeTemplateDefinition MapPackageToDefinition(TemplatePackageDefinition package)
    {
        using var manifest = JsonDocument.Parse(package.ManifestJson);
        var root = manifest.RootElement;

        var name = TemplatePresentationHelpers.FirstNonEmpty(
            GetString(root, "display_name"),
            GetString(root, "name"),
            package.DisplayName,
            package.PackageId,
            package.RequestedTemplateId);
        var tagline = TemplatePresentationHelpers.FirstNonEmpty(
            GetString(root, "positioning"),
            GetString(root, "description"),
            package.Description,
            "Digital employee template");
        var description = TemplatePresentationHelpers.FirstNonEmpty(
            GetString(root, "description"),
            GetString(root, "positioning"),
            package.Description,
            $"{name} template");
        var useCases = ParseStringCollection(root, "use_cases");
        var tags = ParseStringCollection(root, "tags");
        var coreAbilities = package.RequiredSkills
            .Select(skill => skill.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inScope = useCases.Count > 0
            ? useCases
            : package.OntologySlices.Select(slice => $"Ontology: {slice.Name}").ToArray();
        if (inScope.Count == 0)
        {
            inScope = ["Execute only within declared template scope"];
        }

        var prerequisites = package.RequiredSkills
            .Select(skill => new TemplatePrerequisiteDto(
                SystemName: "Skill",
                PermissionName: skill.Name,
                RequiredLevel: "required",
                Purpose: "Built-in template skill"))
            .ToArray();
        var successCases = new[] { $"Built-in template package {package.PackageVersion}" };

        return new EmployeeTemplateDefinition(
            TemplateId: package.RequestedTemplateId,
            IconUrl: TemplatePresentationHelpers.BuildDefaultIconUrl(package.RequestedTemplateId, name),
            Name: name,
            Tagline: tagline,
            Description: description,
            CoreAbilityTags: tags.Count > 0 ? tags : useCases.Count > 0 ? useCases : ["General"],
            HiredCount: package.RequiredSkills.Count,
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: true,
            CoreAbilities: coreAbilities.Length > 0 ? coreAbilities : ["To be configured"],
            InScope: inScope,
            OutOfScope: [],
            Prerequisites: prerequisites,
            SuccessCases: successCases);
    }

    private static IReadOnlyList<string> ParseStringCollection(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

}

