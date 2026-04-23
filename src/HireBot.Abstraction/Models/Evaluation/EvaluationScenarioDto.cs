namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationScenarioDto(
    string ScenarioId,
    string ScenarioName,
    string Status,
    string? Verdict,
    string? VerdictComment,
    int MessageCount,
    string StartedAt,
    string? CompletedAt);
