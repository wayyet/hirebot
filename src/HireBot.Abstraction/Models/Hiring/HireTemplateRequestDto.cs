using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Hiring;

public sealed record HireTemplateRequestDto
{
    [Required]
    public string TenantId { get; init; } = string.Empty;

    [Required]
    public string OperatorId { get; init; } = string.Empty;

    public string? UseCase { get; init; }
}
