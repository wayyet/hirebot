# AppUser 多租户优化设计文档

## 📋 问题背景

### 原设计的问题

HireBot 原有的 `AppUserEntity` 设计存在严重的多租户隔离问题：

```csharp
// ❌ 问题设计
public sealed class AppUserEntity
{
    public string Id { get; set; } = string.Empty;  // 使用 JWT sub 作为主键
    public string TenantId { get; set; } = string.Empty;
    // ...
}
```

**核心问题**：
1. **跨租户数据泄露**：同一个 Keycloak 用户在所有租户下共享同一条记录
2. **无法租户隔离**：无法为同一外部用户在不同租户下设置不同属性
3. **审计映射错误**：`CreatedByUserId` 无法正确映射到租户内用户
4. **违反多租户原则**：用户信息成为全局共享资源

### 实际场景示例

假设用户 `alice@company.com` 在 Keycloak 中的 sub 为 `alice-uuid-123`：

**原设计**：
- 租户A和租户B的 `AppUsers` 表中都只有一条记录：`Id='alice-uuid-123', TenantId='tenant-a'`
- 当 Alice 切换到租户B时，记录的 TenantId 会被更新为 `'tenant-b'`
- **问题**：租户A的历史数据失去用户关联！

**新设计**：
- 租户A：`Id='guid-1', ExternalUserId='alice-uuid-123', TenantId='tenant-a'`
- 租户B：`Id='guid-2', ExternalUserId='alice-uuid-123', TenantId='tenant-b'`
- **优势**：两个租户各自维护独立的用户记录

## ✅ 优化方案（参考 ncrew-builder）

### 新设计架构

```csharp
/// <summary>
/// 平台用户 — 从 JWT claims 同步入库，用于展示创建人/更新人信息
/// 多租户设计：同一个 Keycloak 用户在不同租户下有独立记录
/// 唯一约束：(TenantId, ExternalUserId)
/// </summary>
public sealed class AppUserEntity : ITenant, IPrimaryKey
{
    /// <summary>主键，全局唯一标识（使用 GUID）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>外部用户ID (JWT sub, Keycloak 用户唯一标识)</summary>
    public string ExternalUserId { get; set; } = string.Empty;

    /// <summary>租户ID，用于多租户数据隔离</summary>
    public string? TenantId { get; set; }

    // ... 其他字段
}
```

### 数据库设计

#### 主键策略
- **Id (PRIMARY KEY)**：使用 GUID，确保全局唯一性
- **ExternalUserId**：存储 JWT sub（Keycloak 用户ID）

#### 唯一约束
```sql
CREATE UNIQUE INDEX "IX_AppUsers_TenantId_ExternalUserId" 
    ON "AppUsers" ("TenantId", "ExternalUserId");
```

**约束说明**：同一租户下，一个外部用户只能有一条记录

#### 索引优化
```sql
-- 租户级查询
CREATE INDEX "IX_AppUsers_TenantId" ON "AppUsers" ("TenantId");

-- 用户名查询
CREATE INDEX "IX_AppUsers_TenantId_Username" 
    ON "AppUsers" ("TenantId", "Username");

-- 跨租户查找同一用户
CREATE INDEX "IX_AppUsers_ExternalUserId" 
    ON "AppUsers" ("ExternalUserId");
```

## 🔧 实现细节

### 1. UserSyncMiddleware 更新

#### 查询逻辑
```csharp
// 按 (TenantId, ExternalUserId) 查找用户
var existing = await db.AppUsers
    .FirstOrDefaultAsync(u => 
        u.TenantId == tenantId && 
        u.ExternalUserId == externalUserId);
```

#### 创建逻辑
```csharp
if (existing is null)
{
    db.AppUsers.Add(new AppUserEntity
    {
        Id = Guid.NewGuid().ToString(),           // 新的 GUID 主键
        ExternalUserId = externalUserId,          // JWT sub
        TenantId = tenantId,                      // 当前租户
        Username = username,
        DisplayName = displayName,
        Email = email,
        CreatedAt = now,
        LastSeenAt = now,
    });
}
else
{
    // 更新可变字段
    existing.Username = username;
    existing.DisplayName = displayName;
    existing.Email = email;
    existing.LastSeenAt = now;
}
```

