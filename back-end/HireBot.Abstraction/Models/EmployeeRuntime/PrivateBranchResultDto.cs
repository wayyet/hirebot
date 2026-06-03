namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record PrivateBranchResultDto(
    string BranchId,
    string DisplayName,
    string Status,
    string FromInstanceId,
    bool ImRoutingSwitched);
