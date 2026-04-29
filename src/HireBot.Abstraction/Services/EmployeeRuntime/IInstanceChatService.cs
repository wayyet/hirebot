using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IInstanceChatService
{
    Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
        string instanceId,
        SendInstanceChatMessageRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}
