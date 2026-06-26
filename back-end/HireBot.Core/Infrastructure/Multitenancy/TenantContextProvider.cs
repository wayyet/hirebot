using System.Security.Claims;
using HireBot.Abstraction.Infrastructure.Identity;
using HireBot.Abstraction.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Infrastructure.Multitenancy;

/// <summary>
/// 租户上下文提供者实现
/// 使用 AsyncLocal 实现线程安全的租户上下文传播
/// </summary>
public class TenantContextProvider : ITenantContextProvider
{
    private static readonly AsyncLocal<string?> _currentTenantId = new();
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserIdentity _userIdentity;
    private readonly ILogger<TenantContextProvider> _logger;

    public TenantContextProvider(
        IHttpContextAccessor httpContextAccessor,
        IUserIdentity userIdentity,
        ILogger<TenantContextProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _userIdentity = userIdentity;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前租户ID
    /// 三级优先级：1) 手动设置 2) 用户身份 3) JWT Claims
    /// </summary>
    public string? GetTenantId()
    {
        // 1. 优先使用手动设置的租户ID（用于后台任务等）
        var manualTenantId = _currentTenantId.Value;
        if (!string.IsNullOrWhiteSpace(manualTenantId))
        {
            _logger.LogDebug("使用手动设置的租户ID: {TenantId}", manualTenantId);
            return manualTenantId;
        }

        // 2. 尝试从用户身份上下文获取
        try
        {
            if (!string.IsNullOrWhiteSpace(_userIdentity.TenantId))
            {
                _logger.LogDebug("从用户身份上下文获取租户ID: {TenantId}", _userIdentity.TenantId);
                return _userIdentity.TenantId;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                var tenantId = GetTenantIdFromClaims(httpContext.User);
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    _logger.LogDebug("从 JWT Claims 获取租户ID: {TenantId}", tenantId);
                    return tenantId;
                }

                _logger.LogWarning("用户已认证但无法从 JWT Claims 获取租户ID");
            }
        }
        catch (Exception ex)
        {
            // 忽略异常，可能在没有 HTTP 上下文的场景下（如后台任务）
            _logger.LogWarning(ex, "从 HTTP 上下文获取租户ID时发生异常");
        }

        _logger.LogDebug("当前上下文没有可用租户ID");
        return null;
    }

    /// <summary>
    /// 手动设置租户ID（用于后台任务或初始化场景）
    /// </summary>
    public void SetTenantId(string? tenantId)
    {
        _logger.LogInformation("手动设置租户ID: {TenantId}", tenantId ?? "(null)");
        _currentTenantId.Value = tenantId;
    }

    /// <summary>
    /// 清除手动设置的租户ID
    /// </summary>
    public void ClearTenantId()
    {
        _logger.LogDebug("清除手动设置的租户ID");
        _currentTenantId.Value = null;
    }

    /// <summary>
    /// 获取租户ID的来源
    /// </summary>
    public TenantIdSource GetTenantIdSource()
    {
        // 检查是否有手动设置的租户ID
        if (!string.IsNullOrWhiteSpace(_currentTenantId.Value))
        {
            return TenantIdSource.Manual;
        }

        // 检查是否可以从 JWT Claims 获取
        try
        {
            if (!string.IsNullOrWhiteSpace(_userIdentity.TenantId))
            {
                return TenantIdSource.JwtClaims;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                if (!string.IsNullOrWhiteSpace(GetTenantIdFromClaims(httpContext.User)))
                {
                    return TenantIdSource.JwtClaims;
                }
            }
        }
        catch
        {
            // 忽略异常
        }

        // 默认值
        return TenantIdSource.Default;
    }

    private static string? GetTenantIdFromClaims(ClaimsPrincipal user)
    {
        var tenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value
            ?? user.FindFirst("tenant")?.Value
            ?? user.FindFirst(ClaimTypes.GroupSid)?.Value;

        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
    }
}
