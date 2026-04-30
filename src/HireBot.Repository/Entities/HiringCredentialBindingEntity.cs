using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class HiringCredentialBindingEntity
{
    [Key]
    [MaxLength(64)]
    public required string BindingId { get; set; }

    [MaxLength(64)]
    public required string SessionId { get; set; }

    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(160)]
    public required string CredentialSlot { get; set; }

    [MaxLength(256)]
    public string? SecretRef { get; set; }

    [MaxLength(80)]
    public string? AuthKind { get; set; }

    [MaxLength(160)]
    public string? TargetSystem { get; set; }

    [MaxLength(160)]
    public string? TodoId { get; set; }

    [MaxLength(64)]
    public required string BindingStatus { get; set; }

    public required string ProtectedSecret { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
