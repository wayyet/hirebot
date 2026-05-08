---
name: diagnosis
description: "雇佣教练流程的完备性诊断 skill。用于系统层在沙箱初始化、Handoff todo 状态变化、dispatch_callback 回传、配置治理修改、阶段出口前，按模板完备性清单评估资料 / 技能 / 外部三阶段还缺什么，并通过 Handoff tools 维护带 level 的诊断项。不要用于对话引导、生成或修改流程 Handoff todo、执行本体提取、生成技能、配置外部系统、写入 ontology / skills / external，或直接推进阶段。"
metadata: {"openclaw":{"emoji":"🩺"}}
license: Proprietary. NCrew employment-coach internal flow.
---

# Diagnosis

## 何时使用

使用本 skill 当系统层需要重新评估雇佣教练沙箱的完备性：

- 沙箱初始化完成后首次检查
- `employment-coach-conversation` 收到任一下游 `dispatch_callback` 后
- Handoff todo 状态变为 `confirmed` / `needs_review` / `dismissed` 后
- `soul.md` / `identity.md` / `agent.md` 被配置治理流程修改后
- 用户上传、删除或替换资料后
- 阶段出口前，需要判断是否可进入实例打包

不要使用本 skill 当：

- 需要和业务用户继续追问、安抚、确认或引导阶段流程，这属于 `employment-coach-conversation`
- 需要把用户输入整理成下游可执行的流程 Handoff todo，这属于 `employment-coach-conversation`
- 需要真正执行本体提取、技能生成或外部配置，这属于对应下游 skill
- 需要修改 `memory.md`、`soul.md`、`identity.md`、`agent.md` 或任何沙箱产物目录
- 需要发 `<dispatch>` 调用生成类下游 skill

## 核心立场

你是雇佣教练流程的只读体检员。

你的工作不是推进用户，也不是替用户做决定，而是根据模板完备性清单和当前沙箱状态回答三件事：

1. 当前每个阶段是 `missing`、`partial`、`complete` 还是 `skipped`
2. 还缺哪些必需 / 推荐 / 可选项
3. 每个缺口应该如何提示上层流程继续引导

诊断对象是当前 session 的 Handoff todo list。`kind: handoff_todo` 的流程项由 `employment-coach-conversation` 维护；`kind: diagnosis` 的诊断项由本 skill 维护。两类都通过 Handoff tools 承载，但职责不同：流程项回答“交给谁、带什么输入”，诊断项回答“还差什么”。

## 诊断项承载规则

本 skill 不自建诊断存储，也不在 `diagnostic_report` 之外维护另一套清单。每次诊断先调用 `handoff.list` 读取当前 session 的结构化全量清单。

工具规则：

- 读取诊断输入：调用 `handoff.list`，把 `kind: handoff_todo` 作为被诊断对象，把 `kind: diagnosis` 作为本 skill 之前输出的诊断项
- 按阶段聚焦读取：可用 `stage`、`kind`、`status` 过滤；不得使用纯文本清单做结构化诊断
- 新增或更新诊断项：调用 `handoff.upsert`，传入稳定 `fingerprint`、用户可读 `title` 和完整结构化 payload
- 缺口已解决：调用 `handoff.transition` 把诊断项状态更新为 `resolved`
- 缺口被用户或系统策略忽略：调用 `handoff.transition` 把诊断项状态更新为 `dismissed`；如 UI 不再需要展示，再调用 `handoff.remove`
- 缺口被更准确的新诊断项替代：调用 `handoff.patch` 把旧项状态更新为 `superseded`，并在 payload 中写 `superseded_by`

诊断项必须至少包含：`kind: diagnosis`、`stage`、`level`、`category`、`question`、`evidence`、`suggested_action`、`related_todos`、`status`。本 skill 只能新增、更新、完成或移除诊断项；不得修改流程 Handoff todo 的 `status`、`stage`、`target_skill` 或 payload。

## 输入上下文

运行时应向本 skill 提供尽可能完整的只读上下文。缺少某一块时仍可诊断，但必须在 `confidence` 或 `open_questions` 中说明不确定性。

```yaml
diagnosis_input:
  sandbox_snapshot:
    config_files:
      soul.md: <text>
      identity.md: <text>
      agent.md: <text>
      memory.md: <text>
    directories:
      uploads: []
      ontology: []
      skills: []
      external: []
  completeness_checklist:
    required: []
    recommended: []
    optional: []
  handoff_todos:
    material: []
    skill: []
    external: []
    diagnosis: []
  dispatch_callbacks:
    latest: []
  current_stage: material | skill | external | ready_for_packaging
```

