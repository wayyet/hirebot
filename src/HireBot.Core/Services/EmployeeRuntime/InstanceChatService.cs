using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class InstanceChatService(IInstanceRuntimeConversationService runtimeConversationService) : IInstanceChatService
{
    private const string InAppChannel = "inapp";

    public async Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.GetMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }

    public async Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
        string instanceId,
        SendInstanceChatMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(400, "content 不能为空");
        }

        return await runtimeConversationService.SendMessageAsync(
            instanceId,
            InAppChannel,
            request.Content,
            cancellationToken: cancellationToken);
    }

    public async Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.ClearMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }
}
