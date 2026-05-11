---
name: 私有分支功能开发上下文
description: 私有分支（Private Branch）功能实现的完整技术上下文，包括架构设计、文件清单、已知BUG和修复记录
type: reference
---

# 私有分支功能开发上下文

## 需求概述

需求文档：[02-0 雇佣端完整需求规格说明书-v2.md](f:\WorkSpace\ai4c\doc\hirebot-web\02-0 雇佣端完整需求规格说明书-v2.md) 第 6 节

**方案B**：MVP 严格走双阶段评估（AI 评估 + 用户自评），通过后才上岗并切换 IM 路由。

核心流程：
```
个人分身(live) → [创建私有分支] → hired → AI评估 → 用户自评 → live(IM路由切换)
                                                    ↓ 失败
                                              Review → 重试或废弃
```

## 涉及文件

### 新增文件
| 文件 | 说明 |
|------|------|
| `hirebot/back-end/src/HireBot.Abstraction/Models/EmployeeRuntime/CreatePrivateBranchRequestDto.cs` | 创建请求 (DisplayName, DisplayDescription, SelectedStations) |
| `hirebot/back-end/src/HireBot.Abstraction/Models/EmployeeRuntime/PrivateBranchResultDto.cs` | 创建结果 (BranchId, DisplayName, Status, FromInstanceId, ImRoutingSwitched) |
| `hirebot/back-end/src/HireBot.Repository/Migrations/20260510055253_AddActiveBranchId.cs` | DB迁移：Instances 表加 active_branch_id 列 |
| `hirebot/back-end/tests/HireBot.Core.Tests/EmployeeRuntimePrivateBranchTests.cs` | 10 个单元测试 |

### 修改文件（后端）
| 文件 | 修改内容 |
|------|----------|
| `HireBot.Abstraction/Services/EmployeeRuntime/IEmployeeRuntimeService.cs` | 新增 CreatePrivateBranchAsync、AbandonPrivateBranchAsync 接口 |
| `HireBot.Core/Services/EmployeeRuntime/EmployeeRuntimeService.cs` | 实现创建/废弃私有分支、lifecycle hook、IM路由切换 |
| `HireBot.Core/Services/EmployeeRuntime/InstanceArtifactCloneService.cs` | ResolveSourceRootAsync 支持 personal_clone 路径 |
| `HireBot.Repository/Entities/InstanceEntity.cs` | 新增 ActiveBranchId 字段 |
| `HireBot.Repository/HireBotDbContext.cs` | 注册 ActiveBranchId 列配置 |
| `HireBot.ApiService/Controllers/InstancesController.cs` | 新增 POST private-branch、POST abandon-branch |
| `tests/HireBot.Core.Tests/RuntimeApiControllerTests.cs` | FakeEmployeeRuntimeService 实现新接口 |

### 修改文件（前端）
| 文件 | 修改内容 |
|------|----------|
| `front-end/src/infra/api/modules/employeeRuntimeApi.ts` | 新增 CreatePrivateBranchRequest、PrivateBranchResult 类型和 createPrivateBranch、abandonPrivateBranch 函数 |
| `front-end/src/features/hiring/pages/PrivateBranchPage.tsx` | 调用 createPrivateBranch API，创建后跳转评估页 |
| `front-end/src/features/team/pages/InstanceDetailPage.tsx` | 新增"废弃私有分支"按钮（private_branch类型）+ abandonBranch 函数，canCreatePrivateBranch 条件已存在 |
| `front-end/src/features/hiring/pages/MyEmployeesPage.tsx` | 列表项增加"废弃"快捷操作 |

## API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/v1/instances/{instanceId}/private-branch` | 从个人分身创建私有分支（返回 status="hired"） |
| POST | `/api/v1/instances/{instanceId}/abandon-branch` | 废弃私有分支，恢复 IM 路由 |

## 核心设计决策

1. **ActiveBranchId 机制**：源分身的 InstanceEntity 上设 ActiveBranchId 指向活跃分支，用于站内对话路由。IM webhook 路由切换（KingCrab Gateway）只做了 best-effort。
2. **状态流转**：hired → interning_ai → interning_human → live（复用现有评估系统）
3. **不可二次分支**：CreatePrivateBranchAsync 校验 source.InstanceType ≠ "private_branch" + DB 检查无活跃分支
4. **IM 凭证复用**：私有分支不创建独立的 IM_CONFIG 记录，复用原分身凭证
5. **数据隔离**：private_branch 有独立 owner + conversation，通过 OwnerUserId 过滤

## 已知 BUG 及修复记录

### BUG #1: column i.active_branch_id does not exist (PostgreSQL)
- **日期**: 2026-05-10
- **现象**: 访问任何实例相关接口时报 500，PostgreSQL 报错 column 不存在
- **根因**: 手动创建的迁移文件缺少 .Designer.cs 且模型快照未更新，EF Core 未检测到待应用迁移
- **修复**: 删除手动迁移，执行 `dotnet ef migrations add AddActiveBranchId` 正确生成，模型快照自动更新
- **注意**: 需要重启应用以应用迁移（或手动执行 `dotnet ef database update`）。如果 Database:AutoMigrateOnStartup=false 则需手动执行 SQL：`ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS active_branch_id varchar(120);`

### BUG #2: 创建私有分支 409 Conflict
- **日期**: 2026-05-10
- **现象**: POST /api/v1/instances/pc_xxx/private-branch 返回 409
- **根因**: `InstanceArtifactCloneService.ResolveSourceRootAsync` 只在 `instances/department/{id}/` 路径查找产物。personal_clone 的产物在 `instances/personal_clone/{fromInstanceId}/{cloneId}/versions/{version}/`，导致找不到源文件抛出 InvalidOperationException
- **修复**: ResolveSourceRootAsync 新增 fallback — 当源有 FromInstanceId 时，额外查找 `instances/{personal_clone|private_branch}/{fromInstanceId}/{instanceId}/versions/{version}/`；新增 BuildCloneVersionRoot 辅助方法
- **注意**: 需要重启 HireBot.ApiService 以重新加载 DLL

## 待完成项

1. **KingCrab Gateway IM Webhook 路由切换**：当前只实现了站内对话路由（ActiveBranchId），IM 消息（飞书/钉钉/企微）的 Webhook 转发到分支沙箱需要额外调用 KingCrab Gateway API
2. **审计日志**：需求 14.3 要求私有分支创建/废弃操作记录审计日志
3. **IM_CONFIG suspended 状态**：需求附录 A.2 的 IM_CONFIG 状态机要求 active → suspended（分支占用）→ active（废弃恢复），当前未实现
4. **简化版 Coach 对话**：当前只收集 goal 文本 + 勾选工位，未运行 digital_employee_discovery coach 的工位深度追问
