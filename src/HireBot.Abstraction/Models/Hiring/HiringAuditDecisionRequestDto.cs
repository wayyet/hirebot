using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringAuditDecisionRequestDto
{
    [Required]
    public string Stage { get; init; } = string.Empty;

    [Required]
    public string Decision { get; init; } = string.Empty;

    public string? Comment { get; init; }

    public string? RollbackTargetStage { get; init; }
}
