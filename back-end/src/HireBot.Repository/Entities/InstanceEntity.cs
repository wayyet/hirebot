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

    /// <summary>
    /// 当此实例有活跃的私有分支时，指向该分支的 InstanceId。
    /// IM 消息和站内对话路由到此分支。私有分支废弃时清空。
    /// </summary>
    [MaxLength(120)]
    public string? ActiveBranchId { get; set; }

    [MaxLength(120)]
    public string? EvalReportId { get; set; }

    [MaxLength(256)]
    public required string OwnerUserId { get; set; }

    [MaxLength(128)]
    public required string DepartmentId { get; set; }

    [MaxLength(80)]
    public required string CurrentVersion { get; set; }

    public string? RuntimeSnapshotJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
