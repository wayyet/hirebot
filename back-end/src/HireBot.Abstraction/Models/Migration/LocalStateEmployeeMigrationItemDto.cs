namespace HireBot.Abstraction.Models.Migration;

public sealed record LocalStateEmployeeMigrationItemDto(
    string EmployeeId,
    string Nickname,
    string RoleName,
    string SourceTemplate,
    string SourceTemplateId,
    string LifecycleStatus,
    string StageSummary,
    string PrimarySignal,
    string SignalLevel,
    string OwningTeam,
    string CreatedAt,
    string? InternshipStartAt,
    string? GraduatedAt,
    int TasksDone,
    int TasksTotal,
    IReadOnlyList<string> PendingActions,
    IReadOnlyList<string> CapabilityNames,
    bool IsConfigured);
