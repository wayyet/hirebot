using HireBot.Abstraction.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HireBot.Core.Infrastructure.Identity;

/// <summary>
/// HireBot 用户身份信息实现，从 JWT Claims 中提取用户和租户信息
/// </summary>
public sealed class HireBotUserIdentity(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : IUserIdentity
{
    private IEnumerable<Claim> Claims => httpContextAccessor.HttpContext?.User?.Claims ?? [];
    private const string DefaultResourceAccessClientId = "ncrew-client";

    [JsonPropertyName("id")]
    public string Id =>
        Claim("sub") ??
        Claim(ClaimTypes.NameIdentifier) ??
        string.Empty;

    [JsonPropertyName("email")]
    public string Email =>
        Claim("email") ??
        Claim(ClaimTypes.Email) ??
        string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName =>
        Claim("preferred_username") ??
        Claim("username") ??
        Claim(ClaimTypes.Name) ??
        Claim("name") ??
        Id;

    [JsonPropertyName("first_name")]
    public string FirstName =>
        Claim("given_name") ??
        Claim(ClaimTypes.GivenName) ??
        UserName;

    [JsonPropertyName("last_name")]
    public string LastName =>
        Claim("family_name") ??
        Claim(ClaimTypes.Surname) ??
        string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName
    {
        get
        {
            var fullName = Claim("name");
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
        Claim("display_name") ??
        Claim("name") ??
        FullName;

    [JsonPropertyName("tenant_id")]
    public string? TenantId
    {
        get
        {
            var tenantId = Claim("tenant_id", "tid", "tenant");

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                return tenantId;
            }

            var organizationTenantId = TryReadOrganizationTenantId();
            if (!string.IsNullOrWhiteSpace(organizationTenantId))
            {
                return organizationTenantId;
            }

            tenantId = Claim(ClaimTypes.GroupSid);
            return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        }
    }

    [JsonPropertyName("tenant_name")]
    public string? TenantName =>
        TryReadOrganizationTenantName() ??
        Claim("tenant_name");

    [JsonPropertyName("operator_id")]
    public string OperatorId =>
        Claim("operator_id") ??
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

            return OperatorId;
        }
    }

    [JsonPropertyName("role")]
    public string? Role
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user is null)
                return null;

            var clientId = configuration.GetSection("ConsoleAuth")["ClientId"] ?? DefaultResourceAccessClientId;
            var roles = JwtRoleResolver.GetRoles(user, clientId);
            if (!roles.Any())
                return null;

            // Prioritize roles by permission level: admin > viewer
            return roles.Contains("admin", StringComparer.OrdinalIgnoreCase)
                ? "admin"
                : roles.FirstOrDefault();
        }
    }

    [JsonPropertyName("is_authenticated")]
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    [JsonPropertyName("department_id")]
    public string? DepartmentId =>
        Claim("department_id", "dept_id");

    private string? Claim(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Claims.FirstOrDefault(x => x.Type == name)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private string? TryReadOrganizationTenantId()
    {
        var organizationClaim = Claim("organization");
        if (string.IsNullOrWhiteSpace(organizationClaim))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(organizationClaim);
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
            // 忽略格式异常，继续使用其他租户 claim。
        }

        return null;
    }

    private string? TryReadOrganizationTenantName()
    {
        var organizationClaim = Claim("organization");
        if (string.IsNullOrWhiteSpace(organizationClaim))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(organizationClaim);
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
            // 忽略格式异常，继续使用 tenant_name claim。
        }

        return null;
    }
}
