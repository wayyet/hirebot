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

    /// <summary>获取评估报告文件（JSON 或 HTML），返回物理路径和 MIME 类型供控制器流式下载。</summary>
    /// <param name="employeeId">候选人 ID</param>
    /// <param name="reportId">报告 ID（EvaluationReportEntity.Id 的 N 格式）</param>
    /// <param name="fileType">文件类型：json 或 html</param>
    Task<ApiResponse<EvaluationReportFileDto>> GetReportFileAsync(
        string employeeId,
        string reportId,
        string fileType,
        CancellationToken cancellationToken = default);
}
