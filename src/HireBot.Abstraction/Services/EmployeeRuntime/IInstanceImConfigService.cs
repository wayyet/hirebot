using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IInstanceImConfigService
{
    Task<ApiResponse<ImWebhookUrlDto>> GetWebhookUrlAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ImConfigResultDto>> UpsertConfigAsync(
        string instanceId,
        string platform,
        ImConfigRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ImConfigStatusDto>> GetConfigsAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteConfigAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default);
}

