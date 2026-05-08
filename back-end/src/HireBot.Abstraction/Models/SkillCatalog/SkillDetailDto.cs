namespace HireBot.Abstraction.Models.SkillCatalog;

public sealed record SkillDetailDto(
    string SkillId,
    string Name,
    string Description,
    string Level,
    string Status,
    string Version,
    string UpdatedAt,
    string InputExample,
    string OutputExample,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> BoundTemplates,
    IReadOnlyList<string> Files);
