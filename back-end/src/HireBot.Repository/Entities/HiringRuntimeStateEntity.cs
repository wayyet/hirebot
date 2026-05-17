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

    /// <summary>身份元数据（TemplateId、OwnerSubject、连接状态等标量字段）。</summary>
    public required string PayloadJson { get; set; }

    /// <summary>模板包定义：RoleTemplatePackage、WorkingTemplatePackage、DiscoverySkill。体积较大，单独列存储。</summary>
    public string PackagesJson { get; set; } = "{}";

    /// <summary>动态工作流状态：StructuredData、Materials、HandoffItems、StageCompletion 等。每轮对话更新。</summary>
    public string WorkflowStateJson { get; set; } = "{}";

    /// <summary>前端对话状态缓存（ChatMessage[] + stageOverrides）序列化为 JSON，用于刷新页面后恢复对话历史。</summary>
    public string ConversationCacheJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
