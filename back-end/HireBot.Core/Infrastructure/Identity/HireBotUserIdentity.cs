using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using HireBot.Abstraction.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace HireBot.Core.Infrastructure.Identity;

/// <summary>
/// HireBot 用户身份信息实现，从 JWT Claims 中提取用户和租户信息
/// </summary>
public sealed class HireBotUserIdentity(IHttpContextAccessor httpContextAccessor) : IUserIdentity
{
    private IEnumerable<Claim> Claims => httpContextAccessor.HttpContext?.User?.Claims ?? [];

    [JsonPropertyName("id")]
    public string Id =>
        Claims.FirstOrDefault(x => x.Type == "sub")?.Value ??
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value ??
        string.Empty;

    [JsonPropertyName("email")]
    public string Email =>
        Claims.FirstOrDefault(x => x.Type == "email")?.Value ??
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value ??
        string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName =>
        Claims.FirstOrDefault(x => x.Type == "preferred_username")?.Value ??
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value ??
        Claims.FirstOrDefault(x => x.Type == "name")?.Value ??
        Id;

    [JsonPropertyName("first_name")]
    public string FirstName =>
        Claims.FirstOrDefault(x => x.Type == "given_name")?.Value ??
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.GivenName)?.Value ??
        UserName;

    [JsonPropertyName("last_name")]
    public string LastName =>
        Claims.FirstOrDefault(x => x.Type == "family_name")?.Value ??
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.Surname)?.Value ??
        string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName
    {
        get
        {
            var fullName = Claims.FirstOrDefault(x => x.Type == "name")?.Value;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName;
            }
            return string.IsNullOrWhiteSpace(LastName) 
                ? FirstName 
                : $"{FirstName} {LastName}".Trim();
        }
    }

    [JsonPropertyName("display_name")]
    public string DisplayName =>
        Claims.FirstOrDefault(x => x.Type == "name")?.Value ??
        FullName;

    [JsonPropertyName("tenant_id")]
    public string? TenantId
    {
        get
        {
            // 优先从标准 tenant claims 读取
            var tenantId = Claims.FirstOrDefault(x => x.Type == "tenant_id")?.Value ??
                          Claims.FirstOrDefault(x => x.Type == "tid")?.Value ??
                          Claims.FirstOrDefault(x => x.Type == "tenant")?.Value;

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                return tenantId;
            }

            // 尝试从 organization claim 解析（兼容多种格式）
            var organizationClaim = Claims.FirstOrDefault(x => x.Type == "organization")?.Value;
            if (!string.IsNullOrWhiteSpace(organizationClaim))
            {
                try
                {
                    using var document = JsonDocument.Parse(organizationClaim);
                    // 查找第一个包含 id 属性的对象
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object &&
                            property.Value.TryGetProperty("id", out var id))
                        {
                            return id.GetString();
                        }
                    }
                }
                catch
                {
                    // 解析失败，继续使用 fallback
                }
            }

            // 尝试从 GroupSid 读取（某些 SSO 配置）
            tenantId = Claims.FirstOrDefault(x => x.Type == ClaimTypes.GroupSid)?.Value;
            return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        }
    }

    [JsonPropertyName("tenant_name")]
    public string? TenantName
    {
        get
        {
            // 尝试从 organization claim 解析
            var organizationClaim = Claims.FirstOrDefault(x => x.Type == "organization")?.Value;
            if (!string.IsNullOrWhiteSpace(organizationClaim))
            {
                try
                {
                    using var document = JsonDocument.Parse(organizationClaim);
                    // 查找第一个包含 id 属性的对象，property.Name 就是租户名称
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object &&
                            property.Value.TryGetProperty("id", out _))
                        {
                            return property.Name;
                        }
                    }
                }
                catch
                {
                    // 解析失败
                }
            }

            // fallback: 使用 tenant_name claim
            return Claims.FirstOrDefault(x => x.Type == "tenant_name")?.Value;
        }
    }

    [JsonPropertyName("operator_id")]
    public string OperatorId =>
        Claims.FirstOrDefault(x => x.Type == "operator_id")?.Value ??
        UserName;

    [JsonPropertyName("owner_subject")]
    public string OwnerSubject
    {
        get
        {
            // OwnerSubject 优先使用 JWT sub
            var sub = Id;
            if (!string.IsNullOrWhiteSpace(sub))
            {
                return sub;
            }

            // Fallback: 从 X-HireBot-Owner header 读取（测试用）
            var ownerHeader = httpContextAccessor.HttpContext?.Request?.Headers["X-HireBot-Owner"].ToString();
            if (!string.IsNullOrWhiteSpace(ownerHeader))
            {
                return ownerHeader.Trim();
            }

            // 最终 fallback: TenantId:OperatorId
            var tenantId = TenantId ?? "tenant-default";
            var operatorId = OperatorId;
            return $"{tenantId}:{operatorId}";
        }
    }

    [JsonPropertyName("role")]
    public string? Role =>
        Claims.FirstOrDefault(x => x.Type == ClaimTypes.Role)?.Value ??
        Claims.FirstOrDefault(x => x.Type == "role")?.Value;

    [JsonPropertyName("is_authenticated")]
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    [JsonPropertyName("department_id")]
    public string? DepartmentId =>
        Claims.FirstOrDefault(x => x.Type == "department_id")?.Value ??
        Claims.FirstOrDefault(x => x.Type == "dept_id")?.Value;
}
