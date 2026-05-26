using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;

namespace HireBot.Abstraction.Services.Evaluation;

public interface IEvaluationService
{
    Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationSandboxConversationStateDto>> GetEvaluationSandboxConversationAsync(string employeeId, string? since = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationSandboxConversationStateDto>> SendEvaluationSandboxMessageAsync(string employeeId, EvaluationSandboxMessageRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> StartAiEvaluationAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(string employeeId, EvaluationOnboardingDecisionRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<EvaluationSandboxConnectionResultDto>> GetSandboxConnectionAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationVerdictSyncResultDto>> SyncVerdictAsync(string employeeId, EvaluationVerdictSyncRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationTraceSyncResultDto>> SyncTraceAsync(string employeeId, EvaluationTraceSyncRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationTraceContentDto>> GetTraceContentAsync(string employeeId, string sessionId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationWorkspaceStatusDto>> GetWorkspaceStatusAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> ResetEvaluationDataAsync(string employeeId, CancellationToken cancellationToken = default);
}
