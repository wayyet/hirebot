using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class HiringAuditLogEntity : ITenant
{
    [Key]
    public Guid AuditId { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(64)]
    public string? ArtifactId { get; set; }

    [MaxLength(64)]
    public string? BeforeSha256 { get; set; }

    [MaxLength(64)]
    public string? AfterSha256 { get; set; }

    [MaxLength(64)]
    public required string Action { get; set; }

    [MaxLength(256)]
    public required string Actor { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(2048)]
    public string? DetailJson { get; set; }
}

