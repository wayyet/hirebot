namespace HireBot.Abstraction.Models.EmployeeRuntime;

/// <summary>
/// 员工实例状态常量定义。
/// </summary>
public static class EmployeeStatus
{
    /// <summary>
    /// 雇佣中（雇佣流程进行中）。
    /// </summary>
    public const string Hiring = "hiring";

    /// <summary>
    /// 已雇佣（雇佣流程完成，候选包已导入，等待进入评估）。
    /// </summary>
    public const string Hired = "hired";

    /// <summary>
    /// AI实习中（AI评估阶段）。
    /// </summary>
    public const string InterningAi = "interning_ai";

    /// <summary>
    /// 人工实习中（人工评估阶段）。
    /// </summary>
    public const string InterningHuman = "interning_human";

    /// <summary>
    /// 已上岗（正式上线）。
    /// </summary>
    public const string Live = "live";

    /// <summary>
    /// 失败（评估未通过）。
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// 已退休（下线）。
    /// </summary>
    public const string Retired = "retired";
}
