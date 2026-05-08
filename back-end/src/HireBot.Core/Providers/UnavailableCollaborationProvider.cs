using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class UnavailableCollaborationProvider : ICollaborationProvider
{
    private const string Message = "协作群能力未接入真实数据源，Mock 数据已移除。";

    public Task<IReadOnlyList<CollaborationGroupSummaryDto>> GetGroupsAsync(
        string ownerSubject,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }

    public Task<CollaborationGroupDetailDto?> GetGroupAsync(
        string ownerSubject,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }

    public Task<CollaborationGroupDetailDto?> SetArchivedAsync(
        string ownerSubject,
        string groupId,
        bool archived,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }

    public Task<int> MarkArchivedAsync(
        string ownerSubject,
        IReadOnlyList<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }
}
