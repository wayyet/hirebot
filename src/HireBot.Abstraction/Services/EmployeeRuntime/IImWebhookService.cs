using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IImWebhookService
{
    Task<ApiResponse<ImWebhookHandleResultDto>> VerifyAsync(
        string platform,
        string instanceId,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<ImWebhookHandleResultDto>> HandleAsync(
        string platform,
        string instanceId,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<string?>> ExtractFeishuUrlVerificationChallengeAsync(
        string instanceId,
        string payload,
        CancellationToken cancellationToken = default);
}

