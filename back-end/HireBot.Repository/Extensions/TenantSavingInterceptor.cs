using HireBot.Abstraction.Contracts;
using HireBot.Abstraction.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HireBot.Repository.Extensions;

/// <summary>
/// 租户保存拦截器
/// 在保存实体前自动设置租户ID和审计字段
/// </summary>
public class TenantSavingInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantSavingInterceptor> _logger;

    public TenantSavingInterceptor(
        IServiceProvider serviceProvider,
        ILogger<TenantSavingInterceptor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTenantAndAuditInfo(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAuditInfo(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// 应用租户ID和审计信息
    /// </summary>
    private void ApplyTenantAndAuditInfo(DbContext? context)
    {
        if (context == null) return;

        // 从服务容器获取租户上下文
        using var scope = _serviceProvider.CreateScope();
        var tenantContextProvider = scope.ServiceProvider
            .GetService<ITenantContextProvider>();

        var currentTenantId = tenantContextProvider?.GetTenantId();
        var currentUserId = GetCurrentUserId(scope.ServiceProvider);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // 处理租户ID
            if (entry.Entity is ITenant tenantEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    // 新增实体：设置租户ID
                    if (string.IsNullOrWhiteSpace(tenantEntity.TenantId))
                    {
                        tenantEntity.TenantId = currentTenantId;
                        _logger.LogDebug("自动设置租户ID: {EntityType} - {TenantId}", 
                            entry.Entity.GetType().Name, currentTenantId);
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    // 修改实体：验证租户ID是否被篡改
                    var originalTenantId = entry.OriginalValues.GetValue<string?>(nameof(ITenant.TenantId));
                    if (originalTenantId != tenantEntity.TenantId)
                    {
                        _logger.LogWarning(
                            "检测到租户ID被修改: {EntityType} {EntityId}, 原值={Original}, 新值={Current}",
                            entry.Entity.GetType().Name,
                            (entry.Entity as IPrimaryKey)?.Id,
                            originalTenantId,
                            tenantEntity.TenantId);

                        // 可以选择阻止此操作
                        // throw new InvalidOperationException("不允许修改租户ID");
                    }
                }
            }

            // 处理创建信息
            if (entry.State == EntityState.Added && entry.Entity is ICreatedInfo createdInfo)
            {
                createdInfo.CreatedAt = DateTimeOffset.UtcNow;
                if (string.IsNullOrWhiteSpace(createdInfo.CreatedByUserId))
                {
                    createdInfo.CreatedByUserId = currentUserId ?? "system";
                }
            }

            // 处理更新信息
            if (entry.State == EntityState.Modified && entry.Entity is IUpdatedInfo updatedInfo)
            {
                updatedInfo.UpdatedAt = DateTimeOffset.UtcNow;
                updatedInfo.UpdatedByUserId = currentUserId;
            }
        }
    }

    /// <summary>
    /// 从服务提供者获取当前用户ID
    /// </summary>
    private string? GetCurrentUserId(IServiceProvider serviceProvider)
    {
        try
        {
            var httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();
            var userId = httpContextAccessor?.HttpContext?.User?.FindFirst("sub")?.Value
                ?? httpContextAccessor?.HttpContext?.User?.FindFirst("user_id")?.Value;
            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取当前用户ID时发生异常");
            return null;
        }
    }
}
