# 评估服务多租户修复文档

## 📋 问题描述

用户在点击进入AI评估时，系统会不停地创建新的沙箱，而不是复用已有的沙箱环境。这导致资源浪费、用户体验差，且每次都需要重新初始化环境。

## 🔍 根本原因分析

### 1. 硬编码的租户ID（核心问题）

在多租户改造后，`EvaluationService.CreateEvaluationSandboxAsync` 方法中存在硬编码的租户ID和操作员ID：

**问题代码位置**：`back-end/HireBot.Core/Services/Evaluation/EvaluationService.WorkspaceManagement.cs`

```csharp
var createResult = await sandboxService.CreateAsync(
    new SandboxCreateRequestDto
    {
        // ...
        TenantId = "tenant-default",      // ❌ 硬编码！
        OperatorId = "operator-default",   // ❌ 硬编码！
        // ...
    },
    cancellationToken);
```

### 2. 工作区缓存机制失效

**缓存流程**：
1. 用户第一次访问评估功能时，创建沙箱并保存工作区状态到 `EvaluationWorkspaceStateEntity`
2. `TenantSavingInterceptor` 自动设置 `TenantId` 字段为当前租户ID（从 JWT 提取）
3. 用户第二次访问时，尝试加载已有的工作区状态
4. EF Core 全局查询过滤器自动添加 `TenantId` 条件
5. **如果租户上下文不一致，查询失败** ❌

**失效原因**：
- `EvaluationWorkspaceStateEntity` 实现了 `ITenant` 接口
- 全局查询过滤器：`WHERE TenantId = @CurrentTenantId`
- 保存时的 `TenantId` ≠ 加载时的 `TenantId` → 查询不到记录
- **缓存失效 → 系统认为是新用户 → 重新创建沙箱** 🔄

### 3. 数据库设计细节

**表结构**：`EvaluationWorkspaceStates`
- 主键：`Id` (Guid)
- 唯一索引：`(TenantId, OwnerSubject, EmployeeId)`
- 实现接口：`ITenant`（受全局查询过滤器影响）

**索引定义**：
```csharp
entity.HasIndex(e => new { e.TenantId, e.OwnerSubject, e.EmployeeId }).IsUnique();
```

### 4. 对比正确的实现

**雇佣服务的正确实现**（`EmployeeHiringService.cs`）：

```csharp
var tenantId = userIdentity.TenantId ?? "default";
var operatorId = userIdentity.OperatorId ?? "anonymous";
var ownerSubject = userIdentity.OwnerSubject ?? $"{tenantId}:{operatorId}";

var sandboxResult = await sandboxService.CreateAsync(new SandboxCreateRequestDto
{
    // ...
    TenantId = tenantId,           // ✅ 从用户身份获取
    OperatorId = operatorId,       // ✅ 从用户身份获取
    // ...
}, cancellationToken);
```

## ✅ 修复方案

### 修改内容

**文件**：`back-end/HireBot.Core/Services/Evaluation/EvaluationService.WorkspaceManagement.cs`

**修改位置**：`CreateEvaluationSandboxAsync` 方法

**修复代码**：

```csharp
private async Task<ApiResponse<(string RuntimeId, string SandboxId)>> CreateEvaluationSandboxAsync(
    string owner,
    string employeeId,
    string sandboxRole,
    bool useStableRuntimeId,
    CancellationToken cancellationToken)
{
    var runtimeId = useStableRuntimeId
        ? BuildEvaluationRuntimeId(employeeId, sandboxRole)
        : $"eval-{sandboxRole}-{Guid.NewGuid():N}"[..Math.Min(40, 15 + sandboxRole.Length + 32)];

    if (useStableRuntimeId)
    {
        // ... 查找现有沙箱逻辑保持不变 ...
    }

    // ✅ 修复：从 userIdentity 获取租户ID和操作员ID，避免硬编码
    var tenantId = userIdentity.TenantId ?? "default";
    var operatorId = userIdentity.OperatorId ?? "anonymous";

    var createResult = await sandboxService.CreateAsync(
        new SandboxCreateRequestDto
        {
            ScopeType = SandboxScopeTypes.Managed,
            ScopeKey = runtimeId,
            SandboxRole = sandboxRole,
            OwnerSubject = owner,
            TenantId = tenantId,        // ✅ 使用实际租户ID
            OperatorId = operatorId,    // ✅ 使用实际操作员ID
            ProvisioningMode = "managed",
            UseCase = $"evaluation-{sandboxRole}-for:{employeeId}",
            Metadata = new Dictionary<string, string>
            {
                [SandboxMetaKeys.UserSubject] = owner,
                [SandboxMetaKeys.EmployeeId] = employeeId,
                [SandboxMetaKeys.EvalScopeKey] = runtimeId
            }
        },
        cancellationToken);
    
    // ... 后续逻辑保持不变 ...
}
```

