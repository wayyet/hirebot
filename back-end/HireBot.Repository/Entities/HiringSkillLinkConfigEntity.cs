using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

/// <summary>
/// 雇佣流程关联的模板技能配置状态。与 external-config 一致，使用整份状态 JSON 作为唯一事实来源。
/// </summary>
public sealed class HiringSkillLinkConfigEntity : ITenant
{
    [Key]
    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [Required]
    public required string ConfigJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}
