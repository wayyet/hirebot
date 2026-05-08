using HireBot.Abstraction.Models.Team;

namespace HireBot.Abstraction.Providers;

public interface ITeamImProvider
{
    Task<IReadOnlyList<TeamImItemDto>> GetItemsAsync(string ownerSubject, TeamImQueryDto query, CancellationToken cancellationToken = default);
    Task<TeamImItemDto?> ConfirmItemAsync(string ownerSubject, string itemId, string? requestId, string actor, CancellationToken cancellationToken = default);
    Task<int> ReplaceItemsAsync(string ownerSubject, IReadOnlyList<TeamImItemDto> items, CancellationToken cancellationToken = default);
}
