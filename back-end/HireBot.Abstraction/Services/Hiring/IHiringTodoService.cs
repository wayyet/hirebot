using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Services.Hiring;

/// <summary>
/// 雇佣 TODO（Handoff）事项管理服务。
/// 供 MCP 工具层调用，提供 TODO 查询和保存能力。
/// 通过 sessionId（Kingcrab 会话 ID）定位雇佣上下文，无需调用方传入 hireId。
/// </summary>
public interface IHiringTodoService
{
    /// <summary>
    /// 通过 Kingcrab sessionId 获取该雇佣会话的所有 TODO 事项（handoff items）。
    /// </summary>
    /// <param name="sessionId">Kingcrab 会话 ID（来自 _meta.sessionId）。</param>
    /// <param name="requestingUserId">发起请求的用户 ID（来自 JWT sub），用于权限校验。</param>
    Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosAsync(
        string sessionId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建或更新一个 TODO 事项。handoffId 相同则覆盖更新，否则新建。
    /// </summary>
    /// <param name="sessionId">Kingcrab 会话 ID（来自 _meta.sessionId）。</param>
    /// <param name="requestingUserId">发起请求的用户 ID（来自 JWT sub），用于权限校验。</param>
    Task<ApiResponse<HiringWorkflowHandoffDto>> UpsertTodoAsync(
        string sessionId,
        string requestingUserId,
        UpsertHiringTodoRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过 hireId 获取该雇佣流程的所有 TODO 事项（供 REST 端点调用）。
    /// </summary>
    Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosByHireIdAsync(
        string hireId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新指定 TODO 事项的状态（供用户在前端确认/撤销操作时调用）。
    /// </summary>
    Task<ApiResponse<HiringWorkflowHandoffDto>> UpdateTodoStatusAsync(
        string hireId,
        string handoffId,
        string status,
        CancellationToken cancellationToken = default);
}
