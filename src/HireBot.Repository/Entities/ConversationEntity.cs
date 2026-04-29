using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class ConversationEntity
{
    [Key]
    [MaxLength(120)]
    public required string ConversationId { get; set; }

    [MaxLength(120)]
    public required string InstanceId { get; set; }

    [MaxLength(128)]
    public required string TenantId { get; set; }

    [MaxLength(256)]
    public required string OwnerUserId { get; set; }

    [MaxLength(40)]
    public required string Channel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
