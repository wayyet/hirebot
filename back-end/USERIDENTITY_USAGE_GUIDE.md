# IUserIdentity 使用指南

## 概述

`IUserIdentity` 提供了统一的接口来访问当前用户的身份信息和租户信息，替代了之前分散在各处的 `OwnerSubject`、`TenantId`、`OperatorId` 等字段的读取逻辑。

## 核心特性

- ✅ 统一的用户信息访问接口
- ✅ 自动从 JWT Claims 提取信息
- ✅ 多租户支持（TenantId, TenantName）
- ✅ 支持多种 JWT Claims 格式（Keycloak, Azure AD, 自定义）
- ✅ Fallback 机制（开发模式支持 X-HireBot-Owner header）
- ✅ JSON 序列化支持

## 接口定义

```csharp
public interface IUserIdentity
{
    string Id { get; }                    // JWT sub (外部用户 ID)
    string Email { get; }                 // 用户邮箱
    string UserName { get; }              // 用户名
    string FirstName { get; }             // 名
    string LastName { get; }              // 姓
    string FullName { get; }              // 全名
    string DisplayName { get; }           // 显示名称
    string? TenantId { get; }             // 租户 ID
    string? TenantName { get; }           // 租户名称
    string OperatorId { get; }            // 操作员 ID
    string OwnerSubject { get; }          // 所有者主体标识
    string? Role { get; }                 // 用户角色
    bool IsAuthenticated { get; }         // 是否已认证
    string? DepartmentId { get; }         // 部门 ID
}
```

## 在控制器中使用

### 示例 1：通过构造函数注入

```csharp
using HireBot.Abstraction.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class MyController(IUserIdentity userIdentity) : ControllerBase
{
    [HttpGet("profile")]
    public IActionResult GetUserProfile()
    {
        // 直接使用 userIdentity 获取用户信息
        return Ok(new
        {
            userId = userIdentity.Id,
            userName = userIdentity.UserName,
            email = userIdentity.Email,
            tenantId = userIdentity.TenantId,
            isAuthenticated = userIdentity.IsAuthenticated
        });
    }

    [HttpPost("create-resource")]
    public async Task<IActionResult> CreateResource([FromBody] CreateResourceDto dto)
    {
        // 使用 OwnerSubject 标识资源所有者
        var resource = new Resource
        {
            OwnerSubject = userIdentity.OwnerSubject,
            TenantId = userIdentity.TenantId ?? "default",
            CreatedBy = userIdentity.UserName,
            // ...
        };

        await SaveResourceAsync(resource);
        return Ok(resource);
    }
}
```

### 示例 2：替代 SandboxRequestModels 中的字段

**之前的写法**：
```csharp
var request = new SandboxCreateRequestDto
{
    ScopeType = "hire",
    ScopeKey = hireId,
    SandboxRole = "candidate",
    OwnerSubject = ResolveOwnerSubject(),  // ❌ 需要手动调用方法
    TenantId = ResolveTenantId(),          // ❌ 需要手动调用方法
    OperatorId = ResolveOperatorId(),      // ❌ 需要手动调用方法
    // ...
};
```

**现在的写法**：
```csharp
var request = new SandboxCreateRequestDto
{
    ScopeType = "hire",
    ScopeKey = hireId,
    SandboxRole = "candidate",
    OwnerSubject = userIdentity.OwnerSubject,  // ✅ 直接从 userIdentity 获取
    TenantId = userIdentity.TenantId ?? "default",
    OperatorId = userIdentity.OperatorId,
    // ...
};
```

## 在服务中使用

### 示例 1：在主构造函数中注入

```csharp
using HireBot.Abstraction.Infrastructure.Identity;

public class MyService(
    HireBotDbContext dbContext,
    IUserIdentity userIdentity,  // ✅ 注入 IUserIdentity
    ILogger<MyService> logger)
{
    public async Task<ApiResponse<MyDto>> CreateAsync(CreateRequestDto request)
    {
        // 直接使用 userIdentity
        var entity = new MyEntity
        {
            OwnerSubject = userIdentity.OwnerSubject,
            TenantId = userIdentity.TenantId ?? "default",
            OperatorId = userIdentity.OperatorId,
            CreatedBy = userIdentity.UserName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.MyEntities.Add(entity);
        await dbContext.SaveChangesAsync();

        return ApiResponse<MyDto>.SuccessResponse(ToDto(entity));
    }

    public async Task<IEnumerable<MyEntity>> GetMyResourcesAsync()
    {
        // 使用 OwnerSubject 查询当前用户的资源
        return await dbContext.MyEntities
            .Where(e => e.OwnerSubject == userIdentity.OwnerSubject)
            .Where(e => e.TenantId == userIdentity.TenantId)
            .ToListAsync();
    }
}
```

### 示例 2：替代 RequestContextService

**之前的写法**（仍然可用）：
```csharp
public class OldService(
    IRequestContextService requestContextService)
{
    public void DoSomething()
    {
        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(null, null);
        // ...
    }
}
```

**推荐的新写法**：
```csharp
public class NewService(
    IUserIdentity userIdentity)  // ✅ 更简洁、语义更清晰
{
    public void DoSomething()
    {
        var ownerSubject = userIdentity.OwnerSubject;
        var tenantId = userIdentity.TenantId;
        var operatorId = userIdentity.OperatorId;
        // ...
    }
}
```

## 在 Minimal API 中使用

