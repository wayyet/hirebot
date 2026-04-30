# Collaboration Boundary

本文件固定 `diagnosis` 与 `employment-coach-conversation`、主 skill、下游生成类 skill 的协作边界。

## 角色分工

| 角色 | 回答的问题 | 是否写入状态 | 是否调下游 |
|---|---|---|---|
| `diagnosis` | 还差什么 | 否 | 否 |
| `employment-coach-conversation` | 差的部分要交给谁、要带什么去 | 是，维护 handoff todo 和配置治理 | 是，通过 `<dispatch>` 信号 |
| `ontology_extraction` | 如何把资料写成 ontology / slice | 是，写 `ontology/` | 否 |
| `skill_generation` | 如何生成业务 skill 包 | 是，写 `skills/` | 否 |
| `external_config` | 如何生成外部系统配置 | 是，写 `external/` | 否 |
| 主 skill / 系统层 | 何时调谁、如何合并 UI | 是，维护流程状态 | 是 |

## 诊断 todo 与 handoff todo

诊断 todo：

- 回答“还差什么”
- 有 `level: 必需 | 推荐 | 可选`
- 可以跨阶段
- 可以关联多个 handoff todo
- 只读，不直接触发下游

handoff todo：

- 回答“交给谁、带什么去”
- 没有 `level`
- 每条只属于一个阶段
- 每条有一个明确 `target_skill`
- 由 `employment-coach-conversation` 维护状态并 dispatch

## 推荐协作链路

1. 主 skill 初始化沙箱并加载模板完备性清单。
2. `employment-coach-conversation` 引导用户并维护 handoff todo。
3. 状态变化后，主 skill 调 `diagnosis` 重跑。
4. `diagnosis` 输出 `diagnostic_report` 和诊断 todo。
5. UI 合并展示 handoff todo 与诊断 todo。
6. 用户选择补齐缺口时，主 skill 通知 `employment-coach-conversation` 回到对应阶段继续引导。
7. `employment-coach-conversation` 把用户补齐的内容沉淀成 handoff todo 并 dispatch。

## UI 合并展示建议

- 同一阶段 + 同一意图的诊断 todo 与 handoff todo 可以合并成一行显示。
- handoff todo 强调“正在做什么 / 做到哪一步”。
- 诊断 todo 强调“还差什么 / 是否必需”。
- 当诊断 todo 关联多个 handoff todo 时，UI 应展示为一个缺口组，而不是复制多条重复缺口。
- `level: 必需` 的诊断 todo 应比推荐 / 可选更突出，但不要让业务用户看到内部字段名。

## 诊断报告给用户的口径

`diagnosis` 的 `user_summary` 要短，交给上层流程转述：

- 好：`资料已经够用，技能还差一条主线能力；补齐后就能继续配置外部系统。`
- 不好：`d_skill_main_required_001 未满足，stage_readiness.skill=partial。`

诊断内部结构给系统层和 UI 使用，不直接暴露给业务用户。

## 禁止行为

- 不要输出 `<dispatch>`
- 不要把诊断 todo 写入 handoff todo 索引
- 不要把 handoff todo 的状态改成 `needs_review`、`confirmed` 或任何其他状态
- 不要替 `employment-coach-conversation` 追问用户
- 不要替 `external_config` 接收或校验凭据
- 不要把沙箱绝对路径、hook、orchestrator 等内部概念写入 `user_summary`

## Callback 后重跑口径

每次 `employment-coach-conversation` 收到 `dispatch_callback` 后，主 skill 应重跑诊断。诊断时按 callback 状态处理：

- `success`: 如果相关 todo 已由雇佣教练确认，可满足对应项
- `partial`: 生成 warning 或 blocked，取决于缺失项是否 required
- `failed`: 生成 blocked 诊断 todo，建议重新引导或重发

注意：callback 成功不等于 handoff todo confirmed。只有用户确认后，由雇佣教练把 handoff todo 切到 `confirmed`，诊断才把它视为完成。
