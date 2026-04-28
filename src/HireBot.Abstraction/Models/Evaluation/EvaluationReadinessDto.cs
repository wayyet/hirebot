namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationReadinessDto(
    bool TestcasesReady,
    bool OntologyReady,
    string Status,
    string? Message);
