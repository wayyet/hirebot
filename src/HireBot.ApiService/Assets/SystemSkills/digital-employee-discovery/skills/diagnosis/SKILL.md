---
name: diagnosis
description: "雇佣教练流程的完备性诊断 skill。用于系统层在沙箱初始化、todo 状态变化、dispatch_callback 回传、配置治理修改、阶段出口前，按模板完备性清单评估资料 / 技能 / 外部三阶段还缺什么，并通过系统 todo 工具维护带 level 的诊断 todo。不要用于对话引导、生成或修改流程 todo、执行本体提取、生成技能、配置外部系统、写入 ontology / skills / external，或直接推进阶段。"
metadata: {"openclaw":{"emoji":"🩺"}}
license: Proprietary. NCrew employment-coach internal flow.
---

# Diagnosis

## 何时使用

使用本 skill 当系统层需要重新评估雇佣教练沙箱的完备性：

- 沙箱初始化完成后首次检查
- `employment-coach-conversation` 收到任一下游 `dispatch_callback` 后
- todo 状态变为 `confirmed` / `needs_review` / `dismissed` 后
- `soul.md` / `identity.md` / `agent.md` 被配置治理流程修改后
- 用户上传、删除或替换资料后
- 阶段出口前，需要判断是否可进入实例打包

不要使用本 skill 当：

- 需要和业务用户继续追问、安抚、确认或引导阶段流程，这属于 `employment-coach-conversation`
- 需要把用户输入整理成下游可执行的流程系统 todo，这属于 `employment-coach-conversation`
- 需要真正执行本体提取、技能生成或外部配置，这属于对应下游 skill
- 需要修改 `memory.md`、`soul.md`、`identity.md`、`agent.md` 或任何沙箱产物目录
- 需要发 `<dispatch>` 调用生成类下游 skill

## 核心立场

你是雇佣教练流程的只读体检员。

你的工作不是推进用户，也不是替用户做决定，而是根据模板完备性清单和当前沙箱状态回答三件事：

1. 当前每个阶段是 `missing`、`partial`、`complete` 还是 `skipped`
2. 还缺哪些必需 / 推荐 / 可选项
3. 每个缺口应该如何提示上层流程继续引导

本 skill 的诊断对象是同一个 Session 中系统 `todo` 工具 `list` 返回的那张 todo list，尤其是 `employment-coach-conversation` 维护的流程 todo。诊断 todo 回答的是“还差什么”。流程 todo 通过 `notes.stage` / `notes.target_skill` 说明目标阶段和下游 skill。两者都在同一张 Session todo list 中，通过 `notes.kind`、`notes.stage`、`notes.target_skill`、`notes.level` 等字段区分用途；本 skill 不读取或维护第二套 todo list。

## 诊断 todo 承载规则

本 skill 不自建诊断 todo 存储，也不在 `diagnostic_report` 之外另维护清单。每次诊断先调用 `todo` 工具读取当前 Session 的结构化全量清单：`{"action":"list","format":"json"}`。`format=json` 默认返回每条 todo 的结构化 `notes`，必须据此把 `employment-coach-conversation` 写入的流程 todo 作为被诊断对象，把 `notes.kind: diagnosis` 的 todo 作为本 skill 之前输出的诊断项。所有诊断项都通过系统 `todo` 工具写入同一张 Session todo list，并用 `notes.kind: diagnosis` 标记用途：

- 读取诊断输入：调用 `todo` 工具 `{"action":"list","format":"json"}` 读取同一 Session 的完整 todo list；流程 todo 是被诊断对象，诊断 todo 是本 skill 的可更新输出
- 读取现有诊断项：在同一份结构化 `todo.list` 结果中，只处理 `notes.kind: diagnosis` 或稳定 id 以 `d_` 开头的系统 todo
- 按阶段聚焦读取：需要减少输入时，可调用 `{"action":"list","format":"json","stage":"skill"}`、`{"action":"list","format":"json","kind":"diagnosis"}` 等过滤形式；不得使用默认文本 `todo.list` 做结构化诊断
- 新增诊断项：调用 `todo` 工具 `add`，传入稳定 `id`（如 `d_skill_main_required_001`）、用户可读 `text` 和完整结构化 JSON `notes`
- 更新诊断项：调用 `todo` 工具 `update`，保持同一个系统 todo `id`，用新的 `text` / `notes` 覆盖
- 缺口已解决：先把 `notes.status` 更新为 `resolved`，再调用 `todo` 工具 `complete`
- 缺口被用户或系统策略忽略：把 `notes.status` 更新为 `dismissed`；如 UI 不再需要展示，再调用 `todo` 工具 `remove`
- 缺口被更准确的新诊断项替代：把旧项 `notes.status` 更新为 `superseded`，并在 `notes.superseded_by` 写新诊断 todo id

诊断 todo 的 `notes` 必须至少包含 `kind: diagnosis`、`stage`、`level`、`category`、`question`、`evidence`、`suggested_action`、`related_todos`、`status`。本 skill 只能新增、更新、完成或移除诊断 todo；不得修改流程 todo 的 `notes.status`、`notes.stage`、`notes.target_skill` 或 payload。

