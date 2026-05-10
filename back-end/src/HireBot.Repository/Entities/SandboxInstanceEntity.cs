using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class SandboxInstanceEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(120)]
    public string SandboxId { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string ScopeType { get; set; } = string.Empty;

    [Required]
    [MaxLength(160)]
    public string ScopeKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string SandboxRole { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string ProvisioningMode { get; set; } = "managed";

    [Required]
    [MaxLength(256)]
    public string OwnerSubject { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string OperatorId { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string State { get; set; } = "creating";

    [MaxLength(512)]
    public string? GatewayEndpoint { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }

    [MaxLength(200)]
    public string? UseCase { get; set; }

    [MaxLength(128)]
    public string? TemplateId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SandboxSessionEntity> Sessions { get; set; } = new List<SandboxSessionEntity>();

    public ICollection<SandboxAssetEntity> Assets { get; set; } = new List<SandboxAssetEntity>();
}
