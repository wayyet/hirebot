using HireBot.Abstraction.Models.Hiring;
using System.Text.Json;

namespace HireBot.Abstraction.Services.Hiring;

public interface IEmployeeHiringService
{
    Task<ApiResponse<HireTemplateResultDto>> HireAsync(string templateId, string? useCase = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(string hireId, string? stage, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(string hireId, HiringAuditDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringExternalSystemConfigDto>> GetExternalSystemConfigAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringExternalSystemConfigDto>> SaveExternalSystemConfigAsync(string hireId, HiringExternalSystemConfigDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringSkillLinkConfigDto>> GetSkillLinkConfigAsync(string hireId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HiringSkillLinkConfigDto>> SaveSkillLinkConfigAsync(string hireId, HiringSkillLinkConfigDto request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 同步对话轮次，解析 AI 回复中的结构化数据标签并保存。
    /// </summary>
    Task<ApiResponse<HiringConversationSyncResultDto>> SyncConversationTurnAsync(string hireId, HiringConversationSyncRequestDto request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取已收集的结构化数据。
    /// </summary>
    Task<ApiResponse<Dictionary<string, string>>> GetStructuredDataAsync(string hireId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按阶段保存运行时状态（阶段覆盖配置 + 下游运行记录）。
    /// </summary>
    Task<ApiResponse<bool>> SaveRuntimeStateByStageAsync(string hireId, string stage, SaveRuntimeStateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按阶段获取运行时状态（阶段覆盖配置 + 下游运行记录）。
    /// </summary>
    Task<ApiResponse<RuntimeStateDto>> GetRuntimeStateByStageAsync(string hireId, string stage, CancellationToken cancellationToken = default);
    /// <summary>
    /// 前端从沙箱网关直接下载产物包后，调用此接口将包上传至后端，跳过 KingCrab 依赖。
    /// </summary>
    /// <param name="linkedStoreSkillIds">用户在前端 TODO 面板关联的 store skill UUID 列表；后端会从 ncrew-builder 下载并合并到最终产物。</param>
    Task<ApiResponse<HiringFinalizeResultDto>> ImportPackageAsync(string hireId, Stream packageStream, string fileName, IReadOnlyList<string>? linkedStoreSkillIds = null, CancellationToken cancellationToken = default);
    Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(string hireId, CancellationToken cancellationToken = default);
    Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 前端将模板包 ZIP 直接上传到指定雇佣会话的沙箱工作区，
    /// 返回沙箱内文件路径和可嵌入 WS 消息的 [FILE_URL:...] 标记。
    /// </summary>
    Task<ApiResponse<HiringTemplatePackageUploadResultDto>> UploadTemplatePackageFromClientAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
