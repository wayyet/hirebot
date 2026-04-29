using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class InstanceEntity
{
    [Key]
    [MaxLength(120)]
    public required string InstanceId { get; set; }

    [MaxLength(128)]
    public required string TenantId { get; set; }

    [MaxLength(40)]
    public required string InstanceType { get; set; }

    [MaxLength(40)]
    public required string Status { get; set; }

    public bool ViaQuickClone { get; set; }

    [MaxLength(128)]
    public string? BasedOnTemplateId { get; set; }

    [MaxLength(120)]
    public string? FromInstanceId { get; set; }

    [MaxLength(120)]
    public string? EvalReportId { get; set; }

    [MaxLength(256)]
    public required string OwnerUserId { get; set; }

    [MaxLength(128)]
    public required string DepartmentId { get; set; }

    [MaxLength(80)]
    public required string CurrentVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
