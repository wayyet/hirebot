namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed record TemplatePackageDefinition(
    string RequestedTemplateId,
    string PackageId,
    string PackageVersion,
    string PackageHash,
    string PackageRootPath,
    string ManifestJson,
    string DisplayName,
    string Description,
    IReadOnlyList<TemplateOntologySliceAsset> OntologySlices,
    IReadOnlyList<TemplateSkillAsset> RequiredSkills);

internal sealed record TemplateOntologySliceAsset(
    string Name,
    string RelativePath,
    string Type,
    bool Required,
    string Content,
    string ContentHash);

internal sealed record TemplateSkillAsset(
    string Name,
    string RelativePath,
    bool Required,
    string Content,
    string ContentHash);
