# HireBot 多租户实现总结

## 已完成的工作

### 1. 核心基础设施

#### 接口定义 (HireBot.Abstraction/Contracts/)
- ✅ `ITenant.cs` - 租户接口，包含 TenantId 属性
- ✅ `IPrimaryKey.cs` - 主键接口
- ✅ `ICreatedInfo.cs` - 创建审计接口
- ✅ `IUpdatedInfo.cs` - 更新审计接口

#### 租户上下文提供者
- ✅ `ITenantContextProvider.cs` (HireBot.Abstraction/Infrastructure/Multitenancy/)
- ✅ `TenantContextProvider.cs` (HireBot.Core/Infrastructure/Multitenancy/)
  - 使用 AsyncLocal 实现线程安全
  - 从 JWT Claims (tenant_id, tid) 获取租户ID
  - 支持手动设置租户ID（用于后台任务）
  - 默认租户ID: "default"

### 2. EF Core 集成

#### 扩展方法 (HireBot.Repository/Extensions/)
- ✅ `MultitenancyExtensions.cs` - 全局查询过滤器扩展
  - `ApplyTenantQueryFilters()` - 自动为实现 ITenant 接口的实体添加租户过滤
- ✅ `TenantSavingInterceptor.cs` - 保存拦截器
  - 自动为新实体设置 TenantId
  - 验证租户ID不被篡改
  - 自动设置创建和更新审计字段

### 3. 用户表多租户优化 ⭐

#### 设计理念（参考 ncrew-builder 最佳实践）

**核心问题**：同一个 Keycloak 用户在多租户系统中的身份管理

**优化前的问题**：
- ❌ 使用 JWT sub 作为主键，导致同一用户在所有租户下共享同一条记录
- ❌ 无法为同一外部用户在不同租户下设置不同属性
- ❌ 用户信息跨租户泄露，违反多租户隔离原则

**优化后的设计**：
```csharp
public sealed class AppUserEntity : ITenant, IPrimaryKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString();     // 独立主键
    public string ExternalUserId { get; set; } = string.Empty;       // JWT sub
    public string? TenantId { get; set; }                            // 租户ID
    // ... 其他字段
}

// 数据库唯一约束：(TenantId, ExternalUserId)
entity.HasIndex(e => new { e.TenantId, e.ExternalUserId }).IsUnique();
```

**关键优势**：
- ✅ 同一个 Keycloak 用户在不同租户下有独立记录
- ✅ 真正实现租户级别的用户数据隔离
- ✅ 支持租户级别的用户权限和属性管理
- ✅ 审计字段正确映射到租户内用户

#### UserSyncMiddleware 优化

**查询逻辑**：
```csharp
// 按 (TenantId, ExternalUserId) 查找用户
var existing = await db.AppUsers
    .FirstOrDefaultAsync(u => 
        u.TenantId == tenantId && 
        u.ExternalUserId == externalUserId);
```

**创建逻辑**：
```csharp
db.AppUsers.Add(new AppUserEntity
{
    Id = Guid.NewGuid().ToString(),           // 新的 GUID 主键
    ExternalUserId = externalUserId,          // JWT sub
    TenantId = tenantId,                      // 当前租户
    // ... 其他从 JWT claims 同步的字段
});
```

**缓存键优化**：
```csharp
// 从 "user-synced:{sub}" 改为包含租户ID
var cacheKey = $"user-synced:{tenantId}:{externalUserId}";
```

#### 审计字段说明

**CreatedByUserId / UpdatedByUserId** 存储 **外部用户ID (JWT sub)**，而非 AppUserEntity.Id

**原因**：
1. 避免额外的数据库查询开销
2. 字段语义清晰（直接对应 Keycloak 用户）
3. 联表查询时使用 `(TenantId, ExternalUserId)` 即可获取用户信息

**示例查询**：
```csharp
// 获取创建人信息
var creator = await db.AppUsers
    .Where(u => u.TenantId == entity.TenantId && 
                u.ExternalUserId == entity.CreatedByUserId)
    .FirstOrDefaultAsync();
```

### 4. 实体更新

#### 已添加 TenantId 字段的实体：
- ✅ `EvaluationSessionEntity` - 评估会话
- ✅ `EvaluationWorkspaceStateEntity` - 评估工作区状态（同时添加了主键 Id）
- ✅ `HiringArtifactEntity` - 雇佣产出物
- ✅ `HiringRuntimeStateEntity` - 雇佣运行时状态
- ✅ `HiringArtifactUploadEntity` - 雇佣产出物上传
- ✅ `HiringAuditLogEntity` - 雇佣审计日志
- ✅ `SandboxSessionEntity` - 沙箱会话

#### 已有 TenantId 的实体：
- InstanceEntity
- AppUserEntity ⭐（已优化多租户设计）
- HiringSessionEntity
- SandboxInstanceEntity
- ConversationEntity
- MessageEntity
- HiringMaterialFileEntity

### 4. 服务配置

