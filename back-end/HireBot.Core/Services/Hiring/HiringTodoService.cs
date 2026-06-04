using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣 TODO 服务（简化版：从沙箱实时读取 handoff items，不再依赖 RuntimeStore）。
/// </summary>
internal sealed class HiringTodoService(
    HireBotDbContext dbContext,
    ILogger<HiringTodoService> logger) : IHiringTodoService
{
    public async Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosAsync(
        string sessionId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(400, "sessionId 不能为空");

        var session = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (session is null)
            return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(404, $"找不到会话 {sessionId}");

        // TODO: 从沙箱 artifacts 实时解析 handoff.json（暂时返回空列表）
        logger.LogWarning("GetTodosAsync: TODO 功能暂未实现沙箱 artifact 解析，返回空列表");
        return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse([], "获取成功");
    }

    public Task<ApiResponse<HiringWorkflowHandoffDto>> UpsertTodoAsync(
        string sessionId,
        string requestingUserId,
        UpsertHiringTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现基于沙箱的 TODO 更新逻辑
        logger.LogWarning("UpsertTodoAsync: TODO 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(501, "TODO 功能暂未实现"));
    }

    public async Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosByHireIdAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
            return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(400, "hireId 不能为空");

        var session = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (session is null)
            return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse([], "暂无数据");

        // TODO: 从沙箱 artifacts 实时解析 handoff.json
        logger.LogWarning("GetTodosByHireIdAsync: TODO 功能暂未实现沙箱 artifact 解析，返回空列表");
        return ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse([], "获取成功");
    }

    public Task<ApiResponse<HiringWorkflowHandoffDto>> UpdateTodoStatusAsync(
        string hireId,
        string handoffId,
        string status,
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现基于沙箱的 TODO 状态更新
        logger.LogWarning("UpdateTodoStatusAsync: TODO 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(501, "TODO 功能暂未实现"));
    }
}
