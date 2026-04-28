using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HireBot.Repository.Entities;

public sealed class EvaluationAssetEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid SessionEntityId { get; set; }

    [ForeignKey(nameof(SessionEntityId))]
    public EvaluationSessionEntity? Session { get; set; }

    [Required]
    [MaxLength(40)]
    public string AssetType { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? RelatedKey { get; set; }

    [Required]
    [MaxLength(512)]
    public string RelativePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string PublicUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string MimeType { get; set; } = string.Empty;

    public long Size { get; set; }

    [Required]
    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string SourceType { get; set; } = "system";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
