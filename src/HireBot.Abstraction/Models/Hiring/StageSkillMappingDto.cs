namespace HireBot.Abstraction.Models.Hiring;

public sealed record StageSkillMappingDto(
    string Stage,
    string SkillName,
    IReadOnlyList<string> RequiredFields,
    string Description);
