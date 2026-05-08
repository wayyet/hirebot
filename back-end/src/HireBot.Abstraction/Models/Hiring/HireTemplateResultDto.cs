namespace HireBot.Abstraction.Models.Hiring;

public sealed record HireTemplateResultDto(
    string HireId,
    string SandboxId,
    string Status,
    string NextAction,
    string? SessionId = null);
