using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

/// <summary>
/// 雇佣流程阶段推进状态（每个 hire 一条记录）。
/// </summary>
public sealed class HiringStageProgressEntity : ITenant
{
    /// <summary>
    /// 雇佣流程唯一标识（对应 HiringSessionEntity.HireId）。
    /// </summary>
    [Key]
    [MaxLength(64)]
    public required string HireId { get; set; }

    /// <summary>
    /// 租户标识。
    /// </summary>
    [MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>
    /// 当前所处阶段：material | skill | external | ready_for_packaging。
    /// </summary>
    [Required]
    [MaxLength(40)]
    public required string CurrentStage { get; set; }

    /// <summary>
    /// 测试用例打包状态：not_asked | generating | generated | null（未启用）。
    /// </summary>
    [MaxLength(40)]
    public string? PackagingTestCasesStatus { get; set; }

    /// <summary>
    /// 阶段覆盖配置（用户手动修改的阶段配置，JSON 格式）。
    /// 格式：{ "goal_collection": {...}, "skill_design": {...} }
    /// </summary>
    public string? StageOverridesJson { get; set; }

    /// <summary>
    /// 下游运行记录（外部系统调用记录，JSON 格式）。
    /// 格式：{ "runId1": { "status": "success", "result": {...} }, "runId2": {...} }
    /// </summary>
    public string? DownstreamRunsJson { get; set; }

    /// <summary>    /// 对话上传文件列表（仅元数据，JSON 格式）。
    /// </summary>
    public string? UploadedFilesJson { get; set; }

    /// <summary>
    /// 最新产物包结构（文件名 + 包内文件列表，JSON 格式）。
    /// </summary>
    public string? PackageStructureJson { get; set; }

    /// <summary>    /// 最后更新时间（UTC）。
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 更新操作人（OwnerSubject 或系统标识）。
    /// </summary>
    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}
