namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationSyncRequestDto
{
    public string UserMessage { get; init; } = string.Empty;

    public string AssistantReply { get; init; } = string.Empty;

    public IReadOnlyList<HiringConversationMaterialDto>? Materials { get; init; }

    public IReadOnlyList<HiringConversationToolCallDto>? ToolCalls { get; init; }
}

public sealed record HiringConversationToolCallDto
{
    public string ToolName { get; init; } = string.Empty;

    public string? Arguments { get; init; }

    public string? Result { get; init; }
}
