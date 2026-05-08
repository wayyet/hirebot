namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record CreatePersonalCloneRequestDto(
    string DisplayName,
    string? DisplayAvatar,
    string? DisplayDescription);
