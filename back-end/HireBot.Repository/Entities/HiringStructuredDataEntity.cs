using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

/// <summary>
/// 雇佣流程中收集到的结构化数据（键值对存储，每个字段一行）。
/// </summary>
public sealed class HiringStructuredDataEntity : ITenant
{
    /// <summary>
    /// 主键（自增）。
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 雇佣流程唯一标识。
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string HireId { get; set; }

    /// <summary>
    /// 租户标识。
    /// </summary>
    [MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>
    /// 字段键（例如：candidate.name, candidate.skills, job.title）。
    /// </summary>
    [Required]
    [MaxLength(256)]
    public required string FieldKey { get; set; }

    /// <summary>
    /// 字段值（JSON 或纯文本）。
    /// </summary>
    public string? FieldValue { get; set; }

    /// <summary>
    /// 数据收集时间（UTC）。
    /// </summary>
    public DateTimeOffset CollectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
