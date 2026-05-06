using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例聊天服务，封装站内聊天功能。
/// </summary>
public sealed class InstanceChatService(IInstanceRuntimeConversationService runtimeConversationService) : IInstanceChatService
{
    private const string InAppChannel = "inapp";

    /// <summary>
    /// 获取实例的聊天消息列表。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天时间线</returns>
    public async Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.GetMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 发送消息给实例。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="request">消息请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天结果</returns>
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

    /// <summary>
    /// 清空实例的聊天消息。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.ClearMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }
}