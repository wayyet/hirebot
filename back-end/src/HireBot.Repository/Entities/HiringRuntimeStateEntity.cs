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

    /// <summary>前端对话状态缓存（ChatMessage[] + stageOverrides）序列化为 JSON，用于刷新页面后恢复对话历史。</summary>
    public string ConversationCacheJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
