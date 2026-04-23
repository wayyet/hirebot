namespace HireBot.Abstraction.Models.Collaboration;

public sealed record ArchiveCollaborationGroupRequestDto
{
    public bool Archived { get; init; } = true;
}
