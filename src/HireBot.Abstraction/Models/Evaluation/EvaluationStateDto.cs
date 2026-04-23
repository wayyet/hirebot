namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationStateDto(
    string EmployeeId,
    string OverallStatus,
    IReadOnlyList<EvaluationScenarioDto> Scenarios,
    string Recommendation);
