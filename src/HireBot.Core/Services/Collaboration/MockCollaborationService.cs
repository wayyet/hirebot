using HireBot.Abstraction;
using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Core.Services.Internal;

namespace HireBot.Core.Services.Collaboration;

public sealed class MockCollaborationService(
    ICollaborationProvider collaborationProvider,
    IRequestContextService requestContextService) : ICollaborationService
{
    public async Task<ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>> GetGroupsAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        var groups = await collaborationProvider.GetGroupsAsync(owner, includeArchived, cancellationToken);
        return ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>.SuccessResponse(groups);
    }

    public async Task<ApiResponse<CollaborationGroupDetailDto>> GetGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(400, "groupId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var group = await collaborationProvider.GetGroupAsync(owner, groupId.Trim(), cancellationToken);
        if (group is null)
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(404, "协作群不存在");
        }

        return ApiResponse<CollaborationGroupDetailDto>.SuccessResponse(group);
    }

    public async Task<ApiResponse<CollaborationGroupDetailDto>> SetArchivedAsync(
        string groupId,
        bool archived,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(400, "groupId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var updated = await collaborationProvider.SetArchivedAsync(owner, groupId.Trim(), archived, cancellationToken);
        if (updated is null)
        {
            return ApiResponse<CollaborationGroupDetailDto>.ErrorResponse(404, "协作群不存在");
        }

        return ApiResponse<CollaborationGroupDetailDto>.SuccessResponse(updated, archived ? "协作群已归档" : "协作群已恢复");
    }

    public Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        return collaborationProvider.MarkArchivedAsync(owner, groupIds, cancellationToken);
    }
}
