namespace HireBot.Abstraction.Models.Team;

public sealed record TeamImItemDto(
    string ItemId,
    string EmployeeId,
    string EmployeeName,
    string Category,
    string Content,
    string Source,
    string ReceivedAt,
    string Status,
    string? ConfirmedAt);
