using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Training;

public sealed record TrainingDecisionRequestDto
{
    [Required]
    public string Decision { get; init; } = string.Empty;

    public string? Comment { get; init; }
}