#### 缓存键优化
```csharp
// 从 "user-synced:{sub}" 改为包含租户ID
var cacheKey = $"user-synced:{tenantId}:{externalUserId}";
```

**重要**：缓存键必须包含租户ID，否则跨租户访问时会跳过同步

### 2. 审计字段设计

#### CreatedByUserId / UpdatedByUserId 存储策略

**存储内容**：外部用户ID (JWT sub)，而非 AppUserEntity.Id

```csharp
// TenantSavingInterceptor 设置逻辑
createdInfo.CreatedByUserId = httpContext?.User?.FindFirst("sub")?.Value ?? "system";
```

**设计理由**：
1. ✅ **性能优化**：避免每次保存时查询数据库获取 AppUserEntity.Id
2. ✅ **语义清晰**：直接对应 Keycloak 用户ID，便于追溯
3. ✅ **解耦设计**：审计字段不依赖 AppUser 表的存在
4. ✅ **跨租户追溯**：可以查询同一外部用户在不同租户下的操作

#### 获取用户信息示例

```csharp
// 获取创建人信息
public async Task<UserDisplayInfo?> GetCreatorInfoAsync(string tenantId, string createdByUserId)
{
    return await db.AppUsers
        .Where(u => u.TenantId == tenantId && 
                    u.ExternalUserId == createdByUserId)
        .Select(u => new UserDisplayInfo
        {
            DisplayName = u.DisplayName,
            Username = u.Username,
            Email = u.Email
        })
        .FirstOrDefaultAsync();
}
```

## 📊 数据迁移

### 迁移策略

#### 方案1：全量迁移（推荐）
为现有数据中的每个 (TenantId, ExternalUserId) 组合创建独立记录

```sql
-- 创建新表结构
CREATE TABLE "AppUsers_New" (
    "Id" VARCHAR(36) PRIMARY KEY,
    "ExternalUserId" VARCHAR(256) NOT NULL,
    "TenantId" VARCHAR(128),
    "Username" VARCHAR(128) NOT NULL,
    "DisplayName" VARCHAR(256) NOT NULL,
    "Email" VARCHAR(256) NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "LastSeenAt" TIMESTAMPTZ NOT NULL
);

-- 迁移数据：原 Id 变成 ExternalUserId
INSERT INTO "AppUsers_New" 
    ("Id", "ExternalUserId", "TenantId", "Username", "DisplayName", 
     "Email", "CreatedAt", "LastSeenAt")
SELECT 
    gen_random_uuid()::text,  -- 新主键
    "Id",                     -- 原主键作为 ExternalUserId
    "TenantId",
    "Username",
    "DisplayName",
    "Email",
    "CreatedAt",
    "LastSeenAt"
FROM "AppUsers";

-- 创建索引
CREATE UNIQUE INDEX "IX_AppUsers_TenantId_ExternalUserId" 
    ON "AppUsers_New" ("TenantId", "ExternalUserId");

-- 替换表
DROP TABLE "AppUsers";
ALTER TABLE "AppUsers_New" RENAME TO "AppUsers";
```

#### 方案2：渐进式迁移
保留旧数据，让 UserSyncMiddleware 自然创建新记录

**优势**：无需停机，风险较低
**劣势**：历史数据无法立即查询

### 数据完整性验证

```sql
-- 验证唯一约束
SELECT "TenantId", "ExternalUserId", COUNT(*) 
FROM "AppUsers" 
GROUP BY "TenantId", "ExternalUserId" 
HAVING COUNT(*) > 1;
-- 应返回空结果

-- 统计租户用户数
SELECT "TenantId", COUNT(*) as UserCount
FROM "AppUsers"
GROUP BY "TenantId"
ORDER BY UserCount DESC;
```

## 🎯 使用指南

### 场景1：创建实体时关联用户

```csharp
public async Task<Instance> CreateInstanceAsync(CreateInstanceRequest request)
{
    var instance = new InstanceEntity
    {
        // ... 其他字段
        // 注意：CreatedByUserId 会由 TenantSavingInterceptor 自动设置为 JWT sub
    };

    db.Instances.Add(instance);
    await db.SaveChangesAsync();
    
    return instance;
}
```