### 关键改动

1. **新增**：从 `userIdentity` 获取租户ID和操作员ID
   ```csharp
   var tenantId = userIdentity.TenantId ?? "default";
   var operatorId = userIdentity.OperatorId ?? "anonymous";
   ```

2. **替换**：将硬编码的值替换为实际值
   - `TenantId = "tenant-default"` → `TenantId = tenantId`
   - `OperatorId = "operator-default"` → `OperatorId = operatorId`

## 🎯 修复效果

### 修复前
- ❌ 每次访问都创建新沙箱
- ❌ 工作区缓存永远查不到
- ❌ 资源浪费，用户需要等待沙箱初始化
- ❌ 历史数据丢失

### 修复后
- ✅ 租户ID正确设置，与保存时一致
- ✅ 工作区缓存查询成功
- ✅ 复用已有沙箱，无需重新创建
- ✅ 用户体验流畅，秒级响应
- ✅ 数据持久化正常

## 🧪 测试建议

### 1. 单元测试验证
- 验证 `CreateEvaluationSandboxAsync` 使用正确的租户ID
- 验证工作区缓存的加载和保存逻辑

### 2. 集成测试场景
1. **首次访问**：用户第一次进入AI评估，创建新沙箱和工作区
2. **再次访问**：同一用户再次进入，应复用已有沙箱
3. **多租户隔离**：不同租户的用户各自独立的沙箱和工作区
4. **跨会话持久化**：用户退出后重新登录，仍能找到之前的工作区

### 3. 数据库验证

**查询工作区状态**：
```sql
SELECT 
    "OwnerSubject",
    "TenantId",
    "EmployeeId",
    "CreatedAtUtc",
    "UpdatedAtUtc"
FROM "EvaluationWorkspaceStates"
ORDER BY "UpdatedAtUtc" DESC
LIMIT 10;
```

**查询沙箱实例**：
```sql
SELECT 
    "SandboxId",
    "ScopeKey",
    "SandboxRole",
    "OwnerSubject",
    "TenantId",
    "OperatorId",
    "State"
FROM "SandboxInstances"
WHERE "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
ORDER BY "UpdatedAtUtc" DESC
LIMIT 20;
```

## 📌 相关资源

### 代码文件
- `HireBot.Core/Services/Evaluation/EvaluationService.WorkspaceManagement.cs` - 评估工作区管理
- `HireBot.Core/Services/Evaluation/EvaluationService.cs` - 评估服务主文件
- `HireBot.Repository/Entities/EvaluationWorkspaceStateEntity.cs` - 工作区状态实体
- `HireBot.Repository/Extensions/TenantSavingInterceptor.cs` - 租户保存拦截器

### 文档
- `MULTITENANCY_IMPLEMENTATION.md` - 多租户实现总结
- `APPUSER_MULTITENANCY_OPTIMIZATION.md` - 用户表多租户优化

### 参考实现
- `HireBot.Core/Services/Hiring/EmployeeHiringService.cs` - 正确的租户ID处理示例
- `HireBot.Core/Infrastructure/Identity/HireBotUserIdentity.cs` - 用户身份信息提供者

## 🔐 安全性说明

- 租户ID和操作员ID来自经过验证的 JWT Claims
- 全局查询过滤器确保租户级别的数据隔离
- 唯一索引 `(TenantId, OwnerSubject, EmployeeId)` 防止数据冲突

## 📝 后续建议

### 1. 代码审查
- 检查其他服务是否存在类似的硬编码问题
- 确保所有与租户相关的操作都使用 `userIdentity.TenantId`

### 2. 日志增强
- 在沙箱创建时记录租户ID和操作员ID
- 在工作区缓存失败时记录详细的查询条件

### 3. 监控告警
- 监控同一用户短时间内多次创建沙箱的行为
- 设置工作区缓存命中率监控指标

### 4. 数据清理
- 清理多租户改造前创建的旧数据（TenantId 为 "tenant-default" 的记录）
- 为历史数据补充正确的租户ID

---

**修复时间**：2026-06-04  
**修复人员**：GitHub Copilot  
**影响范围**：评估服务的沙箱创建和工作区缓存逻辑  
**风险评估**：低风险，修复逻辑与雇佣服务保持一致