```csharp
app.MapGet("/api/current-user", (IUserIdentity userIdentity) =>
{
    if (!userIdentity.IsAuthenticated)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        id = userIdentity.Id,
        userName = userIdentity.UserName,
        email = userIdentity.Email,
        fullName = userIdentity.FullName,
        tenantId = userIdentity.TenantId,
        tenantName = userIdentity.TenantName,
        role = userIdentity.Role,
        departmentId = userIdentity.DepartmentId
    });
});
```

## JWT Claims 映射

| IUserIdentity 属性 | JWT Claims（优先级从高到低） | 说明 |
|-------------------|---------------------------|------|
| `Id` | `sub`, `ClaimTypes.NameIdentifier` | 用户唯一标识（Keycloak user ID） |
| `Email` | `email`, `ClaimTypes.Email` | 用户邮箱 |
| `UserName` | `preferred_username`, `ClaimTypes.Name`, `name` | 用户名 |
| `FirstName` | `given_name`, `ClaimTypes.GivenName` | 名 |
| `LastName` | `family_name`, `ClaimTypes.Surname` | 姓 |
| `TenantId` | `tenant_id`, `tid`, `tenant`, `organization`, `ClaimTypes.GroupSid` | 租户 ID |
| `OperatorId` | `operator_id`, `preferred_username` | 操作员 ID |
| `Role` | `ClaimTypes.Role`, `role` | 用户角色 |
| `DepartmentId` | `department_id`, `dept_id` | 部门 ID |

## OwnerSubject 的计算逻辑

```csharp
OwnerSubject = 
    JWT sub claim                          // 优先：JWT 标准 subject
    ?? X-HireBot-Owner header             // 次选：测试模式 header
    ?? "{TenantId}:{OperatorId}"         // 最终：组合标识
```

## 多租户支持

### 从 organization claim 解析租户信息

如果 JWT 包含 `organization` claim（JSON 格式）：

```json
{
  "organization": {
    "my-company": {
      "id": "tenant-123",
      "name": "My Company Inc"
    }
  }
}
```

则：
- `TenantId` = `"tenant-123"`
- `TenantName` = `"my-company"`

### 在查询中使用租户过滤

```csharp
public async Task<List<MyEntity>> GetTenantResourcesAsync()
{
    // TenantId 会自动通过 TenantSavingInterceptor 和 Query Filters 处理
    // 但你也可以显式查询
    return await dbContext.MyEntities
        .Where(e => e.TenantId == userIdentity.TenantId)
        .ToListAsync();
}
```

## 向后兼容性

现有的 `IRequestContextService` 和相关方法仍然可用，但推荐逐步迁移到 `IUserIdentity`：

| 旧方法 | 新属性 |
|--------|--------|
| `requestContextService.ResolveOwnerSubject()` | `userIdentity.OwnerSubject` |
| `requestContextService.ResolveTenantAndOperator()` | `(userIdentity.TenantId, userIdentity.OperatorId)` |

## 开发和测试

### 在单元测试中模拟

```csharp
using Moq;

var mockUserIdentity = new Mock<IUserIdentity>();
mockUserIdentity.Setup(x => x.Id).Returns("test-user-123");
mockUserIdentity.Setup(x => x.UserName).Returns("testuser");
mockUserIdentity.Setup(x => x.TenantId).Returns("test-tenant");
mockUserIdentity.Setup(x => x.OwnerSubject).Returns("test-user-123");
mockUserIdentity.Setup(x => x.IsAuthenticated).Returns(true);

var service = new MyService(dbContext, mockUserIdentity.Object, logger);
```

### 使用 X-HireBot-Owner Header（开发模式）

```bash
curl -X POST https://localhost:5001/api/resources \
  -H "X-HireBot-Owner: dev-user-123" \
  -H "Content-Type: application/json" \
  -d '{"name": "Test Resource"}'
```

## 迁移建议

### 阶段 1：新代码使用 IUserIdentity

所有新的控制器和服务都应该使用 `IUserIdentity` 而不是 `IRequestContextService`。

### 阶段 2：逐步重构现有代码

识别使用 `ResolveOwnerSubject`、`ResolveTenantAndOperator` 的地方，逐步替换为 `IUserIdentity`。

### 阶段 3：移除冗余代码

当所有代码都迁移后，可以考虑废弃 `IRequestContextService`（或简化其实现）。

## 常见问题

### Q: IUserIdentity 和 IRequestContextService 有什么区别？

**A**: 
- `IUserIdentity` 提供更完整的用户信息（如 Email、FullName、Role 等）
- `IUserIdentity` 是属性访问，更直观
- `IRequestContextService` 只提供基础的身份解析方法
- 推荐新代码使用 `IUserIdentity`

### Q: 如果用户未认证，IUserIdentity 返回什么？

**A**: 
- `IsAuthenticated` 返回 `false`
- 字符串属性返回空字符串或 fallback 值（如 "tenant-default"）
- 可空属性返回 `null`

### Q: OwnerSubject 什么时候使用 fallback？

**A**: 
当 JWT 中没有 `sub` claim 且没有 `X-HireBot-Owner` header 时，会使用 `{TenantId}:{OperatorId}` 作为 fallback。这主要用于开发和测试环境。

## 相关文件

- **接口定义**: `HireBot.Abstraction/Infrastructure/Identity/IUserIdentity.cs`
- **实现类**: `HireBot.Core/Infrastructure/Identity/HireBotUserIdentity.cs`
- **服务注册**: `HireBot.Core/Extensions/ServiceExtensions.cs`
- **用户同步**: `HireBot.ApiService/Authentication/UserSyncMiddleware.cs`
