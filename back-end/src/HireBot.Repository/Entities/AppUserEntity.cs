namespace HireBot.Repository.Entities;

/// <summary>
/// 平台用户 — 从 JWT claims 同步入库，用于展示创建人/更新人信息
/// </summary>
public sealed class AppUserEntity
{
    /// <summary>JWT sub (OIDC 用户唯一 ID)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>租户 ID</summary>
    public string TenantId { get; set; } = string.Empty;

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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后活跃时间</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
