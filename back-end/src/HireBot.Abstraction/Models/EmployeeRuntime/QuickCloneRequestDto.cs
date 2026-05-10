namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record QuickCloneRequestDto(
    string DisplayName,
    string UserRole,
    string? DisplayAvatar,
    string? DisplayDescription);
