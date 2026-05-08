using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class ImConfigEntity
{
    [Key]
    [MaxLength(120)]
    public required string ConfigId { get; set; }

    [MaxLength(120)]
    public required string InstanceId { get; set; }

    [MaxLength(128)]
    public required string TenantId { get; set; }

    [MaxLength(256)]
    public required string OwnerUserId { get; set; }

    [MaxLength(40)]
    public required string Platform { get; set; }

    [MaxLength(40)]
    public required string ConnectionMode { get; set; }

    [MaxLength(256)]
    public string? WebhookPath { get; set; }

    public string? AppId { get; set; }

    public string? AppSecret { get; set; }

    public string? EncryptKey { get; set; }

    public string? Token { get; set; }

    public string? AesKey { get; set; }

    public string? VerificationToken { get; set; }

    [MaxLength(512)]
    public string? CorpId { get; set; }

    [MaxLength(512)]
    public string? AgentId { get; set; }

    [MaxLength(256)]
    public string? AgentSecret { get; set; }

    [MaxLength(40)]
    public required string Status { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }

    public DateTimeOffset? ConfiguredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

