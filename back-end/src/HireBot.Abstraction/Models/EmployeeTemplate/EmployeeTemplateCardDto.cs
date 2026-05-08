namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record EmployeeTemplateCardDto(
    string TemplateId,
    string IconUrl,
    string Name,
    string Tagline,
    IReadOnlyList<string> CoreAbilityTags,
    TemplateTrustProofDto TrustProof,
    bool IsAvailable);
