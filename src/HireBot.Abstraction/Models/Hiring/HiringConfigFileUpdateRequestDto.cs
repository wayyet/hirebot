namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConfigFileUpdateRequestDto
{
    public string Content { get; init; } = string.Empty;

    public string? Summary { get; init; }
}
