namespace HireBot.Abstraction.Contracts;

/// <summary>
/// 更新审计接口，记录实体的最后更新信息
/// </summary>
public interface IUpdatedInfo
{
    /// <summary>
    /// 最后更新时间（UTC）
    /// </summary>
    DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 最后更新用户ID（通常来自 JWT sub claim）
    /// </summary>
    string? UpdatedByUserId { get; set; }
}
