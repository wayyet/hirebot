namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationMessageRequestDto
{
    public string Content { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string>? StructuredAnswers { get; init; }

    public IReadOnlyList<HiringConversationMaterialDto>? Materials { get; init; }
}
