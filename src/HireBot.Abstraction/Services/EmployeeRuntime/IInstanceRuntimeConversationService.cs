using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IInstanceRuntimeConversationService
{
    Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        string channel,
        string? ownerUserId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
        string instanceId,
        string channel,
        string content,
        string? ownerUserId = null,
        string? externalMessageId = null,
        string? externalUserId = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        string channel,
        string? ownerUserId = null,
        CancellationToken cancellationToken = default);
}

