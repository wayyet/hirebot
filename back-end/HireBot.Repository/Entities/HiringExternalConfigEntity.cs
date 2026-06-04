using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

/// <summary>
/// 雇佣流程关联的外部系统配置（飞书/钉钉/企业微信等，每个 hire 一条记录）。
/// </summary>
public sealed class HiringExternalConfigEntity : ITenant
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
    /// 外部系统配置 JSON（加密后的敏感信息）。
    /// </summary>
    [Required]
    public required string ConfigJson { get; set; } = "{}";

    /// <summary>
    /// 最后更新时间（UTC）。
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 更新操作人（OwnerSubject 或系统标识）。
    /// </summary>
    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}
