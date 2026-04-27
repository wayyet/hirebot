using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Evaluation;

public sealed record AiEvaluationDecisionRequestDto
{
    [Required]
    public string Decision { get; init; } = string.Empty;

    public string? Comment { get; init; }
}
