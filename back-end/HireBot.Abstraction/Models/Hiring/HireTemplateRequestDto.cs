namespace HireBot.Abstraction.Models.Hiring;

public sealed record HireTemplateRequestDto
{
    public string? TenantId { get; init; }

    public string? OperatorId { get; init; }

    public string? UseCase { get; init; }
}
