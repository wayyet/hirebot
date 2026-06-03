namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record CreatePrivateBranchRequestDto(
    string DisplayName,
    string? DisplayDescription,
    List<string> SelectedStations);
