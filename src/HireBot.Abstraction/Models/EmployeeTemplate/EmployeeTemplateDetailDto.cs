namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record EmployeeTemplateDetailDto(
    string TemplateId,
    string IconUrl,
    string Name,
    string Tagline,
    string Description,
    IReadOnlyList<string> CoreAbilities,
    TemplateResponsibilityBoundaryDto ResponsibilityBoundary,
    IReadOnlyList<TemplatePrerequisiteDto> Prerequisites,
    IReadOnlyList<string> SuccessCases,
    TemplateCtaDto Cta);
