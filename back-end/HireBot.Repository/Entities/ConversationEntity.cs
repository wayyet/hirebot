using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class ConversationEntity : ITenant
{
    [Key]
    [MaxLength(120)]
    public required string ConversationId { get; set; }

    [MaxLength(120)]
    public required string InstanceId { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(256)]
    public required string OwnerUserId { get; set; }

    [MaxLength(40)]
    public required string Channel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
