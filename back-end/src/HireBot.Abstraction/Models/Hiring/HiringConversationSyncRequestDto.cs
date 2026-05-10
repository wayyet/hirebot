namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationSyncRequestDto
{
    public string UserMessage { get; init; } = string.Empty;

    public string AssistantReply { get; init; } = string.Empty;

    public IReadOnlyList<HiringConversationMaterialDto>? Materials { get; init; }
}
