namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringCredentialBindingRequestDto
{
    public string CredentialSlot { get; init; } = string.Empty;

    public string SecretValue { get; init; } = string.Empty;

    public string? SecretRef { get; init; }

    public string? AuthKind { get; init; }

    public string? TargetSystem { get; init; }

    public string? TodoId { get; init; }
}
