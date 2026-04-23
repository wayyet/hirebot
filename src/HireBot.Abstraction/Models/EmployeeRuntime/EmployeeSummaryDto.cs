namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record EmployeeSummaryDto(
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
    int TasksDone,
    int TasksTotal,
    IReadOnlyList<string> PendingActions,
    bool IsConfigured);
