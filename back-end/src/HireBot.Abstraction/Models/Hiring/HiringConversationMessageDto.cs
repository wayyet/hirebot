namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationMessageDto(
    string MessageId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
