using System.Linq.Expressions;
using HireBot.Abstraction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
    /// <param name="tenantIdAccessor">租户ID访问器表达式</param>
    public static void ApplyTenantQueryFilters(
        this ModelBuilder modelBuilder,
        Expression<Func<string?>> tenantIdAccessor)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 跳过以下类型:
            // 1. 未实现 ITenant 接口
            // 2. 抽象类型(无法实例化)
            // 3. 拥有类型(Owned Entity Types, 通过父实体访问)
            if (!typeof(ITenant).IsAssignableFrom(entityType.ClrType) ||
                entityType.ClrType.IsAbstract ||
                entityType.IsOwned())
                continue;

            // 获取实体类型
            var clrType = entityType.ClrType;

            // 创建过滤器表达式
            var filterExpression = CreateTenantFilterExpression(clrType, tenantIdAccessor);

            // EF Core 10注意: GetDeclaredQueryFilters() 返回的 IQueryFilter 接口在 10.0 中发生了变化
            // 如果实体已有过滤器,可能需要手动合并。这里简化处理:直接应用租户过滤器
            // 如需保留原有过滤器,请在 OnModelCreating 中先配置原过滤器,再调用 ApplyTenantQueryFilters

            // 应用查询过滤器
            modelBuilder.Entity(clrType).HasQueryFilter(filterExpression);
        }
    }

    /// <summary>
    /// 创建租户过滤器表达式。
    /// 生成类似 e => e.TenantId == currentTenantId 的表达式。
    /// </summary>
    private static LambdaExpression CreateTenantFilterExpression(
        Type entityType,
        Expression<Func<string?>> tenantIdAccessor)
    {
        // 创建参数: e
        var parameter = Expression.Parameter(entityType, "e");

        // 访问属性: e.TenantId
        var tenantIdProperty = Expression.Property(parameter, nameof(ITenant.TenantId));

        // 直接嵌入 DbContext.TenantId 属性访问表达式，避免把租户访问器固化为普通 delegate。
        var currentTenantIdExpression = tenantIdAccessor.Body;

        // 构建比较表达式: e.TenantId == currentTenantId
        var finalExpression = Expression.Equal(tenantIdProperty, currentTenantIdExpression);

        // 返回 Lambda 表达式
        return Expression.Lambda(finalExpression, parameter);
    }

    /// <summary>
    /// 合并两个查询过滤器表达式 (existing && tenant)
    /// </summary>
    private static LambdaExpression CombineFilters(
        LambdaExpression existingFilter,
        LambdaExpression tenantFilter)
    {
        var parameter = existingFilter.Parameters[0];

        // 替换 tenantFilter 的参数为 existingFilter 的参数
        var tenantBody = new ParameterReplacerVisitor(tenantFilter.Parameters[0], parameter)
            .Visit(tenantFilter.Body);

        // 合并: existing && tenant
        var combined = Expression.AndAlso(existingFilter.Body, tenantBody!);

        return Expression.Lambda(combined, parameter);
    }

    /// <summary>
    /// 简化版:为特定实体类型配置租户过滤器
    /// </summary>
    public static void ApplyTenantFilterFor<TEntity>(
        this ModelBuilder modelBuilder,
        Expression<Func<string?>> tenantIdAccessor)
        where TEntity : class, ITenant
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter((Expression<Func<TEntity, bool>>)CreateTenantFilterExpression(typeof(TEntity), tenantIdAccessor));
    }

    /// <summary>
    /// 参数替换访问器,用于合并过滤器时统一参数
    /// </summary>
    private class ParameterReplacerVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public ParameterReplacerVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParameter ? _newParameter : base.VisitParameter(node);
        }
    }
}
