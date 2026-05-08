using HireBot.Abstraction;
using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Core.Services.Internal;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Collaboration;

/// <summary>
/// 协作群组服务，提供协作群组的查询和管理功能。
/// </summary>
public sealed class CollaborationService(
    ICollaborationProvider collaborationProvider,
    IRequestContextService requestContextService,
    ILogger<CollaborationService> logger) : ICollaborationService
{
    /// <summary>
    /// 获取协作群组列表。
    /// </summary>
    /// <param name="includeArchived">是否包含已归档的群组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>群组摘要列表</returns>
    public async Task<ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>> GetGroupsAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var owner = requestContextService.ResolveOwnerSubject();
            var groups = await collaborationProvider.GetGroupsAsync(owner, includeArchived, cancellationToken);
            return ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>.SuccessResponse(groups);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Collaboration group list unavailable from upstream provider.");
            return ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>.ErrorResponse(501, ex.Message);
        }
    }

    /// <summary>
    /// 获取指定协作群组的详细信息。
    /// </summary>
    /// <param name="groupId">群组标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>群组详细信息</returns>
    public async Task<ApiResponse<CollaborationGroupDetailDto>> GetGroupAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(400, "groupId 不能为空");
        }

        try
        {
            var owner = requestContextService.ResolveOwnerSubject();
            var group = await collaborationProvider.GetGroupAsync(owner, groupId.Trim(), cancellationToken);
            if (group is null)
            {
                return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(404, "协作群不存在");
            }

            return ApiResponse<CollaborationGroupDetailDto>.SuccessResponse(group);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Collaboration group detail unavailable from upstream provider. GroupId={GroupId}", groupId);
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(501, ex.Message);
        }
    }

    /// <summary>
    /// 设置协作群组的归档状态。
    /// </summary>
    /// <param name="groupId">群组标识</param>
    /// <param name="archived">归档状态（true=归档，false=恢复）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的群组详细信息</returns>
    public async Task<ApiResponse<CollaborationGroupDetailDto>> SetArchivedAsync(
        string groupId,
        bool archived,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(400, "groupId 不能为空");
        }

        try
        {
            var owner = requestContextService.ResolveOwnerSubject();
            var updated = await collaborationProvider.SetArchivedAsync(owner, groupId.Trim(), archived, cancellationToken);
            if (updated is null)
            {
                return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(404, "协作群不存在");
            }

            return ApiResponse<CollaborationGroupDetailDto>.SuccessResponse(
                updated,
                archived ? "协作群已归档" : "协作群已恢复");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Collaboration archive operation unavailable from upstream provider. GroupId={GroupId}", groupId);
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(501, ex.Message);
        }
    }

    /// <summary>
    /// 批量标记群组为已归档状态。
    /// </summary>
    /// <param name="groupIds">群组标识列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功标记的群组数量</returns>
    public Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        return collaborationProvider.MarkArchivedAsync(owner, groupIds, cancellationToken);
    }
}
