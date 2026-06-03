# 数据库迁移脚本说明

## 📋 迁移文件清单

### 20260603_AddMultitenancySupport

此迁移为 HireBot 项目添加完整的多租户支持。

#### 文件说明

1. **20260603_AddMultitenancySupport.sql** （幂等脚本）
   - 完整的幂等性SQL脚本
   - 包含所有历史迁移
   - 可在任何状态的数据库上安全执行
   - 适用于生产环境部署

2. **20260603_AddMultitenancySupport_clean.sql** （增量脚本）⭐
   - 仅包含本次多租户迁移的SQL
   - 更简洁，易于阅读和审查
   - 适用于开发环境快速应用
   - **推荐用于首次执行**

## 🎯 迁移内容

### 新增功能

#### 1. AppUsers 多租户用户表
- 使用独立的 GUID 主键
- `ExternalUserId` 存储 Keycloak 的 JWT sub
- 唯一约束：`(TenantId, ExternalUserId)`
- 同一个 Keycloak 用户在不同租户下有独立记录

#### 2. 实体添加 TenantId 字段
为以下实体添加了 TenantId 支持：
- ✅ SandboxSessions
- ✅ SandboxAssets
- ✅ HiringRuntimeStates
- ✅ HiringAuditLogs
- ✅ HiringArtifactUploads
- ✅ HiringArtifactUploadParts
- ✅ HiringArtifacts
- ✅ EvaluationWorkspaceStates（同时添加了 Id 主键）
- ✅ EvaluationSessions
- ✅ EvaluationReports
- ✅ EvaluationAssets

#### 3. 索引优化
- 所有多租户查询都添加了 `TenantId` 前缀索引
- 提升租户隔离查询性能
- 确保唯一约束包含租户维度

## 📝 使用说明

### 方式1：使用 EF Core 命令（推荐）

```bash
# 在项目根目录执行
cd back-end

# 应用迁移（自动执行到最新）
dotnet ef database update --project HireBot.Repository --startup-project HireBot.ApiService

# 查看迁移状态
dotnet ef migrations list --project HireBot.Repository --startup-project HireBot.ApiService
```

### 方式2：直接执行 SQL 脚本

#### 开发环境（首次执行）

```bash
# 使用简洁的增量脚本
psql -U your_username -d your_database -f scripts/migrations/20260603_AddMultitenancySupport_clean.sql
```

#### 生产环境（已有数据库）

```bash
# 使用幂等脚本，安全可重复执行
psql -U your_username -d your_database -f scripts/migrations/20260603_AddMultitenancySupport.sql
```

### 方式3：在应用程序启动时自动迁移

在 `Program.cs` 中添加：

```csharp
// 自动应用数据库迁移
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HireBotDbContext>();
    db.Database.Migrate();
}
```

## ⚠️ 注意事项

### 执行前检查

1. **备份数据库**
   ```bash
   pg_dump -U username -d database_name > backup_before_multitenancy.sql
   ```

2. **检查现有数据**
   - 确认所有现有数据的租户归属
   - 默认值 `'default'` 适用于单租户场景

3. **停止应用服务**
   - 建议在维护窗口执行
   - 避免迁移过程中有新数据写入

### 执行后验证

1. **检查表结构**
   ```sql
   -- 查看新增的 TenantId 字段
   SELECT table_name, column_name, data_type 
   FROM information_schema.columns 
   WHERE column_name = 'TenantId';
   ```

2. **验证索引**
   ```sql
   -- 查看新增的多租户索引
   SELECT indexname, tablename 
   FROM pg_indexes 
   WHERE indexname LIKE '%TenantId%';
   ```

3. **检查 AppUsers 表**
   ```sql
   -- 验证唯一约束
   SELECT * FROM pg_indexes WHERE tablename = 'AppUsers';
   ```

4. **测试查询性能**
   ```sql
   -- 测试租户过滤查询
   EXPLAIN ANALYZE 
   SELECT * FROM "HiringArtifacts" 
   WHERE "TenantId" = 'default' AND "SessionId" = 'test-session';
   ```

## 🔄 回滚说明

### 使用 EF Core 回滚

```bash
# 回滚到上一个迁移
cd back-end
dotnet ef database update AddWorkspaceRelativePathToHiringMaterialFiles \
  --project HireBot.Repository --startup-project HireBot.ApiService
```

### 手动回滚 SQL

如需手动回滚，执行以下步骤：

