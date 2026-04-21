namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringStatusDto(
    string HireId,
    string SandboxId,
    string Status,
    string? ErrorCode,
    string? ErrorMessage);
