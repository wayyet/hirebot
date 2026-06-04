using System.Text.Json.Serialization;

namespace HireBot.Abstraction.Infrastructure.Identity;

/// <summary>
/// 用户身份信息接口，统一访问当前用户的身份和租户信息
/// </summary>
public interface IUserIdentity
{
    /// <summary>外部用户 ID（JWT sub claim，Keycloak user ID）</summary>
    [JsonPropertyName("id")]
    string Id { get; }

    /// <summary>用户邮箱</summary>
    [JsonPropertyName("email")]
    string Email { get; }

    /// <summary>用户名</summary>
    [JsonPropertyName("user_name")]
    string UserName { get; }

    /// <summary>名</summary>
    [JsonPropertyName("first_name")]
    string FirstName { get; }

    /// <summary>姓</summary>
    [JsonPropertyName("last_name")]
    string LastName { get; }

    /// <summary>全名</summary>
    [JsonPropertyName("full_name")]
    string FullName { get; }

    /// <summary>显示名称</summary>
    [JsonPropertyName("display_name")]
    string DisplayName { get; }

    /// <summary>租户 ID</summary>
    [JsonPropertyName("tenant_id")]
    string? TenantId { get; }

    /// <summary>租户名称</summary>
    [JsonPropertyName("tenant_name")]
    string? TenantName { get; }

    /// <summary>操作员 ID（通常等同于 UserName）</summary>
    [JsonPropertyName("operator_id")]
    string OperatorId { get; }

    /// <summary>所有者主体标识（用于沙箱等资源的所有权标识，通常为 JWT sub）</summary>
    [JsonPropertyName("owner_subject")]
    string OwnerSubject { get; }

    /// <summary>用户角色</summary>
    [JsonPropertyName("role")]
    string? Role { get; }

    /// <summary>是否已认证</summary>
    [JsonPropertyName("is_authenticated")]
    bool IsAuthenticated { get; }

    /// <summary>部门 ID（可选）</summary>
    [JsonPropertyName("department_id")]
    string? DepartmentId { get; }
}
