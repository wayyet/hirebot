namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringAuditLogDto(
    string LogId,
    string Stage,
    string SkillName,
    string Decision,
    string Actor,
    string? Comment,
    string InputDigest,
    string OutputDigest,
    DateTimeOffset TimestampUtc);
