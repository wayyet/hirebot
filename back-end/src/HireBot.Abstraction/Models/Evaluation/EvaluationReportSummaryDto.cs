using HireBot.Abstraction.Models.Evaluation.Tools;

namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationReportSummaryDto(
    string ReportId,
    int Iteration,
    decimal OverallScore,
    bool Passed,
    string ReportJsonUrl,
    string? ReportHtmlUrl,
    string CreatedAtUtc,
    IReadOnlyList<EvaluationDimensionScoreDto>? DimensionScores = null);