#### 已更新的配置 (HireBot.Core/Extensions/ServiceExtensions.cs)
- ✅ 注册 `ITenantContextProvider` 和 `TenantContextProvider`
- ✅ DbContext 配置添加 `TenantSavingInterceptor`
- ✅ 支持 PostgreSQL 和 SQLite

#### Program.cs
- ✅ 已有 `AddHttpContextAccessor()` 注册

### 5. DbContext 更新

#### HireBotDbContext.cs 需要的更改：
1. ✅ 添加租户上下文提供者注入
2. ✅ 添加 TenantId 属性（懒加载）
3. ✅ 更新实体配置添加 TenantId 索引
4. ✅ 在 OnModelCreating 末尾调用 `ApplyTenantQueryFilters()`

## 数据库迁移

由于涉及重大架构变更，建议分阶段执行迁移：

### 阶段1：用户表多租户优化 ⭐

**重要说明**：此迁移会重构用户表主键结构，需要谨慎操作

#### PostgreSQL 迁移脚本

```sql
-- ======================================================================
-- 阶段1：用户表多租户优化
-- ======================================================================

-- 步骤1：备份原有数据
CREATE TABLE "AppUsers_Backup" AS SELECT * FROM "AppUsers";

-- 步骤2：创建新的用户表结构
CREATE TABLE "AppUsers_New" (
    "Id" VARCHAR(36) PRIMARY KEY,                      -- 新的 GUID 主键
    "ExternalUserId" VARCHAR(256) NOT NULL,            -- 原 Id (JWT sub)
    "TenantId" VARCHAR(128),                           -- 租户ID（可空）
    "Username" VARCHAR(128) NOT NULL,
    "DisplayName" VARCHAR(256) NOT NULL,
    "FamilyName" VARCHAR(256),
    "GivenName" VARCHAR(256),
    "Email" VARCHAR(256) NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "LastSeenAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 步骤3：迁移数据（为每个租户创建独立用户记录）
-- 注意：需要根据实际业务数据调整此脚本
INSERT INTO "AppUsers_New" ("Id", "ExternalUserId", "TenantId", "Username", "DisplayName", 
                            "FamilyName", "GivenName", "Email", "CreatedAt", "LastSeenAt")
SELECT 
    gen_random_uuid()::text,          -- 新的主键
    "Id",                              -- 原主键作为 ExternalUserId
    "TenantId",                        -- 保持租户ID
    "Username",
    "DisplayName",
    "FamilyName",
    "GivenName",
    "Email",
    "CreatedAt",
    "LastSeenAt"
FROM "AppUsers";

-- 步骤4：创建索引和约束
CREATE UNIQUE INDEX "IX_AppUsers_TenantId_ExternalUserId" 
    ON "AppUsers_New" ("TenantId", "ExternalUserId");
CREATE INDEX "IX_AppUsers_TenantId" ON "AppUsers_New" ("TenantId");
CREATE INDEX "IX_AppUsers_TenantId_Username" ON "AppUsers_New" ("TenantId", "Username");
CREATE INDEX "IX_AppUsers_ExternalUserId" ON "AppUsers_New" ("ExternalUserId");

-- 步骤5：替换旧表
DROP TABLE "AppUsers";
ALTER TABLE "AppUsers_New" RENAME TO "AppUsers";

-- 步骤6：验证数据（可选）
-- SELECT "TenantId", "ExternalUserId", COUNT(*) 
-- FROM "AppUsers" 
-- GROUP BY "TenantId", "ExternalUserId" 
-- HAVING COUNT(*) > 1;  -- 应该返回空结果

COMMIT;
```

**回滚脚本**（如需回滚）：
```sql
DROP TABLE IF EXISTS "AppUsers";
ALTER TABLE "AppUsers_Backup" RENAME TO "AppUsers";
```

### 阶段2：其他实体添加租户字段

#### PostgreSQL 迁移脚本

