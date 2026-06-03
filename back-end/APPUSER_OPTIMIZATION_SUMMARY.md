# 多租户用户表优化总结

## 🎯 核心问题

**原设计的严重缺陷**：使用 JWT sub 作为主键，导致同一个 Keycloak 用户在所有租户下共享同一条记录，违反多租户隔离原则。

## ✅ 优化方案（参考 ncrew-builder）

### 架构变更

```
原设计 (❌)                          新设计 (✅)
┌────────────────────┐              ┌────────────────────┐
│ AppUserEntity      │              │ AppUserEntity      │
├────────────────────┤              ├────────────────────┤
│ Id (PK)            │ JWT sub      │ Id (PK)            │ GUID
│ TenantId           │              │ ExternalUserId     │ JWT sub
│ Username           │              │ TenantId           │
│ ...                │              │ Username           │
└────────────────────┘              │ ...                │
                                    └────────────────────┘
问题：                              约束：
- 跨租户数据泄露                    - UNIQUE (TenantId, ExternalUserId)
- 租户切换时数据丢失                - 同一用户在不同租户有独立记录
```

### 关键改动

| 项目 | 原设计 | 新设计 | 说明 |
|------|--------|--------|------|
| 主键 | `Id = JWT sub` | `Id = GUID` | 全局唯一标识 |
| 外部用户ID | 无 | `ExternalUserId = JWT sub` | Keycloak 用户ID |
| 唯一约束 | 无 | `(TenantId, ExternalUserId)` | 租户内用户唯一 |
| 接口实现 | 无 | `ITenant, IPrimaryKey` | 多租户支持 |
| 查询逻辑 | `FindAsync(sub)` | `FirstOrDefaultAsync(u => u.TenantId == tid && u.ExternalUserId == sub)` | 按租户查询 |
| 缓存键 | `user-synced:{sub}` | `user-synced:{tid}:{sub}` | 租户隔离 |

## 📁 修改的文件

### 1. HireBot.Repository/Entities/AppUserEntity.cs
```csharp
// 添加接口实现和新字段
public sealed class AppUserEntity : ITenant, IPrimaryKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ExternalUserId { get; set; } = string.Empty;  // 新增
    public string? TenantId { get; set; }
    // ...
}
```

### 2. HireBot.Repository/HireBotDbContext.cs
```csharp
// 更新实体配置
modelBuilder.Entity<AppUserEntity>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasMaxLength(36).ValueGeneratedNever();
    entity.Property(e => e.ExternalUserId).IsRequired().HasMaxLength(256);
    
    // 唯一约束：同一租户下，外部用户ID唯一
    entity.HasIndex(e => new { e.TenantId, e.ExternalUserId }).IsUnique();
    // ...
});
```

### 3. HireBot.ApiService/Authentication/UserSyncMiddleware.cs
```csharp
// 更新查询逻辑
var existing = await db.AppUsers
    .FirstOrDefaultAsync(u => 
        u.TenantId == tenantId && 
        u.ExternalUserId == externalUserId);

// 创建时使用新主键
db.AppUsers.Add(new AppUserEntity
{
    Id = Guid.NewGuid().ToString(),
    ExternalUserId = externalUserId,
    TenantId = tenantId,
    // ...
});

// 缓存键包含租户ID
var cacheKey = $"user-synced:{tenantId}:{externalUserId}";
```

## 📊 数据迁移

**重要**：需要执行数据库迁移脚本重构表结构

```sql
-- 1. 创建新表（含新主键和唯一约束）
-- 2. 迁移数据（原 Id → ExternalUserId）
-- 3. 替换旧表
-- 详见：MULTITENANCY_IMPLEMENTATION.md
```

## 🎓 设计理念

### 审计字段存储策略

**CreatedByUserId / UpdatedByUserId 存储外部用户ID (JWT sub)**，而非 AppUserEntity.Id

**理由**：
- ✅ 性能：避免每次保存时查询数据库
- ✅ 语义清晰：直接对应 Keycloak 用户
- ✅ 解耦：不依赖 AppUser 表
- ✅ 可追溯：可查询同一用户在不同租户的操作

**使用方式**：
```csharp
// 联表查询获取用户信息
var creator = await db.AppUsers
    .Where(u => u.TenantId == entity.TenantId && 
                u.ExternalUserId == entity.CreatedByUserId)
    .FirstOrDefaultAsync();
```

## 📚 参考文档

- **详细设计**：`APPUSER_MULTITENANCY_OPTIMIZATION.md`
- **完整实现**：`MULTITENANCY_IMPLEMENTATION.md`
- **参考项目**：`D:\gitee-ai4c\ncrew-builder\api\Data\Entities\AppUser.cs`

## ✅ 验证结果

- [x] 所有项目编译成功
- [x] 多租户隔离逻辑正确
- [x] 用户同步中间件支持租户隔离
- [x] 数据库设计符合最佳实践
- [x] 文档完整覆盖设计和使用方法
