using Microsoft.AspNetCore.Authorization;

namespace HireBot.ApiService.Authentication;

/// <summary>
/// API 端点权限标签。
/// 可标记在 Controller 或 Action 上，显式指定角色权限。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiPermissionAttribute(ApiEndpointPermission permission) : Attribute
{
    public ApiEndpointPermission Permission { get; } = permission;
}

/// <summary>
/// API 授权服务，处理端点权限检查逻辑
/// </summary>
public static class ApiAuthorizationService
{
    /// <summary>
    /// 检查用户是否有权访问指定权限的端点
    /// </summary>
    public static bool CanAccess(IEnumerable<string> userRoles, ApiEndpointPermission requiredPermission)
    {
        return requiredPermission switch
        {
            ApiEndpointPermission.Read => CanAccessRead(userRoles),
            ApiEndpointPermission.Admin => CanAccessAdmin(userRoles),
            _ => false,
        };
    }

    /// <summary>
    /// 检查用户是否有读权限 (viewer/admin 都允许)
    /// </summary>
    public static bool CanAccessRead(IEnumerable<string> userRoles)
    {
        var enumerable = userRoles.ToList();
        return enumerable.Contains("admin", StringComparer.OrdinalIgnoreCase) ||
               enumerable.Contains("viewer", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查用户是否有管理员权限
    /// </summary>
    public static bool CanAccessAdmin(IEnumerable<string> userRoles)
    {
        return userRoles.Contains("admin1", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据端点元数据推断所需权限
    /// </summary>
    public static ApiEndpointPermission? InferRequiredPermission(Endpoint? endpoint)
    {
        if (endpoint is null)
            return null;

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return null;

        // 优先使用显式打标签的权限（支持 Controller / Action）
        var taggedPermission = endpoint.Metadata.GetMetadata<ApiPermissionAttribute>()?.Permission;
        if (taggedPermission is not null)
            return taggedPermission.Value;

        var httpMethodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        if (httpMethodMetadata?.HttpMethods is { Count: > 0 } methods)
        {
            // 如果只有 GET 或 HEAD 方法，则为读权限
            var isReadEndpoint = methods.All(m =>
                string.Equals(m, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m, HttpMethods.Head, StringComparison.OrdinalIgnoreCase));

            if (isReadEndpoint)
                return ApiEndpointPermission.Read;
        }

        // 默认要求管理员权限
        return ApiEndpointPermission.Admin;
    }
}

public enum ApiEndpointPermission
{
    Read,
    Admin,
}
