using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class HiringMaterialFileEntity : ITenant
{
    [Key]
    public Guid MaterialFileId { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(1024)]
    public required string RelativePath { get; set; }

    [MaxLength(512)]
    public required string OriginalFileName { get; set; }

    [MaxLength(1024)]
    public required string StoragePath { get; set; }

    [MaxLength(32)]
    public required string Format { get; set; }

    [MaxLength(120)]
    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    [MaxLength(64)]
    public required string Sha256 { get; set; }

    [MaxLength(160)]
    public string? RequestedCategoryTitle { get; set; }

    [MaxLength(1024)]
    public string? WorkspaceRelativePath { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(128)]
    public required string OperatorId { get; set; }

    [MaxLength(256)]
    public required string UploadedBy { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DeletedAtUtc { get; set; }
}
