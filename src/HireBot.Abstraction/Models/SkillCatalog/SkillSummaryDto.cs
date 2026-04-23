namespace HireBot.Abstraction.Models.SkillCatalog;

public sealed record SkillSummaryDto(
    string SkillId,
    string Name,
    string Description,
    string Level,
    string Status,
    string Version,
    string UpdatedAt);
