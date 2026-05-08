namespace HireBot.Abstraction.Models.Migration;

public sealed record LocalStateMigrationRequestDto
{
    public IReadOnlyList<LocalStateEmployeeMigrationItemDto>? Employees { get; init; }

    public IReadOnlyList<string>? ArchivedGroupIds { get; init; }
}
