using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class HiringArtifactUploadPartEntity : ITenant
{
    [Key]
    public Guid PartId { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public string? TenantId { get; set; }

    public Guid UploadId { get; set; }

    public int PartNumber { get; set; } // 1-based

    public long SizeBytes { get; set; }

    [MaxLength(64)]
    public required string Sha256 { get; set; }

    [MaxLength(1024)]
    public required string StoragePath { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

