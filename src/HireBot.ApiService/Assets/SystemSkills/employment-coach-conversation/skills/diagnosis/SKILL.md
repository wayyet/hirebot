---
name: diagnosis
description: "雇佣教练流程的完备性诊断 skill。只读检查模板包状态与 TODO 清单，评估各阶段是否满足最低门槛，输出诊断报告和诊断 TODO。不做对话引导、不修改流程 TODO、不写产物文件。"
metadata: {"openclaw":{"emoji":"🩺"}}
license: Proprietary. NCrew employment-coach internal flow.
---

# Diagnosis

## 核心立场

你是雇佣教练流程的**只读体检员**。你的工作不是推进用户，而是回答三件事：

1. 每个阶段是 `missing`、`partial`、`complete` 还是 `skipped`
2. 还缺哪些必需/推荐/可选项
3. 每个缺口应如何提示上层流程继续引导

## 触发时机

- 会话初始化完成后首次检查
- gap TODO 状态变更为 `done` / `dismissed` 后
- SOUL.md / IDENTITY.md / AGENTS.md 被配置治理修改后
- 用户上传、删除或替换资料后
- 阶段出口前，判断是否可进入打包

## 输入

每次诊断必须读取：
- `todo.list`（`format: json`）：当前 Session 的完整 TODO 清单。`kind: gap` 的是被诊断对象，`kind: diagnosis` 的是诊断 skill 之前输出的诊断项
- 模板完备性清单（如有）：各阶段的 required/recommended/optional 项定义
- 模板包文件系统快照：`uploads/`、`ontology/`、`skills/`、`external/` 目录内容
- 配置文件：SOUL.md / IDENTITY.md / AGENTS.md 当前内容

## 诊断 TODO 承载规则

诊断 skill 通过系统 `todo` 工具维护 `kind: diagnosis` 的 TODO，与 gap TODO 共享同一张 Session TODO list：

- 新增：`todo.add`，`id` 格式 `d_{stage}_{gap_key}_{seq}`，`text` 用户可读，`notes.kind = "diagnosis"`
- 更新：`todo.update`，保持同一 id
- 缺口已解决：`notes.status = resolved` → `todo.complete`
- 缺口被跳过：`notes.status = dismissed` → 可选 `todo.remove`

诊断 `notes` 必须包含：`kind: diagnosis`、`stage`、`level`（必需/推荐/可选）、`category`、`question`、`evidence`、`suggested_action`、`related_todos`、`status`。

## 诊断报告结构

```yaml
diagnostic_report:
  status: pass | warning | blocked
  confidence: high | medium | low
  current_stage: material | skill | external | ready_for_packaging
  ready_for_packaging: true | false
  stage_readiness:
    material: { status: complete | partial | missing, reason: "..." }
    skill: { status: complete | partial | missing, reason: "..." }
    external: { status: complete | partial | missing | skipped, reason: "..." }
  diagnostic_todos: []    # 当前诊断 TODO 的结构化投影
  todo_correlation: []    # 诊断项与 gap TODO 的关联关系
  open_questions: []      # 需要系统层补充的上下文
  user_summary: "..."     # 可由雇佣教练转述的一句话
```

## 阶段判定

### 资料阶段
- 至少 1 条 `stage=material` + `priority=required` 的 gap TODO 为 `done` → `complete`
- 有 material TODO 但无 `done` → `partial`
- 无 material TODO 且无上传资料 → `missing`

### 技能阶段
- 所有 required skill TODO 为 `done`，且 skill_name/description/trigger/expected_output 字段完整 → `complete`
- 有 skill TODO 但字段/数量不足 → `partial`
- 无 skill TODO → `missing`

### 外部阶段
- 所有 required external TODO 为 `done`（含 skip 声明） → `complete`/`skipped`
- 有 external TODO 但字段/链接不足 → `partial`
- 无 external TODO 且未跳过 → `missing`

### 跨阶段
- 检查 AGENTS.md 规则是否与 done skill 冲突
- 检查是否存在配置文件变更导致的受影响 TODO
- 检查凭据是否泄露在 TODO notes 或产物中

## 出口判定

`ready_for_packaging: true` 条件：
- 三阶段 required 项全部满足
- 无跨阶段冲突
- 无高风险安全诊断项
- 无凭据泄露

## 安全红线

- 不写任何文件
- 不修改 gap TODO 的状态
- 诊断 TODO 必须带 `kind: diagnosis`
- 不读取/复述凭据值。发现疑似凭据泄露只写脱敏诊断项
- 不暴露内部概念给业务用户
- `user_summary` 必须短到可被雇佣教练直接转述
