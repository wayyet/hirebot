namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record QuickCloneResultDto(
    string NewInstanceId,
    string Status,
    string FromInstanceId,
    bool ViaQuickClone);
