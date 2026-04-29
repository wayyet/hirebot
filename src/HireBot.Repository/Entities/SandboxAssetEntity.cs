using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HireBot.Repository.Entities;

public sealed class SandboxAssetEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SandboxInstanceEntityId { get; set; }

    [ForeignKey(nameof(SandboxInstanceEntityId))]
    public SandboxInstanceEntity? SandboxInstance { get; set; }

    public Guid? SandboxSessionEntityId { get; set; }

    [ForeignKey(nameof(SandboxSessionEntityId))]
    public SandboxSessionEntity? SandboxSession { get; set; }

    [Required]
    [MaxLength(120)]
    public string MediaId { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string MimeType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [MaxLength(128)]
    public string? ContentHash { get; set; }

    [MaxLength(1024)]
    public string? StoragePath { get; set; }

    [Required]
    [MaxLength(80)]
    public string AssetRole { get; set; } = "attachment";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
