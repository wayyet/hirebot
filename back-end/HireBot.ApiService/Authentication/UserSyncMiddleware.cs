using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HireBot.ApiService.Authentication;

/// <summary>
/// 用户同步中间件：在每次认证请求时将 JWT claims 中的用户信息同步到 AppUserEntity 表。
/// 使用 IMemoryCache 限流：同一用户每 5 分钟最多同步一次，避免频繁写数据库。
/// </summary>
public sealed class UserSyncMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext ctx, HireBotDbContext db)
    {
        // 仅同步已认证用户
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var sub = ctx.User.FindFirst("sub")?.Value
                      ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(sub))
            {
                var cacheKey = $"user-synced:{sub}";
                if (!cache.TryGetValue(cacheKey, out _))
                {
                    var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "default";
                    var username = ctx.User.FindFirst("preferred_username")?.Value
                                   ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                                   ?? sub;
                    var displayName = ctx.User.FindFirst("name")?.Value ?? username;
                    var familyName = ctx.User.FindFirst("family_name")?.Value
                                     ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value;
                    var givenName = ctx.User.FindFirst("given_name")?.Value
                                    ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value;
                    var email = ctx.User.FindFirst("email")?.Value
                                ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                ?? string.Empty;

                    var now = DateTime.UtcNow;
                    var existing = await db.AppUsers.FindAsync(sub);
                    if (existing is null)
                    {
                        db.AppUsers.Add(new AppUserEntity
                        {
                            Id = sub,
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
                        existing.TenantId = tenantId;
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
                        // 忽略并发竞态条件导致的重复插入
                    }

                    cache.Set(cacheKey, true, SyncInterval);
                }
            }
        }

        await next(ctx);
    }
}
