using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HireBot.Repository.Entities;

public sealed class SandboxSessionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SandboxInstanceEntityId { get; set; }

    [ForeignKey(nameof(SandboxInstanceEntityId))]
    public SandboxInstanceEntity? SandboxInstance { get; set; }

    [Required]
    [MaxLength(120)]
    public string SessionId { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    public string ScopeType { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    public string ScopeKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string SandboxRole { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    public string SessionKey { get; set; } = "default";

    [MaxLength(120)]
    public string? ChannelId { get; set; }

    [MaxLength(120)]
    public string? SenderId { get; set; }

    [Required]
    [MaxLength(256)]
    public string OwnerSubject { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SandboxAssetEntity> Assets { get; set; } = new List<SandboxAssetEntity>();
}
