using HireBot.Abstraction.Models.Team;

namespace HireBot.Abstraction.Services.Team;

public interface ITeamImService
{
    Task<ApiResponse<IReadOnlyList<TeamImItemDto>>> GetItemsAsync(TeamImQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<TeamImItemDto>> ConfirmItemAsync(string itemId, ConfirmTeamImItemRequestDto request, CancellationToken cancellationToken = default);
}
