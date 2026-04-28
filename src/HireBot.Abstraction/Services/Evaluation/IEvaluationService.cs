using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;

namespace HireBot.Abstraction.Services.Evaluation;

public interface IEvaluationService
{
    Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationSandboxConversationStateDto>> GetEvaluationSandboxConversationAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationSandboxConversationStateDto>> SendEvaluationSandboxMessageAsync(string employeeId, EvaluationSandboxMessageRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> SubmitAiEvaluationDecisionAsync(string employeeId, AiEvaluationDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(string employeeId, EvaluationOnboardingDecisionRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<EvaluationFetchTestcasesResultDto>> FetchTestcasesAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationOntologyQueryResultDto>> QueryOntologyAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationTargetBootstrapResultDto>> BootstrapTargetSandboxAsync(string employeeId, EvaluationTargetBootstrapRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationTargetExecuteResultDto>> ExecuteTargetAsync(string employeeId, EvaluationTargetExecuteRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationTraceReadResultDto>> ReadTraceAsync(string employeeId, EvaluationTraceReadRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EvaluationReportUpsertResultDto>> UpsertReportAsync(string employeeId, EvaluationReportUpsertRequestDto request, CancellationToken cancellationToken = default);
}
