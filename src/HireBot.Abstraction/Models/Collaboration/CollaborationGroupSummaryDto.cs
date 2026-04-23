namespace HireBot.Abstraction.Models.Collaboration;

public sealed record CollaborationGroupSummaryDto(
    string GroupId,
    string GroupName,
    string BusinessPurpose,
    string ImPlatform,
    string ImGroupId,
    int MemberCount,
    int DigitalEmployeeCount,
    string RecentActivityTime,
    int CollaborationVolume7d,
    string Status,
    string PrimarySignal,
    bool IsArchived);
