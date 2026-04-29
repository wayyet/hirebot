using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IImWebhookService
{
    Task<ApiResponse<ImWebhookHandleResultDto>> HandleAsync(
        string platform,
        string instanceId,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}

