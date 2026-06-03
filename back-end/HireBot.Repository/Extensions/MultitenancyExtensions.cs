using System.Linq.Expressions;
using HireBot.Abstraction.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Repository.Extensions;

/// <summary>
/// 多租户扩展方法
/// 提供自动应用租户查询过滤器的功能
/// </summary>
public static class MultitenancyExtensions
{
    /// <summary>
    /// 为所有实现 ITenant 接口的实体配置全局查询过滤器
    /// </summary>
    /// <param name="modelBuilder">模型构建器</param>
    /// <param name="tenantIdAccessor">租户ID访问器函数</param>
    public static void ApplyTenantQueryFilters(
        this ModelBuilder modelBuilder,
        Func<string?> tenantIdAccessor)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 只处理实现了 ITenant 接口的实体
            if (!typeof(ITenant).IsAssignableFrom(entityType.ClrType))
                continue;

            // 获取实体类型
            var clrType = entityType.ClrType;

            // 创建过滤器表达式
            var filterExpression = CreateTenantFilterExpression(clrType, tenantIdAccessor);

            // 应用查询过滤器
            modelBuilder.Entity(clrType).HasQueryFilter(filterExpression);
        }
    }

    /// <summary>
    /// 创建租户过滤器表达式
    /// 生成类似 e => e.TenantId == currentTenantId 的表达式
    /// </summary>
    private static LambdaExpression CreateTenantFilterExpression(
        Type entityType,
        Func<string?> tenantIdAccessor)
    {
        // 创建参数: e
        var parameter = Expression.Parameter(entityType, "e");

        // 访问属性: e.TenantId
        var tenantIdProperty = Expression.Property(parameter, nameof(ITenant.TenantId));

        // 获取当前租户ID的常量表达式
        var currentTenantIdExpression = Expression.Invoke(
            Expression.Constant(tenantIdAccessor));

        // 构建比较表达式: e.TenantId == currentTenantId
        var equalExpression = Expression.Equal(tenantIdProperty, currentTenantIdExpression);

        // 如果 TenantId 可为 null，还需要支持全局数据（TenantId == null）
        // 构建: e.TenantId == currentTenantId || e.TenantId == null
        var nullCheckExpression = Expression.Equal(
            tenantIdProperty,
            Expression.Constant(null, typeof(string)));

        var finalExpression = Expression.OrElse(equalExpression, nullCheckExpression);

        // 返回 Lambda 表达式
        return Expression.Lambda(finalExpression, parameter);
    }

    /// <summary>
    /// 简化版：为特定实体类型配置租户过滤器
    /// </summary>
    public static void ApplyTenantFilterFor<TEntity>(
        this ModelBuilder modelBuilder,
        Func<string?> tenantIdAccessor)
        where TEntity : class, ITenant
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.TenantId == tenantIdAccessor() || e.TenantId == null);
    }
}