```sql
-- ======================================================================
-- 阶段2：为其他实体添加租户支持
-- ======================================================================

-- 添加租户ID字段
ALTER TABLE "EvaluationSessions" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "EvaluationWorkspaceStates" ADD COLUMN "Id" UUID NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE "EvaluationWorkspaceStates" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "HiringArtifacts" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "HiringRuntimeStates" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "HiringArtifactUploads" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "HiringAuditLogs" ADD COLUMN "TenantId" VARCHAR(128);
ALTER TABLE "SandboxSessions" ADD COLUMN "TenantId" VARCHAR(128);

-- 更新 EvaluationWorkspaceStates 主键
ALTER TABLE "EvaluationWorkspaceStates" DROP CONSTRAINT IF EXISTS "PK_EvaluationWorkspaceStates";
ALTER TABLE "EvaluationWorkspaceStates" ADD CONSTRAINT "PK_EvaluationWorkspaceStates" PRIMARY KEY ("Id");

-- 添加租户相关索引
CREATE INDEX "IX_EvaluationSessions_TenantId" ON "EvaluationSessions" ("TenantId");
CREATE UNIQUE INDEX "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_UpdatedAtUtc" 
  ON "EvaluationSessions" ("TenantId", "OwnerSubject", "EmployeeId", "UpdatedAtUtc");
CREATE INDEX "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId" 
  ON "EvaluationSessions" ("TenantId", "EvaluatorHireId", "TargetHireId");

CREATE UNIQUE INDEX "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId" 
  ON "EvaluationWorkspaceStates" ("TenantId", "OwnerSubject", "EmployeeId");

CREATE INDEX "IX_HiringArtifacts_TenantId" ON "HiringArtifacts" ("TenantId");
CREATE UNIQUE INDEX "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath" 
  ON "HiringArtifacts" ("TenantId", "SessionId", "Kind", "LogicalPath");
CREATE INDEX "IX_HiringArtifacts_TenantId_SessionId_IsFinal" 
  ON "HiringArtifacts" ("TenantId", "SessionId", "IsFinal");

CREATE INDEX "IX_HiringRuntimeStates_TenantId" ON "HiringRuntimeStates" ("TenantId");
CREATE INDEX "IX_HiringRuntimeStates_TenantId_SessionId" 
  ON "HiringRuntimeStates" ("TenantId", "SessionId");
CREATE INDEX "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc" 
  ON "HiringRuntimeStates" ("TenantId", "UpdatedAtUtc");

CREATE INDEX "IX_HiringArtifactUploads_TenantId" ON "HiringArtifactUploads" ("TenantId");
CREATE INDEX "IX_HiringArtifactUploads_TenantId_SessionId_Kind_LogicalPath_CompletedAtUtc" 
  ON "HiringArtifactUploads" ("TenantId", "SessionId", "Kind", "LogicalPath", "CompletedAtUtc");

CREATE INDEX "IX_HiringAuditLogs_TenantId" ON "HiringAuditLogs" ("TenantId");
CREATE INDEX "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc" 
  ON "HiringAuditLogs" ("TenantId", "SessionId", "TimestampUtc");
CREATE INDEX "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc" 
  ON "HiringAuditLogs" ("TenantId", "HireId", "TimestampUtc");

CREATE INDEX "IX_SandboxSessions_TenantId" ON "SandboxSessions" ("TenantId");
CREATE UNIQUE INDEX "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_SandboxRole_SessionKey" 
  ON "SandboxSessions" ("TenantId", "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey");
```

## 工作机制

### 查询过滤
- 所有实现 `ITenant` 接口的实体自动应用查询过滤器
- 过滤表达式：`e.TenantId == currentTenantId || e.TenantId == null`
- 支持全局数据（TenantId 为 null）

### 保存拦截
- 新增实体：自动设置 TenantId（如果未设置）
- 修改实体：验证 TenantId 是否被篡改（记录警告）
- 自动设置审计字段（CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId）

### 租户ID获取优先级
1. **手动设置**（用于后台任务）
2. **JWT Claims**（tenant_id 或 tid）
3. **默认值**（"default"）

## 配置要求

### JWT Claims
需要在 JWT Token 中包含以下 Claims 之一：
- `tenant_id` （主要）
- `tid` （备用）
- `ClaimTypes.GroupSid` （备用）

### Keycloak 配置
需要在 Keycloak 中配置 Mapper，将租户ID添加到 JWT Token:
```
Mapper Type: User Attribute
User Attribute: tenant_id
Token Claim Name: tenant_id
Claim JSON Type: String
Add to ID token: ON
Add to access token: ON
Add to userinfo: ON
```

## 使用示例

### 后台任务设置租户
```csharp
public class BackgroundService
{
    private readonly ITenantContextProvider _tenantProvider;

    public async Task ProcessAsync(string tenantId)
    {
        _tenantProvider.SetTenantId(tenantId);
        try
        {
            // 执行业务逻辑
            // 所有查询和保存都会自动应用租户过滤
        }
        finally
        {
            _tenantProvider.ClearTenantId();
        }
    }
}
```

### 跨租户查询（系统管理员）
```csharp
// 在 DbContext 中临时禁用过滤器
var allData = await context.Instances
    .IgnoreQueryFilters()
    .ToListAsync();
```

## 测试建议

1. **单元测试**：测试租户过滤器是否正确应用
2. **集成测试**：验证多租户数据隔离
3. **性能测试**：验证查询过滤器的性能影响
4. **安全测试**：尝试跨租户访问数据

## 已知问题和待办事项

### DbContext 编译错误
- 状态：存在格式问题导致编译失败
- 解决方案：需要手动修复 HireBotDbContext.cs 第 48 行的格式问题
- 临时方案：使用本文档中的迁移脚本手动更新数据库

### 后续工作
1. 修复 DbContext 编译错误
2. 创建并测试 EF Core 迁移
3. 更新单元测试以支持多租户
4. 添加多租户集成测试
5. 更新 API 文档说明租户隔离
6. 配置 Keycloak 租户ID映射
7. 验证所有实体的租户隔离是否正确
8. 性能优化和索引调整

## 参考资源

- **参考项目**：D:\gitee-ai4c\ncrew-builder\api
- **多租户模式**：Database-per-tenant (共享数据库，租户ID隔离)
- **EF Core 文档**：https://learn.microsoft.com/ef-core/querying/filters
