namespace HireBot.Abstraction.Contracts;

/// <summary>
/// 多租户接口，所有需要租户隔离的实体都应实现此接口
/// </summary>
public interface ITenant
{
    /// <summary>
    /// 租户ID，用于实现多租户数据隔离
    /// </summary>
    string? TenantId { get; set; }
}