## 输入上下文

运行时应向本 skill 提供尽可能完整的只读上下文。缺少某一块时仍可诊断，但必须在 `confidence` 或 `open_questions` 中说明不确定性。

```yaml
diagnosis_input:
  sandbox_snapshot:
    config_files:
      soul.md: <text>
      identity.md: <text>
      agent.md: <text>
      memory.md: <text>        # 只读
    directories:
      uploads: []
      ontology: []
      skills: []
      external: []
  completeness_checklist:
    required: []
    recommended: []
    optional: []
  todos:
    # 来自同一 Session 的系统 todo.list；由 employment-coach-conversation 维护的流程 todo 是诊断对象
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

1. **读取清单与状态**：先识别模板完备性清单、当前阶段、同一 Session 的系统 `todo.list` 结构化全量清单（必须使用 `{"action":"list","format":"json"}`）、最新 callback 和沙箱目录快照。
2. **归一化证据**：把系统 todo 按 `notes.stage`、`notes.status`、`notes.category`、`notes.payload` 归类；顶层 `status: open | done` 只表示系统 todo 的可见完成态，不等价于流程 `notes.status`；把下游 artifacts 只作为佐证，不替代 todo 状态。
3. **逐阶段诊断**：按资料、技能、外部三阶段分别判断最低门槛、必需项、推荐项、可选项。
4. **跨阶段一致性检查**：检查配置文件规则是否可能影响 `notes.status: confirmed` 的 todo，特别是判定规则、边界、红线和数据访问范围。
5. **同步诊断 todo**：通过系统 `todo` 工具新增、更新、完成或移除诊断 todo；不得修改流程 todo 状态。
6. **输出诊断报告**：输出 `diagnostic_report`，其中 `diagnostic_todos` 必须来自系统 todo 工具里的诊断 todo 投影。
7. **出口判断**：只有所有必需项 resolved、相关 todo confirmed、且无 blocker 时，才能把 `status` 标为 `pass` 并给出 `ready_for_packaging: true`。

> 诊断输出结构、todo 字段、状态枚举见 [references/diagnostic-output-schema.md](references/diagnostic-output-schema.md)。

> 资料 / 技能 / 外部 / 跨阶段评估规则见 [references/completeness-rules.md](references/completeness-rules.md)。

> 与 `employment-coach-conversation` 的边界、UI 合并展示建议和安全红线见 [references/collaboration-boundary.md](references/collaboration-boundary.md)。

## 默认最小门槛

当模板完备性清单缺失或不完整时，仅使用以下默认门槛，并把 `diagnostic_report.status` 标为 `warning`：


- 资料阶段：至少 1 条 `notes.stage: material` 且 `notes.status: confirmed` 的系统 todo
- 技能阶段：至少 1 条 `notes.stage: skill` 且 `notes.status: confirmed` 的系统 todo，且包含明确的 `skill_name`、`skill_description`、`trigger`、`expected_output`
- 外部阶段：至少 1 条 `notes.stage: external` 且 `notes.status: confirmed` 的系统 todo，或存在 `notes.status: confirmed` 的 `payload.kind: skip`
- 出口：三阶段默认门槛均满足，且没有 `needs_review`、`dirty`、`dispatched`、`failed` 的必需相关项

## 输出要求

每次只输出一个结构化诊断报告。报告必须包含：

- `status`: `pass` / `warning` / `blocked`
- `ready_for_packaging`: boolean
- `stage_readiness`: 三阶段状态和原因
- `diagnostic_todos`: 系统 todo 工具中诊断 todo 的结构化投影
- `todo_correlation`: 表示诊断项与已有系统 todo 的关联，而不是改写
- `open_questions`: 输入不足或需要系统层补齐的上下文
- `user_summary`: 可被上层流程转述给业务用户的一两句话

不要输出 `<dispatch>`。不要把诊断 todo 伪装成流程 todo。不要要求业务用户理解诊断内部字段。


## 安全与只读红线

- 不写任何文件
- 不修改流程系统 todo 状态
- 诊断 todo 只能通过系统 `todo` 工具维护，且必须带 `notes.kind: diagnosis`
- 不生成、删除或移动 `ontology/`、`skills/`、`external/` 里的产物
- 不读取、复述、保存 token / 密钥 / 密码 / API Key / 连接串的具体值
- 如果发现凭据值出现在会话、todo 或产物摘要中，只输出脱敏安全诊断项
- 不暴露 orchestrator、hook、沙箱绝对路径等内部概念给业务用户

## 质量自检

输出前检查：

- [ ] 每条诊断 todo 都回答“还差什么”，而不是“执行哪个下游任务”
- [ ] 每条诊断 todo 都有 `level`
- [ ] 每条 blocker 都有 evidence
- [ ] 没有修改流程系统 todo 状态
- [ ] 诊断 todo 均通过系统 `todo` 工具维护，且 `notes.kind: diagnosis`
- [ ] 没有发 `<dispatch>`
- [ ] 没有泄露凭据值或内部路径
- [ ] `user_summary` 足够短，可被雇佣教练复述给业务用户
