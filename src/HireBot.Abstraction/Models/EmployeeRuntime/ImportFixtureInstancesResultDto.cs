namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record ImportFixtureInstancesResultDto(
    string OwnerSubject,
    int FixtureDirectories,
    int ImportedEmployees,
    int ImportedImItems,
    IReadOnlyList<string> EmployeeIds);
