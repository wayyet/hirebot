namespace HireBot.Abstraction.Contracts;

/// <summary>
/// 主键接口，定义实体的唯一标识
/// </summary>
public interface IPrimaryKey
{
    /// <summary>
    /// 实体主键，全局唯一标识
    /// </summary>
    string Id { get; set; }
}
