using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class HiringArtifactUploadEntity
{
    [Key]
    public Guid UploadId { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(32)]
    public required string Kind { get; set; }

    [MaxLength(1024)]
    public required string LogicalPath { get; set; }

    [MaxLength(512)]
    public required string FileName { get; set; }

    public long TotalSizeBytes { get; set; }
    public int PartSizeBytes { get; set; }
    public int TotalParts { get; set; }

    [MaxLength(64)]
    public string? ExpectedSha256 { get; set; }

    [MaxLength(1024)]
    public required string TempStorageDirectory { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? AbortedAtUtc { get; set; }
}

