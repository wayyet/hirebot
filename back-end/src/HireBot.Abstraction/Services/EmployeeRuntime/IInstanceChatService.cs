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

    Task<ApiResponse<ImConfigResultDto>> UpdateFeishuChannelConfigAsync(
        string instanceId,
        ImConfigRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<FeishuChannelEffectiveConfigDto>> GetFeishuChannelEffectiveConfigAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ClearFeishuChannelOverrideAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ImConfigResultDto>> UpdateDingTalkChannelConfigAsync(
        string instanceId,
        DingTalkChannelConfig request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<DingTalkChannelConfig>> GetDingTalkChannelEffectiveConfigAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ClearDingTalkChannelOverrideAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

}
