using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationOnboardingDecisionRequestDto
{
    [Required]
    public string Decision { get; init; } = string.Empty;

    public string? Comment { get; init; }
}
