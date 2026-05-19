namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationStateDto(
    string EmployeeId,
    string OverallStatus,
    IReadOnlyList<EvaluationScenarioDto> Scenarios,
    string Recommendation,
    string? SessionId = null,
    EvaluationReadinessDto? Readiness = null,
    IReadOnlyList<EvaluationQuestionCardDto>? QuestionCards = null,
    EvaluationReportSummaryDto? LatestReport = null,
    IReadOnlyList<EvaluationAssetRefDto>? AssetRefs = null,
    IReadOnlyList<EvaluationTestcaseOutlineDto>? TestcaseOutlines = null);
