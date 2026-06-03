namespace HireBot.Abstraction.Contracts;

/// <summary>
/// 创建审计接口，记录实体的创建信息
/// </summary>
public interface ICreatedInfo
{
    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 创建用户ID（通常来自 JWT sub claim）
    /// </summary>
    string CreatedByUserId { get; set; }
}
