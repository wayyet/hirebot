namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record InstanceChatMessageDto(
    string MessageId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
