namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record InstanceChatResultDto(
    string InstanceId,
    string ConversationId,
    InstanceChatMessageDto AssistantMessage);
