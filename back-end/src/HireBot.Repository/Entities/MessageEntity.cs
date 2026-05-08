using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class MessageEntity
{
    [Key]
    [MaxLength(120)]
    public required string MessageId { get; set; }

    [MaxLength(120)]
    public required string ConversationId { get; set; }

    [MaxLength(120)]
    public required string InstanceId { get; set; }

    [MaxLength(128)]
    public required string TenantId { get; set; }

    [MaxLength(40)]
    public required string Role { get; set; }

    public required string Content { get; set; }

    [MaxLength(40)]
    public required string Channel { get; set; } = "inapp";

    [MaxLength(160)]
    public string? ExternalMessageId { get; set; }

    [MaxLength(160)]
    public string? ExternalUserId { get; set; }

    [MaxLength(40)]
    public string? DeliveryStatus { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
