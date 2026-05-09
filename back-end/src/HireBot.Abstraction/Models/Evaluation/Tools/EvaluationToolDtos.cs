using System.ComponentModel.DataAnnotations;

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

public sealed record EvaluationTargetExecuteRequestDto
{
    [Required]
    public string TestcaseId { get; init; } = string.Empty;

    [Required]
    public string Input { get; init; } = string.Empty;
}

public sealed record EvaluationTargetBootstrapRequestDto
{
    public string? BackendId { get; init; }

    public string? SourceArtifactPath { get; init; }

    public bool ForceRecreate { get; init; }
}

public sealed record EvaluationTargetBootstrapResultDto(
    string EmployeeId,
    string BackendId,
    string TargetRuntimeId,
    string EvaluatorRuntimeId,
    string SessionId,
    string WorkspacePath,
    string SourceArtifactPath,
    string StartedAtUtc);

public sealed record EvaluationTargetExecuteResultDto(
    string SessionId,
    string ExecutionId,
    string TestcaseId,
    string Status,
    string StartedAtUtc,
    string CompletedAtUtc);

public sealed record EvaluationTraceReadRequestDto
{
    [Required]
    public string ExecutionId { get; init; } = string.Empty;

    [Required]
    public string TestcaseId { get; init; } = string.Empty;
}

public sealed record EvaluationTraceReadResultDto(
    string SessionId,
    string ExecutionId,
    string TestcaseId,
    string TraceJson,
    EvaluationAssetRefDto TraceAsset);

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
    string SessionId,
    string TargetHireId,
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
