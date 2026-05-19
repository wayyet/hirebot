using HireBot.Abstraction.Models.Evaluation;

namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record EmployeeDetailDto(
    string EmployeeId,
    string Nickname,
    string RoleName,
    string SourceTemplate,
    string SourceTemplateId,
    string InstanceType,
    string Status,
    string? BasedOnTemplateId,
    string? FromInstanceId,
    string OwnerUserId,
    string DepartmentId,
    string LifecycleStatus,
    string StageSummary,
    string PrimarySignal,
    string SignalLevel,
    string OwningTeam,
    string CreatedAt,
    string? InternshipStartAt,
    string? GraduatedAt,
    int TasksDone,
    int TasksTotal,
    decimal? SatisfactionScore,
    IReadOnlyList<string> PendingActions,
    IReadOnlyList<EmployeeCapabilityDto> Capabilities,
    string? EvalPhase,
    int? EvalIteration,
    int? EvalMaxIterations,
    bool IsConfigured,
    string? CardIntro = null,
    /// <summary>
    /// 最新一次评估报告摘要，仅在员工详情接口中实时查询并填充，不持久化到快照 JSON。
    /// </summary>
    EvaluationReportSummaryDto? LatestReport = null);
