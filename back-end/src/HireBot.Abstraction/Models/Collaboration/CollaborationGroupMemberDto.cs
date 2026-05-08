namespace HireBot.Abstraction.Models.Collaboration;

public sealed record CollaborationGroupMemberDto(
    string Name,
    string Role,
    bool IsDigital,
    string JoinedAt,
    string LastActive);
