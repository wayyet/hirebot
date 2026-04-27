using HireBot.Abstraction;
using HireBot.Abstraction.Models.Team;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Team;
using HireBot.Core.Services.Internal;

namespace HireBot.Core.Services.Team;

public sealed class TeamImService(
    ITeamImProvider teamImProvider,
    IRequestContextService requestContextService) : ITeamImService
{
    private static readonly HashSet<string> AllowedStatus =
        ["pending", "confirmed", "all"];

    public async Task<ApiResponse<IReadOnlyList<TeamImItemDto>>> GetItemsAsync(TeamImQueryDto query, CancellationToken cancellationToken = default)
    {
        query ??= new TeamImQueryDto();

        if (query.Page <= 0 || query.PageSize <= 0)
        {
            return ApiResponse<IReadOnlyList<TeamImItemDto>>.ErrorResponse(400, "page 与 pageSize 必须大于 0");
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? "pending" : query.Status.Trim().ToLowerInvariant();
        if (!AllowedStatus.Contains(status))
        {
            return ApiResponse<IReadOnlyList<TeamImItemDto>>.ErrorResponse(400, "status 仅支持 pending、confirmed、all");
        }

        var normalizedQuery = query with { Status = status };
        var owner = requestContextService.ResolveOwnerSubject();
        var items = await teamImProvider.GetItemsAsync(owner, normalizedQuery, cancellationToken);
        return ApiResponse<IReadOnlyList<TeamImItemDto>>.SuccessResponse(items);
    }

    public async Task<ApiResponse<TeamImItemDto>> ConfirmItemAsync(
        string itemId,
        ConfirmTeamImItemRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return ApiResponse<TeamImItemDto>.ErrorResponse(400, "itemId 不能为空");
        }

        request ??= new ConfirmTeamImItemRequestDto();

        var owner = requestContextService.ResolveOwnerSubject();
        var confirmed = await teamImProvider.ConfirmItemAsync(
            owner,
            itemId.Trim(),
            request.RequestId,
            owner,
            cancellationToken);

        if (confirmed is null)
        {
            return ApiResponse<TeamImItemDto>.ErrorResponse(404, "IM 信息不存在");
        }

        return ApiResponse<TeamImItemDto>.SuccessResponse(confirmed, "IM 信息已确认");
    }
}
