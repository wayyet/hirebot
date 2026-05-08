namespace HireBot.Abstraction.Models.Team;

public sealed record TeamImQueryDto
{
    public string? EmployeeId { get; init; }

    public string? Category { get; init; }

    public string? Status { get; init; }

    public string? Source { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
