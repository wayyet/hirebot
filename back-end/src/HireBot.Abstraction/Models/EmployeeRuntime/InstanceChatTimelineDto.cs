namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record InstanceChatTimelineDto(
    string InstanceId,
    string ConversationId,
    IReadOnlyList<InstanceChatMessageDto> Messages);
