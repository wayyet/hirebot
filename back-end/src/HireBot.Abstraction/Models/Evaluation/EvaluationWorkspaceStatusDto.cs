namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationWorkspaceStepDto(
    string Step,
    string Status,
    string? Detail);

public sealed record EvaluationWorkspaceStatusDto(
    string EmployeeId,
    string OverallStatus,
    string? TargetSandboxId,
    string? EvaluatorSandboxId,
    string? EvaluatorRuntimeId,
    string? TargetRuntimeId,
    IReadOnlyList<EvaluationWorkspaceStepDto> Steps,
    string? ErrorMessage);
