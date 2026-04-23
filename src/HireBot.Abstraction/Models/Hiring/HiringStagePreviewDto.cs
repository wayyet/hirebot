namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringStagePreviewDto(
    string HireId,
    string Stage,
    string SkillName,
    string Summary,
    IReadOnlyDictionary<string, string?> StructuredData,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> RiskNotes,
    bool ReadyForAudit,
    DateTimeOffset GeneratedAt);
