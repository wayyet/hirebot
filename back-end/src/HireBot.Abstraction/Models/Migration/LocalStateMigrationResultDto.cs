namespace HireBot.Abstraction.Models.Migration;

public sealed record LocalStateMigrationResultDto(
    int ImportedEmployees,
    int SkippedEmployees);