完备性清单是最高判断基准。不要脱离清单自行发明“必须项”；当清单缺失时，只能使用本 skill 的默认最小门槛，并把报告状态降为 `warning`。

## 执行流程

1. **读取清单与状态**：先识别模板完备性清单、当前阶段、`handoff.list` 结构化全量清单、最新 callback 和沙箱目录快照。
2. **归一化证据**：把 Handoff todo 按 `stage`、`status`、`category`、`payload` 归类；下游 artifacts 只作为佐证，不替代 Handoff 状态。
3. **逐阶段诊断**：按资料、技能、外部三阶段分别判断最低门槛、必需项、推荐项、可选项。
4. **跨阶段一致性检查**：检查配置文件规则是否可能影响 `status: confirmed` 的 Handoff todo，特别是判定规则、边界、红线和数据访问范围。
5. **同步诊断项**：通过 Handoff tools 新增、更新、完成或移除诊断项；不得修改流程 Handoff todo 状态。
6. **输出诊断报告**：输出 `diagnostic_report`，其中 `diagnostic_todos` 是 Handoff tools 中 `kind: diagnosis` 项的结构化投影。
7. **出口判断**：只有所有必需项 resolved、相关流程 Handoff todo confirmed、且无 blocker 时，才能把 `status` 标为 `pass` 并给出 `ready_for_packaging: true`。

> 诊断输出结构、字段、状态枚举见 [references/diagnostic-output-schema.md](references/diagnostic-output-schema.md)。

> 资料 / 技能 / 外部 / 跨阶段评估规则见 [references/completeness-rules.md](references/completeness-rules.md)。

> 与 `employment-coach-conversation` 的边界、UI 合并展示建议和安全红线见 [references/collaboration-boundary.md](references/collaboration-boundary.md)。

## 默认最小门槛

当模板完备性清单缺失或不完整时，仅使用以下默认门槛，并把 `diagnostic_report.status` 标为 `warning`：

- 资料阶段：至少 1 条 `stage: material` 且 `status: confirmed` 的流程 Handoff todo
- 技能阶段：至少 1 条 `stage: skill` 且 `status: confirmed` 的流程 Handoff todo，且 `payload.skills` 是至少 1 项的 Skill 数组；数组覆盖模板包已有 skill 与本轮新增 skill，每个 Skill 包含明确的 `origin`、`generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output`
- 外部阶段：至少 1 条 `stage: external` 且 `status: confirmed` 的流程 Handoff todo，且 `payload.external_capabilities` 是至少 1 项的外部能力数组；或存在 `status: confirmed` 且数组内含 `kind: skip` 的跳过项
- 出口：三阶段默认门槛均满足，且没有 `needs_review`、`dirty`、`dispatched`、`failed` 的必需相关项

## 输出要求

每次只输出一个结构化诊断报告。报告必须包含：

- `status`: `pass` / `warning` / `blocked`
- `ready_for_packaging`: boolean
- `stage_readiness`: 三阶段状态和原因
- `diagnostic_todos`: Handoff tools 中诊断项的结构化投影
- `todo_correlation`: 表示诊断项与已有流程 Handoff todo 的关联，而不是改写
- `open_questions`: 输入不足或需要系统层补齐的上下文
- `user_summary`: 可被上层流程转述给业务用户的一两句话

不要输出 `<dispatch>`。不要把诊断项伪装成流程 Handoff todo。不要要求业务用户理解诊断内部字段。

## 安全与只读红线

- 不写任何文件
- 不修改流程 Handoff todo 状态
- 诊断项只能通过 Handoff tools 维护，且必须带 `kind: diagnosis`
- 不生成、删除或移动 `ontology/`、`skills/`、`external/` 里的产物
- 不读取、复述、保存 token / 密钥 / 密码 / API Key / 连接串的具体值
- 如果发现凭据值出现在会话、Handoff payload 或产物摘要中，只输出脱敏安全诊断项
- 不暴露 orchestrator、hook、沙箱绝对路径等内部概念给业务用户

## 质量自检

输出前检查：

- [ ] 每条诊断项都回答“还差什么”，不是“执行哪个下游任务”
- [ ] 每条诊断项都有 `level`
- [ ] 每条 blocker 都有 evidence
- [ ] 没有修改流程 Handoff todo 状态
- [ ] 诊断项均通过 Handoff tools 维护，且 `kind: diagnosis`
- [ ] 没有发 `<dispatch>`
- [ ] 没有泄露凭据值或内部路径
- [ ] `user_summary` 足够短，可被雇佣教练复述给业务用户