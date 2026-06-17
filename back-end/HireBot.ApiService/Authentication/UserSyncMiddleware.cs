using HireBot.Abstraction.Infrastructure.Multitenancy;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HireBot.ApiService.Authentication;

/// <summary>
/// 用户同步中间件：在每次认证请求时将 JWT claims 中的用户信息同步到 AppUserEntity 表。
/// 多租户设计：同一个 Keycloak 用户在不同租户下有独立记录
/// 使用 IMemoryCache 限流：同一用户每 5 分钟最多同步一次，避免频繁写数据库。
/// </summary>
public sealed class UserSyncMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(
        HttpContext ctx, 
        HireBotDbContext db,
        ITenantContextProvider tenantContextProvider)
    {
        // 仅同步已认证用户
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var externalUserId = ctx.User.FindFirst("sub")?.Value
                                 ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(externalUserId))
            {
                var tenantId = tenantContextProvider.GetTenantId() ?? "default";
                var cacheKey = $"user-synced:{tenantId}:{externalUserId}";
                
                if (!cache.TryGetValue(cacheKey, out _))
                {
                    var username = ctx.User.FindFirst("preferred_username")?.Value
                                   ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                                   ?? externalUserId;
                    var displayName = ctx.User.FindFirst("name")?.Value ?? username;
                    var familyName = ctx.User.FindFirst("family_name")?.Value
                                     ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value;
                    var givenName = ctx.User.FindFirst("given_name")?.Value
                                    ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value;
                    var email = ctx.User.FindFirst("email")?.Value
                                ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                ?? string.Empty;

                    var now = DateTimeOffset.UtcNow;
                    
                    // 按 (TenantId, ExternalUserId) 查找用户
                    var existing = await db.AppUsers
                        .FirstOrDefaultAsync(u => 
                            u.ExternalUserId == externalUserId);
                    
                    if (existing is null)
                    {
                        db.AppUsers.Add(new AppUserEntity
                        {
                            Id = Guid.NewGuid().ToString(),
                            ExternalUserId = externalUserId,
                            TenantId = tenantId,
                            Username = username,
                            DisplayName = displayName,
                            FamilyName = familyName,
                            GivenName = givenName,
                            Email = email,
                            CreatedAt = now,
                            LastSeenAt = now,
                        });
                    }
                    else
                    {
                        // 更新可变字段（用户信息可能在 Keycloak 中被修改）
                        existing.Username = username;
                        existing.DisplayName = displayName;
                        existing.FamilyName = familyName;
                        existing.GivenName = givenName;
                        existing.Email = email;
                        existing.LastSeenAt = now;
                    }

                    try
                    {
                        await db.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        // 忽略并发竞态条件导致的重复插入（唯一约束冲突）
                    }

                    cache.Set(cacheKey, true, SyncInterval);
                }
            }
        }

        await next(ctx);
    }
}
