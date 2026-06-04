using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

/// <summary>
/// 平台用户 — 从 JWT claims 同步入库，用于展示创建人/更新人信息
/// 多租户设计：同一个 Keycloak 用户在不同租户下有独立记录
/// 唯一约束：(TenantId, ExternalUserId)
/// </summary>
public sealed class AppUserEntity : ITenant, IPrimaryKey
{
    /// <summary>主键，全局唯一标识（使用 GUID）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>外部用户ID (JWT sub, Keycloak 用户唯一标识)</summary>
    public string ExternalUserId { get; set; } = string.Empty;

    /// <summary>租户ID，用于多租户数据隔离</summary>
    public string? TenantId { get; set; }

    /// <summary>用户名 (preferred_username)</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>显示名称 (name，通常为全名)</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>姓氏 (family_name)</summary>
    public string? FamilyName { get; set; }

    /// <summary>名字 (given_name)</summary>
    public string? GivenName { get; set; }

    /// <summary>电子邮箱</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>首次创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最后活跃时间</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
