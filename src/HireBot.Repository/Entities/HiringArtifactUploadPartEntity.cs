using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class HiringArtifactUploadPartEntity
{
    [Key]
    public Guid PartId { get; set; } = Guid.NewGuid();

    public Guid UploadId { get; set; }

    public int PartNumber { get; set; } // 1-based

    public long SizeBytes { get; set; }

    [MaxLength(64)]
    public required string Sha256 { get; set; }

    [MaxLength(1024)]
    public required string StoragePath { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

