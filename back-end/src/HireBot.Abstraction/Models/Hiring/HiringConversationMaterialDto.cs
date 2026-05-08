namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationMaterialDto
{
    public string Type { get; init; } = "text";

    public string Name { get; init; } = string.Empty;

    public string? Content { get; init; }

    public string? ContentHash { get; init; }

    public long? Size { get; init; }

    public string? MimeType { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
