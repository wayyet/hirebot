namespace HireBot.Abstraction.Infrastructure.Multitenancy;

/// <summary>
/// 租户上下文提供者接口
/// 负责在应用程序的不同位置获取和设置当前租户ID
/// </summary>
public interface ITenantContextProvider
{
    /// <summary>
    /// 获取当前租户ID
    /// 优先级：手动设置 > JWT Claims > 默认值
    /// </summary>
    /// <returns>租户ID，可能为 null</returns>
    string? GetTenantId();

    /// <summary>
    /// 手动设置租户ID（用于后台任务、系统初始化等场景）
    /// </summary>
    /// <param name="tenantId">要设置的租户ID</param>
    void SetTenantId(string? tenantId);

    /// <summary>
    /// 清除手动设置的租户ID
    /// </summary>
    void ClearTenantId();

    /// <summary>
    /// 获取租户ID的来源
    /// </summary>
    /// <returns>租户ID来源枚举</returns>
    TenantIdSource GetTenantIdSource();
}

/// <summary>
/// 租户ID来源枚举
/// </summary>
public enum TenantIdSource
{
    /// <summary>手动设置（后台任务、测试等）</summary>
    Manual,
    
    /// <summary>从 JWT Claims 获取</summary>
    JwtClaims,
    
    /// <summary>默认值（未设置）</summary>
    Default
}