```sql
START TRANSACTION;

-- 1. 删除 AppUsers 表
DROP TABLE IF EXISTS "AppUsers";

-- 2. 删除所有多租户索引
DROP INDEX IF EXISTS "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~";
DROP INDEX IF EXISTS "IX_SandboxAssets_TenantId";
-- ... (所有其他租户相关索引)

-- 3. 删除 TenantId 列
ALTER TABLE "SandboxSessions" DROP COLUMN "TenantId";
ALTER TABLE "SandboxAssets" DROP COLUMN "TenantId";
-- ... (所有其他表)

-- 4. 恢复 EvaluationWorkspaceStates 的旧主键
ALTER TABLE "EvaluationWorkspaceStates" DROP CONSTRAINT "PK_EvaluationWorkspaceStates";
ALTER TABLE "EvaluationWorkspaceStates" DROP COLUMN "Id";
ALTER TABLE "EvaluationWorkspaceStates" DROP COLUMN "TenantId";
-- 添加原有的复合主键
ALTER TABLE "EvaluationWorkspaceStates" ADD PRIMARY KEY ("OwnerSubject", "EmployeeId");

-- 5. 删除迁移记录
DELETE FROM "__EFMigrationsHistory" 
WHERE "MigrationId" = '20260603165207_AddMultitenancySupport';

COMMIT;
```

## 📊 数据迁移建议

### 场景1：全新数据库
- 直接执行迁移脚本
- 所有新数据自动设置为 `'default'` 租户

### 场景2：已有数据迁移到多租户

如果需要将现有数据分配到不同租户，执行迁移后运行：

```sql
-- 示例：将特定用户的数据分配到特定租户
UPDATE "HiringArtifacts" 
SET "TenantId" = 'tenant-a' 
WHERE "SessionId" IN (
    SELECT "SessionId" FROM "HiringSessions" 
    WHERE "OwnerSubject" = 'user-a'
);

-- 为 AppUsers 创建租户记录
INSERT INTO "AppUsers" ("Id", "ExternalUserId", "TenantId", "Username", "DisplayName", "Email", "CreatedAt", "LastSeenAt")
SELECT 
    gen_random_uuid()::text,
    "sub_from_jwt",
    'tenant-a',
    "username",
    "display_name",
    "email",
    NOW(),
    NOW()
FROM your_source_table;
```

## 🎯 后续配置

### 1. Keycloak 配置

在 Keycloak 中配置 Mapper，将租户ID添加到 JWT Token：

```
Mapper Type: User Attribute
User Attribute: tenant_id
Token Claim Name: tenant_id
Claim JSON Type: String
Add to ID token: ON
Add to access token: ON
Add to userinfo: ON
```

### 2. 应用程序配置

确保以下服务已注册（已在 ServiceExtensions.cs 中完成）：

```csharp
services.AddScoped<ITenantContextProvider, TenantContextProvider>();
services.AddHttpContextAccessor();
```

### 3. 测试清单

- [ ] 用户登录后 TenantId 正确设置
- [ ] 查询只返回当前租户的数据
- [ ] 跨租户数据完全隔离
- [ ] AppUsers 表正确创建租户级用户记录
- [ ] 审计字段（CreatedByUserId）正确记录外部用户ID

## 📚 相关文档

- [多租户实现总结](../MULTITENANCY_IMPLEMENTATION.md)
- [用户表优化文档](../APPUSER_MULTITENANCY_OPTIMIZATION.md)
- [用户表优化总结](../APPUSER_OPTIMIZATION_SUMMARY.md)

## ❓ 常见问题

### Q: 为什么 TenantId 默认值是 'default'？
A: 为了兼容单租户场景和现有数据。实际使用时会从 JWT Token 中获取真实的租户ID。

### Q: AppUsers 表和其他用户表的关系？
A: AppUsers 是新的多租户用户表，用于存储用户在各租户下的信息。原有的 Users 表可以保留作为系统管理员账号。

### Q: 如何处理租户ID为 null 的全局数据？
A: TenantId 为 null 的数据对所有租户可见，适用于系统配置、全局资源等场景。

### Q: 查询过滤器会影响性能吗？
A: 不会。EF Core 的全局查询过滤器在编译时就确定，与手动添加 WHERE 条件性能相同。

## 📞 支持

如遇到问题，请查看：
1. 项目文档目录下的多租户相关文档
2. EF Core Migrations 官方文档
3. PostgreSQL 数据库日志

---

**最后更新**: 2026-06-03  
**迁移版本**: 20260603165207_AddMultitenancySupport  
**EF Core 版本**: 10.0.7
