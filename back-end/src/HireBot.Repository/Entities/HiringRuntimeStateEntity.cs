using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class HiringRuntimeStateEntity
{
    [Key]
    [MaxLength(64)]
    public required string HireId { get; set; }

    [MaxLength(64)]
    public required string SessionId { get; set; }


    [MaxLength(64)]
    public required string CurrentStage { get; set; }

    [MaxLength(64)]
    public required string CollectionPhase { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
