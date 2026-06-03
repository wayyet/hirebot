using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class HiringArtifactEntity
{
    [Key]
    public Guid ArtifactId { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(32)]
    public required string Kind { get; set; } // source_zip | intermediate | intermediate_package_zip | final_package_zip

    [MaxLength(1024)]
    public required string LogicalPath { get; set; }

    [MaxLength(512)]
    public required string FileName { get; set; }

    public long SizeBytes { get; set; }

    [MaxLength(64)]
    public required string Sha256 { get; set; }

    [MaxLength(1024)]
    public required string StoragePath { get; set; }

    public bool IsFinal { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DeletedAtUtc { get; set; }

    [MaxLength(256)]
    public string? DeletedBy { get; set; }
}

