using HireBot.Abstraction.Models.Collaboration;

namespace HireBot.Abstraction.Providers;

public interface ICollaborationProvider
{
    Task<IReadOnlyList<CollaborationGroupSummaryDto>> GetGroupsAsync(string ownerSubject, bool includeArchived, CancellationToken cancellationToken = default);
    Task<CollaborationGroupDetailDto?> GetGroupAsync(string ownerSubject, string groupId, CancellationToken cancellationToken = default);
    Task<CollaborationGroupDetailDto?> SetArchivedAsync(string ownerSubject, string groupId, bool archived, CancellationToken cancellationToken = default);
    Task<int> MarkArchivedAsync(string ownerSubject, IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default);
}
