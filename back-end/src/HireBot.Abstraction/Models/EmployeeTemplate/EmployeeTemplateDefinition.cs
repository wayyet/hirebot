namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record EmployeeTemplateDefinition(
    string TemplateId,
    string IconUrl,
    string Name,
    string Tagline,
    string Description,
    string DetailDoc,
    IReadOnlyList<string> CoreAbilityTags,
    int HiredCount,
    decimal SuccessRate,
    decimal AvgRating,
    bool IsAvailable,
    IReadOnlyList<string> CoreAbilities,
    IReadOnlyList<string> InScope,
    IReadOnlyList<string> OutOfScope,
    IReadOnlyList<TemplatePrerequisiteDto> Prerequisites,
    IReadOnlyList<string> SuccessCases);
