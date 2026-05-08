using HireBot.Abstraction.Models.Collaboration;

namespace HireBot.Abstraction.Services.Collaboration;

public interface ICollaborationService
{
    Task<ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>> GetGroupsAsync(bool includeArchived, CancellationToken cancellationToken = default);
    Task<ApiResponse<CollaborationGroupDetailDto>> GetGroupAsync(string groupId, CancellationToken cancellationToken = default);
    Task<ApiResponse<CollaborationGroupDetailDto>> SetArchivedAsync(string groupId, bool archived, CancellationToken cancellationToken = default);
    Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default);
}
