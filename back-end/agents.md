# HireBot 项目规则

本文件为 HireBot 项目的开发指令文件，适用于本目录及其子目录。

## 通用规则

### 基础交互
- 所有回答均使用中文
- 提供代码时，为关键逻辑和潜在理解难点添加简明中文注释
- 避免不必要的对象复制、深拷贝和临时集合集创建

### 代码设计原则
- **避免多层嵌套**：优先使用提前返回（guard clauses）
- **单一职责**：函数只做一件事，单个函数聚焦一个核心意图
- **代码组织**：相关代码应放在一起，保持清晰的抽象层次
- **命名规范**：命名必须语义化，遵循 C#/.NET 命名规范，避免单字母变量和难懂缩写
- **并发安全**：需要并发时，使用合适的并发控制机制，并明确线程安全边界

### 重构与提交流程
- 小步重构，每次只做一个小改动，然后测试
- 每次改动后都要执行测试，确保行为不变
- 频繁提交，保证代码始终处于可工作状态
- 若重构前测试覆盖不足，先补关键路径测试再重构

## 技术栈架构

### 项目架构
- **架构形态**：模块化单体（Modular Monolith）
- **API 形态**：ASP.NET Core Controller + REST
- **前后端通信**：RESTful API

### 核心技术组件
- **运行时**：.NET 10 / C# 14，启用可空引用类型
- **弹性策略**：用 Microsoft.Extensions.Resilience 构建弹性 ASP.NET Core API，Microsoft.Extensions.Http.Resilience：用于 HTTP 请求的弹性封装，Microsoft.Extensions.Resilience：用于任意异步操作的通用弹性；
- **认证授权**：Keycloak 26.5（OIDC/OAuth2 标准接入）
- **API 文档**：Swashbuckle.AspNetCore + Microsoft.AspNetCore.OpenApi
- **数据库**：PostgreSQL
- **ORM**：EF Core 10（主）
- **缓存**：Microsoft.Extensions.Caching.Hybrid + Redis + MemoryCache
- **消息队列**：Dapr PubSub
- **分布式任务**：TickerQ
- **分布式锁**：Dapr.DistributedLock
- **AI 能力**：Microsoft Agents/Microsoft.Agents.AI + Azure OpenAI
- **测试框架**：xUnit v3

## 编码规范

### C#14 / .NET10 / ASP.NET Core 10 规范
- **主构造函数依赖注入**：优先使用主构造函数（Primary Constructor）做服务依赖注入，简化构造函数注入模式
- **异步编程**：新代码默认使用 async/await，I/O 操作不得阻塞线程
- **参数校验**：公共 API 参数必须进行校验，使用 FluentValidation.AspNetCore
- **日志记录**：必须使用结构化日志，禁止字符串拼接日志正文
- **异常处理**：统一异常处理（中间件优先，可补充 AOP），异常返回 ProblemDetails
- **依赖注入**：生命周期要准确，`Singleton` 不依赖 `Scoped`
- **内存优化**：高频路径优先选择低分配写法，使用 `Span<T>`、`Memory<T>`、`ArrayPool<T>`
- **JSON 序列化**：优先使用 .NET 10 内置 `System.Text.Json`

### API 响应格式规范
- **成功响应**：
  - `code`: `200` (int)
  - `success`: `true` (bool)
  - `message`: 描述信息 (string)
  - `data`: 返回的实际数据 (object)
- **失败响应**：
  - `code`: 依据具体场景的错误编码 (int)
  - `success`: `false` (bool)
  - `message`: 错误描述信息 (string)
  - `data`: 异常栈信息或其他错误详情 (object)
使用统一对象 recoed class ApiResponse<T>;

### API 文档规范（Swashbuckle.AspNetCore + Microsoft.AspNetCore.OpenApi）
- **版本管理**：API 必须使用版本控制，推荐使用 URL 路径或头部信息版本控制
- **文档生成**：使用 Swagger UI 和 OpenAPI 规范自动生成 API 文档
- **注释文档**：Controller 和 Action 方法必须包含 XML 注释，用于生成 API 文档
- **参数验证**：API 参数应使用 Data Annotations 或 FluentValidation 进行验证，并在文档中体现
- **响应格式**：API 响应模型必须定义清晰的 DTO 类，并在文档中展示响应结构
- **认证说明**：在 API 文档中明确说明认证方式和所需权限
- **示例数据**：为复杂请求和响应提供示例数据，便于前端开发者理解

### 认证与安全
- **统一认证**：使用 Keycloak 26.5 作为身份提供方
- **API 鉴权**：统一使用 Bearer Token 校验，不在业务层手写重复鉴权逻辑
- **配置管理**：所有外部回调地址、密钥和客户端配置必须走配置项与密钥管理

### 数据访问规范
- **ORM 包版本**：固定为 `10.0.7`，包括：
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Relational`
  - `Microsoft.EntityFrameworkCore.Design`
  - `Microsoft.EntityFrameworkCore.Tools`
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
- **DbContext 管理**：按模块拆分，禁止单一巨型 DbContext
- **查询优化**：默认使用只读优化（如 `AsNoTracking`），禁止 N+1 查询
- **事务管理**：事务边界在应用服务层统一管理

### 缓存规范
- **统一入口**：使用 Microsoft.Extensions.Caching.Hybrid
- **缓存层级**：本地缓存（MemoryCache）用于热点数据，Redis 用于共享缓存
- **Key 命名**：`{模块}:{资源}:{标识}:{版本}`
- **过期策略**：必须设置过期策略，禁止无过期时间缓存
- **读取策略**：先缓存后回源，高并发场景结合并发控制

### 消息与异步处理
- **事件传递**：领域事件与集成事件通过 Dapr PubSub 传递
- **幂等性**：消费者逻辑必须幂等，重复消息不应造成业务副作用
- **任务处理**：TickerQ 用于定时任务，任务处理器必须支持取消与超时

### 前后端约定
- **API 设计**：遵循 RESTful 风格，URL 使用资源导向命名
- **响应格式**：`{ code, success, message, data }`，字段使用蛇形命名法（Snake Case）

### 命名规范
- **数据库**：PostgreSQL 表和字段使用蛇形命名法（Snake Case），也称为下划线命名法（Underscore Case）
- **实体映射**：C# Entity 字段使用大驼峰命名，配合 JsonPropertyName 特性

## 质量保障

### 测试规范
- **自动化测试**：统一使用 xUnit v3
- **覆盖率**：关键业务逻辑必须有自动化测试
- **回归测试**：修复缺陷时必须补回归测试
- **测试命名**：应清晰表达场景、输入和预期结果

### 性能优化
- **内存优化**：减少临时对象，及时释放资源，避免事件订阅和闭包泄漏
- **计算优化**：避免重复计算，选择合适的数据结构与算法
- **并行优化**：识别可并行任务，控制并发度

### 文档与注释
- **注释原则**：解释"为什么"，而不是"做什么"
- **API 文档**：公共 API 必须有清晰文档
- **同步更新**：代码变更时同步更新注释与文档

## 执行优先级

1. **正确性**
2. **可读性**
3. **可维护性**
4. **性能**（以数据和指标驱动）
5. **开发效率**
