using HireBot.Abstraction.Models.Hiring;
using System.Text.Json;

namespace HireBot.Abstraction.Services.Hiring;

public interface IEmployeeHiringService
{
    Task<ApiResponse<HireTemplateResultDto>> HireAsync(string templateId, HireTemplateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringConversationControlResultDto>> PauseConversationAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<StartHiringConversationResultDto>> ResetConversationAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringConversationControlResultDto>> ResumeConversationAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(string hireId, HiringConversationMessageRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringConversationResultDto>> SyncConversationTurnAsync(string hireId, HiringConversationSyncRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(string hireId, string? stage, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(string hireId, HiringAuditDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(string hireId, CancellationToken cancellationToken = default);
    /// <summary>
    /// 前端从沙箱网关直接下载产物包后，调用此接口将包上传至后端，跳过 KingCrab 依赖。
    /// </summary>
    /// <param name="linkedStoreSkillIds">用户在前端 TODO 面板关联的 store skill UUID 列表；后端会从 ncrew-builder 下载并合并到最终产物。</param>
    Task<ApiResponse<HiringFinalizeResultDto>> ImportPackageAsync(string hireId, Stream packageStream, string fileName, IReadOnlyList<string>? linkedStoreSkillIds = null, CancellationToken cancellationToken = default);
    Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(string hireId, CancellationToken cancellationToken = default);
    Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default);

    /// <summary>保存前端对话状态缓存（messages + stageOverrides），用于刷新页面后恢复。</summary>
    Task<ApiResponse<bool>> SaveConversationCacheAsync(string hireId, JsonElement cache, CancellationToken cancellationToken = default);

    /// <summary>获取前端对话状态缓存，返回存储的 JSON 对象。</summary>
    Task<ApiResponse<JsonElement?>> GetConversationCacheAsync(string hireId, CancellationToken cancellationToken = default);
}
