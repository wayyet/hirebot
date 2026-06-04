using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class HiringSessionEntity : ITenant
{
    [Key]
    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(128)]
    public required string TemplateId { get; set; }

    [MaxLength(256)]
    public string? PackageId { get; set; }

    [MaxLength(64)]
    public string? PackageVersion { get; set; }

    [MaxLength(64)]
    public string? PackageHash { get; set; }

    [MaxLength(64)]
    public string? SourceZipSha256 { get; set; }

    [MaxLength(1024)]
    public string? SourceZipStoragePath { get; set; }

    public long? SourceZipSizeBytes { get; set; }

    [MaxLength(256)]
    public required string OwnerSubject { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(128)]
    public required string OperatorId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DeletedAtUtc { get; set; }

    [MaxLength(256)]
    public string? DeletedBy { get; set; }
}

