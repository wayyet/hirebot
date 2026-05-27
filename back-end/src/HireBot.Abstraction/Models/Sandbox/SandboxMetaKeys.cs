namespace HireBot.Abstraction.Models.Sandbox;

/// <summary>
/// 沙箱元数据键常量，用于 SandboxInstanceEntity.Metadata 字典，
/// 方便通过列表接口直接识别沙箱归属与上下文，无需关联其他表。
/// </summary>
public static class SandboxMetaKeys
{
    // ──────────────────────────────────────────
    // 通用字段（所有类型沙箱均应填写）
    // ──────────────────────────────────────────

    /// <summary>发起操作的用户主体，格式与 OwnerSubject 一致。</summary>
    public const string UserSubject = "user_subject";

    // ──────────────────────────────────────────
    // 雇佣流程沙箱（ScopeType = "hire"）
    // ──────────────────────────────────────────

    /// <summary>雇佣流程 ID，等于 ScopeKey（hire-{Guid}）。</summary>
    public const string HireId = "hire_id";

    /// <summary>雇佣所用模板 ID。</summary>
    public const string TemplateId = "template_id";

    /// <summary>雇佣流程外部系统配置的加密 JSON 快照。</summary>
    public const string ExternalSystemConfig = "external_system_config";

    // ──────────────────────────────────────────
    // 托管评估沙箱（ScopeType = "managed"）
    // ──────────────────────────────────────────

    /// <summary>被评估的员工实例 ID。</summary>
    public const string EmployeeId = "employee_id";

    /// <summary>评估运行时 Scope Key（eval-{role}-{Guid}），等于 ScopeKey。</summary>
    public const string EvalScopeKey = "eval_scope_key";

    // ──────────────────────────────────────────
    // 个人运行时沙箱（ScopeType = "runtime"）
    // ──────────────────────────────────────────

    /// <summary>绑定的员工实例 ID，可用于反查 InstanceEntity。</summary>
    public const string InstanceId = "instance_id";
}
