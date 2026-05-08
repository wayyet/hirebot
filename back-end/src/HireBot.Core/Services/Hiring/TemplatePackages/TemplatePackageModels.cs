namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed record TemplatePackageDefinition(
    string RequestedTemplateId,
    string PackageId,
    string PackageVersion,
    string PackageHash,
    byte[]? SourceArchive,
    string PackageRootPath,
    string ManifestJson,
    string DisplayName,
    string Description,
    IReadOnlyList<TemplatePackageFileAsset> PackageFiles,
    IReadOnlyList<TemplateOntologySliceAsset> OntologySlices,
    IReadOnlyList<TemplateSkillAsset> RequiredSkills,
    string? EntrySkill,
    IReadOnlyList<TemplatePackageStageRule> StageRules);

internal sealed record TemplatePackageFileAsset(
    string RelativePath,
    byte[] Content,
    string ContentHash);

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

internal sealed record TemplatePackageStageRule(
    string Stage,
    string SkillName,
    string Description,
    IReadOnlyList<string> RequiredFields);
