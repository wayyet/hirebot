using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Models.Evaluation;

namespace HireBot.Abstraction.Models.Evaluation.Tools;

public sealed record EvaluationTestcaseDto(
    string TestcaseId,
    string ScenarioName,
    string SourceFile,
    string SourcePath,
    string RawJson,
    IReadOnlyList<string> ExpectedSteps);

public sealed record EvaluationFetchTestcasesResultDto(
    string SessionId,
    string TargetHireId,
    string TargetRuntimeId,
    IReadOnlyList<EvaluationTestcaseDto> Testcases,
    IReadOnlyList<EvaluationQuestionCardDto> QuestionCards,
    IReadOnlyList<EvaluationAssetRefDto> Assets);

public sealed record EvaluationOntologyQueryResultDto(
    string SessionId,
    IReadOnlyDictionary<string, decimal> DimensionWeights,
    IReadOnlyList<string> DimensionRules,
    IReadOnlyList<EvaluationAssetRefDto> Assets);

public sealed record EvaluationDimensionScoreDto(
    string Dimension,
    decimal Score,
    string Comment,
    IReadOnlyList<string> EvidenceRefs);

public sealed record EvaluationReportUpsertRequestDto
{
    [Required]
    public string SessionId { get; init; } = string.Empty;

    [Range(0, 100)]
    public decimal OverallScore { get; init; }

    public bool Passed { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<EvaluationDimensionScoreDto> DimensionScores { get; init; } = [];
}

public sealed record EvaluationSandboxConnectionResultDto(
    string GatewayEndpoint,
    string SandboxToken,
    string EvaluatorSandboxId,
    string TargetSandboxId,
    string SessionId,
    string TargetHireId,
    string TargetGatewayEndpoint,
    string? EvaluationPayloadJson);

public sealed record EvaluationVerdictSyncRequestDto
{
    [Required]
    public string SessionId { get; init; } = string.Empty;

    [Required]
    public EvaluationVerdictPayloadDto Verdict { get; init; } = null!;
}

public sealed record EvaluationVerdictPayloadDto(
    string Verdict,
    decimal OverallScore,
    string Summary,
    IReadOnlyList<EvaluationDimensionScoreDto> DimensionScores);

public sealed record EvaluationVerdictSyncResultDto(
    string EmployeeId,
    string SessionId,
    bool Passed,
    decimal OverallScore,
    string Summary,
    string Status,
    EvaluationReportSummaryDto? LatestReport);

public sealed record EvaluationReportUpsertResultDto(
    string SessionId,
    string ReportId,
    int Iteration,
    decimal OverallScore,
    bool Passed,
    string ReportJsonUrl,
    string? ReportHtmlUrl,
    IReadOnlyList<EvaluationAssetRefDto> Assets);

public sealed record EvaluationTraceSyncRequestDto
{
    [Required]
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// evaluate.py --mode execute 输出的 trace_result.json 完整内容（JSON 字符串）。
    /// </summary>
    [Required]
    public string TraceJson { get; init; } = string.Empty;
}

public sealed record EvaluationTraceSyncResultDto(
    string SessionId,
    string AssetId,
    string TraceJsonUrl);

/// <summary>
/// 执行轨迹内容查询结果。TraceJsonContent 为 trace_result.json 的原始 JSON 字符串。
/// </summary>
public sealed record EvaluationTraceContentDto(
    string SessionId,
    string AssetId,
    string TraceJsonUrl,
    string TraceJsonContent);