### 场景2：显示创建人信息

```csharp
public async Task<InstanceDetailDto> GetInstanceDetailAsync(string instanceId)
{
    var instance = await db.Instances.FindAsync(instanceId);
    
    // 通过 (TenantId, ExternalUserId) 联表查询创建人
    var creator = await db.AppUsers
        .Where(u => u.TenantId == instance.TenantId && 
                    u.ExternalUserId == instance.CreatedByUserId)
        .FirstOrDefaultAsync();
    
    return new InstanceDetailDto
    {
        // ... 其他字段
        CreatorName = creator?.DisplayName ?? "Unknown",
        CreatorEmail = creator?.Email
    };
}
```

### 场景3：批量查询用户信息

```csharp
public async Task<List<InstanceWithCreatorDto>> GetInstancesWithCreatorsAsync(string tenantId)
{
    return await db.Instances
        .Where(i => i.TenantId == tenantId)
        .Join(db.AppUsers,
            instance => new { instance.TenantId, ExternalUserId = instance.CreatedByUserId },
            user => new { user.TenantId, user.ExternalUserId },
            (instance, user) => new InstanceWithCreatorDto
            {
                InstanceId = instance.InstanceId,
                InstanceName = instance.Name,
                CreatorName = user.DisplayName,
                CreatorEmail = user.Email
            })
        .ToListAsync();
}
```

## ⚠️ 注意事项

### 1. 性能考虑
- 联表查询用户信息时使用索引：`IX_AppUsers_TenantId_ExternalUserId`
- 频繁访问的用户信息可考虑缓存

### 2. 并发处理
UserSyncMiddleware 使用 try-catch 捕获唯一约束冲突：
```csharp
try
{
    await db.SaveChangesAsync();
}
catch (DbUpdateException)
{
    // 忽略并发导致的重复插入
}
```

### 3. 数据一致性
- 同一外部用户可以在多个租户下存在
- 每个租户内，用户名建议唯一但不强制
- 外部用户信息（Email、DisplayName）变更会在下次登录时同步

### 4. 审计追溯
CreatedByUserId 存储的是外部用户ID，需要联表查询才能获取用户详情：
```csharp
// 不推荐：直接使用 CreatedByUserId 显示
errorMessage = $"Created by: {entity.CreatedByUserId}";  // 显示 UUID

// 推荐：联表查询后显示
var creator = await GetUserByExternalId(entity.TenantId, entity.CreatedByUserId);
errorMessage = $"Created by: {creator?.DisplayName ?? entity.CreatedByUserId}";
```

## 📚 参考实现

本设计参考了 ncrew-builder 项目的最佳实践：
- **文件**：`D:\gitee-ai4c\ncrew-builder\api\Data\Entities\AppUser.cs`
- **DbContext**：`D:\gitee-ai4c\ncrew-builder\api\Data\AppDbContext.cs` (行 283-296)

### ncrew-builder 的设计亮点

```csharp
// 清晰的注释说明设计意图
/// <summary>
/// 平台用户 — 从 JWT claims 同步入库，用于展示创建人/更新人信息
/// 多租户设计：同一个 Keycloak 用户在不同租户下有独立记录
/// 唯一约束：(TenantId, ExternalUserId)
/// </summary>
public sealed class AppUser : NCrewBaseEntity
{
    /// <summary>外部用户ID (JWT sub, Keycloak 用户唯一标识)</summary>
    public string ExternalUserId { get; set; } = string.Empty;
    // ...
}

// 数据库配置
m.Entity<AppUser>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.ExternalUserId }).IsUnique();
});
```

## ✅ 验收标准

- [x] AppUserEntity 使用独立 GUID 主键
- [x] 添加 ExternalUserId 字段存储 JWT sub
- [x] 实现 ITenant 接口
- [x] 数据库添加 (TenantId, ExternalUserId) 唯一约束
- [x] UserSyncMiddleware 按租户查询用户
- [x] UserSyncMiddleware 缓存键包含租户ID
- [x] 审计字段存储外部用户ID
- [x] 编译通过无错误
- [x] 文档完整覆盖设计理念和使用方法
