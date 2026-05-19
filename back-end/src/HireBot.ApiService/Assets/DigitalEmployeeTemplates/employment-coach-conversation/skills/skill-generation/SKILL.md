---
name: skill-generation
description: 根据结构化工单输入、用户会话描述或上传的 skill 文件，抽取统一 SkillSpec，生成可直接运行的业务技能包，并仅写入当前沙箱 skills/ 目录。
compatibility: HireBot employment-coach-conversation v1.0
metadata:
  openclaw:
    emoji: "🧩"
  category: generation
  autonomy: 80
  trigger: hiring-session-skill, skill-stage-active
  input: skill-workorder, uploaded-skill-files, user-dialogue
  output: skill-packages, emit-artifact
---

# Skill Generation

当用户要求根据技能工单、描述、Markdown、文本、JSON、YAML 或 zip 文件创建、更新、合并、规范化业务技能包时，使用本技能。输入可以是结构化的技能工单，也可以是非结构化的会话描述或上传文件；无论哪种输入，都必须先抽取为统一的 SkillSpec 中间模型，再映射到固定模板生成技能文件，最后通过质量校验后才落盘。整个过程中要严格区分输入来源、提炼说明、产物质量和消费契约，确保生成过程可审阅、可复盘、可迁移。

本技能的职责是生成以 `SKILL.md` 为核心的业务技能包。核心思想是先把非结构化输入抽取为统一的 SkillSpec，再映射到固定模板，生成后通过最小质量校验，通过后才落盘。生成过程中严格区分输入来源、提炼说明、产物质量和消费契约，确保生成过程可审阅、可复盘、可迁移。

## 启动前置条件

只有在上游 `employment-coach-conversation` 已经：

- 完成技能定义（已发 `skill_workorder_summary`）
- 且用户明确确认"开始生成技能实现"

之后，才允许执行本技能。

硬约束：

- **禁止**因为看到了 `skill_workorder_summary` 就自动开始生成。
- 若上游只发出了 `skill_generation_ready`，表示仍在等待用户确认；此时本技能不得写入任何正式技能文件。
- 若用户明确表示"先不生成"、"稍后再说"、"先继续别的阶段"，本技能保持不启动，直到上游再次给出明确开始信号。

## 输入类型

支持四类输入：

- 会话描述：例如"它要会处理退货咨询、订单查询"。
- 上传文件：Markdown、文本、JSON、YAML 或 zip。
- 混合输入：上传文件作为基线，会话描述作为增量补充。
- 结构化工单输入：包含 `origin`、`generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output` 等字段的技能清单，由上游技能传入。

同时读取当前沙箱 `skills/` 目录快照，用于同名覆盖、异名新增和去重。

## emit_artifact 使用规范

本 skill 执行期间须在两个关键节点调用 `emit_artifact`，推动前端**技能实现轨**更新。它们**不回写**主流程中"技能定义阶段已完成"的语义。

### 进度节点（isTerminal: false）

在开始处理第一个技能规格时调用：

```json
{
  "kind": "data",
  "artifactType": "skill_generation_progress",
  "label": "正在生成业务技能包，共 {N} 个技能待处理",
  "skillName": "skill-generation",
  "stage": "skill-generation",
  "isTerminal": false,
  "displayHint": "progress",
  "data": {
    "total_skills": 3,
    "completed_skills": 0,
    "status": "running"
  }
}
```

### 完成节点（isTerminal: true）

所有技能包落盘并通过质量校验后调用：

```json
{
  "kind": "data",
  "artifactType": "skill_generation_done",
  "label": "技能包已生成完毕，共 {N} 个技能，可继续后续外部配置或打包流程",
  "skillName": "skill-generation",
  "stage": "skill-generation",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_skills": 3,
    "generated_count": 2,
    "reused_count": 1,
    "skill_slugs": ["refund-eligibility-check", "order-status-query", "return-progress-track"],
    "status": "done"
  }
}
```

### 约束

- **先调用后输出**：同一轮次识别到可推送的阶段事件时，先调用 `emit_artifact`，再继续文件生成或对话输出
- **data 禁止凭据**：data 字段中不得写入 token / 密钥 / 密码 / API Key
- **label 用业务语言**：描述对用户有意义的进度，不暴露内部字段名

## 统一中间模型

所有输入必须先归一为 SkillSpec：

```json
{
  "name": "refund-order-assistant",
  "display_name": "退货与订单查询助手",
  "description": "处理退货咨询、订单状态查询、进度追踪",
  "triggers": ["退货", "订单查询", "物流进度"],
  "capabilities": [
    {
      "id": "refund_apply",
      "goal": "受理退货申请",
      "inputs": ["订单号", "退货原因"],
      "outputs": ["受理结果", "下一步指引"],
      "fallback": "信息不足时引导补充"
    }
  ],
  "boundaries": [
    "不承诺退款时效",
    "不处理财务打款"
  ],
  "examples": [
    {
      "user": "我要退货",
      "assistant": "请提供订单号和退货原因，我来为你发起申请。"
    }
  ],
  "source": "conversation|upload|workorder",
  "version": "1.0.0"
}
```

## 执行流程

### Phase 0: 入口分流

先判断请求路径：

- 等待确认路径：上游尚未取得用户关于"是否开始生成技能实现"的明确肯定，此时不执行本技能，只等待。
- 结构化工单路径：输入包含 `generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output` 字段的技能清单，至少 1 项。
- 直接路径：用户已经给出明确业务域、触发词、能力或上传了候选 skill 文件。
- 模糊路径：用户只说"帮我做个 skill""把这些能力整理成 skill"，但缺少业务域、能力边界或产物目标。
- 更新路径：现有 `skills/<skill_slug>/` 已存在，需要同名覆盖、增量合并或跳过。

等待确认路径直接停止，不落盘任何技能文件。结构化工单路径和直接路径继续 Phase 0.5。模糊路径先做需求诊断：列出最多 3 个候选业务域、每个候选域的触发词和预计能力，要求用户确认后再落盘。

### Phase 0.5: 创建自包含技能目录

在解析和渲染前先确定目标目录；`contracts/` 仅在生成 READY projection contract 或 draft projection notes 时创建：

```text
skills/<skill_slug>/
  SKILL.md
  metadata.json
  references/
    source-digest.md
    extraction-notes.md
    quality-report.md
  contracts/                         # optional, only when projection data exists
    projections/
      ontology-extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
```

目录必须自包含：生成 skill 所需的摘要、来源、质量报告都放在该 skill 目录内；如生成 projection contract 或 draft notes，也必须放在该 skill 目录内。不要把生成过程依赖散落到 `config/`、`ontology/`、`external/` 或临时目录。

### Phase 1: 输入采集与来源归档

对不同输入执行不同采集策略：

- 结构化工单：保留 `origin`、`generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output`，写入 `references/source-digest.md` 的 workorder source 区块。
- 会话描述：保留用户原话，写入 `references/source-digest.md` 的 conversation source 区块。
- 上传文件：解析 Markdown、文本、JSON、YAML；zip 递归读取候选 skill 文件；保留文件清单、解析结论和不可解析项。**当由上游 `employment-coach-conversation` 触发时，输入以 `skill_workorder_summary` 为主工单；若工单条目中附带 `source_path` 或来源文件提示，直接按这些真实路径读取，不要运行 `shell: ls` 探索文件系统。** `source_path` 为 `null` 的条目是纯文本描述，无对应文件。`skill_workorder_summary` 的 `data.workspace_root` 是雇佣教练会话初始化时由沙箱解压工具创建并锁定的真实绝对路径（运行时确定，本 skill 当作不透明字符串使用），本 skill 的所有产物必须写入 `<workspace_root>/skills/<skill-slug>/`（用 artifact 收到的真实路径替换 `<workspace_root>`）；若 `workspace_root` 缺失，停下来报错，不要靠 `ls /workspace` 推断或自行拼接 `/workspace/<slug>`。
- 混合输入：上传文件作为基线，会话描述作为增量补充，不用会话描述覆盖文件里更明确的能力定义。

来源归档必须记录：来源类型、可信度、抽取到的能力、未决问题和被丢弃内容。不要把 token、密钥、密码、连接串写入归档。

### Phase 1.5: 采集质量检查点

进入结构化归一前检查：

- 至少有一个可解释的业务域或能力域；结构化工单路径中可由 `skill_name` 和 `skill_description` 直接给出。
- 每个结构化工单条目都明确 `generation_action`；`reuse_existing` 条目有已有产物引用，`generate_new` 条目有生成所需字段。
- 至少能推导出一个 trigger；结构化工单路径中优先使用 `trigger` 字段。
- 至少能推导出一个 capability；结构化工单路径中由 `skill_description` + `expected_output` 构造。
- 所有敏感字段已脱敏或阻断。
- 上传文件不可解析时，已记录失败原因和补全建议。

检查失败时，不写正式 skill。可以返回草稿 SkillSpec 和待补全项。

### Phase 2: SkillSpec 提炼

把所有来源统一提炼为 SkillSpec。提炼时执行三重验证：

1. 复现性：能力是否在用户描述、文件结构或示例中至少有明确依据。
2. 可执行性：能力是否能落到输入、输出、失败兜底和处理流程。
3. 排他性：能力是否足够具体，不只是"通用助手""回答问题"这类空泛描述。

每个 capability 都要保留来源摘要和归属理由，写入 `references/extraction-notes.md`。

### Phase 3: Skill 构建

按模板填充产物：

- `SKILL.md`：业务技能说明、触发条件、能力清单、处理流程、边界、不做事项、对话示例、Projection Contracts 消费章节。
- `metadata.json`：完整 SkillSpec、质量门、来源模式、版本和生成策略。
- `contracts/projections/ontology-extraction/**`：当已有足够本体 projection 信息时，让生成出来的业务 skill 按本仓库 consumer skill 方式消费 `ontology-extraction` projection。
- `references/source-digest.md`、`references/extraction-notes.md`、`references/quality-report.md`：让生成过程可审阅、可复盘、可迁移。

模板参考文件位于本技能目录：

- `references/generated-skill-template.md`
- `references/projection-contract-template.md`
- `references/quality-checklist.md`

### Phase 4: 质量验证

生成后必须执行最小验证：

- Sanity Check：用 2-3 个典型用户请求检查触发词、能力选择和输出边界是否匹配。
- Edge Case：用 1 个信息不足或越界请求检查是否会补槽、拒绝或转交，而不是编造结果。
- Contract Check：如生成了 READY projection contract，确认 `contract-index.json` 的 path 指向真实 projection 文件，projection 含 `prompt_projection`、`delivery_artifacts`、`dropped_items`、`open_questions`；如信息不足，只写 draft/notes，不把 contract 标为 READY，也不阻断基础业务 skill 落盘。
- Safety Check：确认产物不含明文 token、密钥、密码、连接串或凭据。
- Self-contained Check：复制整个 `skills/<skill_slug>/` 后仍能独立被 loader 发现和人工审阅。

结果写入 `references/quality-report.md`。未通过时阻止落盘或保留草稿并明确失败原因。

### Phase 5: 双视角精炼

质量验证通过后做两轮自检，不需要额外写入主 agent：

- 生成器视角：检查结构完整、模板变量无残留、文件路径正确、质量门完整。
- 消费者视角：检查生成出来的业务 skill 是否容易触发、边界清楚、projection contract 可被 runtime 自动发现。

如果两轮自检发现问题，回到 Phase 2 或 Phase 3 修正，再重新执行 Phase 4。

## 兼容执行清单

1. 输入判型：判断是结构化工单、会话描述、上传文件还是混合输入。
2. 内容解析：
   - 结构化工单：读取每个技能条目的 `origin`、`generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output`、`from_upload`、已有 skill 引用。
   - 会话描述：抽取触发词、能力项、输入、输出、边界和示例。
   - 上传文件：解析 Markdown、文本、JSON、YAML，并映射到 SkillSpec。
   - zip 文件：递归读取候选 skill 文件，优先保留原文件能力定义，再结构化归一。
3. 结构化归一：补齐缺省字段，规范化 `name`、`display_name`、`description`、`triggers`、`capabilities`、`boundaries`、`examples`、`source`、`version`。
4. Slug 生成：由能力名称生成 `skill_slug`，使用小写短横线，只保留字母、数字和短横线。
5. 冲突处理：读取现有 `skills/`，按同名覆盖、异名新增规则合并。多能力输入可按业务域合并为一个技能，也可在用户启用多技能拆分时按业务域生成多个技能。
6. Projection 契约生成：有足够本体 projection 信息时，为产出的业务 skill 生成 READY consumer-skill projection 目录；信息不足时只记录 draft/notes，不伪造 READY contract。
7. 模板渲染：按固定业务技能模板生成 `SKILL.md` 和 `metadata.json`；如具备 projection 信息，同时生成 projection contract 伴随文件。
8. 质量校验：未通过时阻止落盘，返回失败原因。
9. 写入产物：只写入 `skills/<skill_slug>/` 下的技能文件、metadata、references，以及可选 projection contract 文件。
10. 返回摘要：输出 `technical_artifact`、`skill_results` 与 `user_summary`。

## SKILL.md 业务模板

生成的业务技能必须使用以下结构：

```markdown
---
name: {{name}}
description: |
  {{description}}
  当用户提到：{{triggers_joined}} 时触发。
---

# {{display_name}}

## 适用场景
- {{scenario_1}}
- {{scenario_2}}

## 能力清单
### {{capability_1.goal}}
- 输入：{{capability_1.inputs}}
- 输出：{{capability_1.outputs}}
- 失败兜底：{{capability_1.fallback}}

## 处理流程
1. 意图识别与槽位补全
2. 执行动作或给出指引
3. 返回结果并提示下一步

## 边界与不做
- {{boundary_1}}
- {{boundary_2}}

## 对话示例
用户：{{example_user}}
助手：{{example_assistant}}
```

如果当前运行时的 skill 解析器需要单行 frontmatter，则把 `description` 渲染为单行，不改变正文语义。

## 生成 Skill 的 Projection Contract 模板

生成出来的业务 skill 可以按本仓库 consumer skill 方式接入 `ontology-extraction` projection。projection contract 是条件增强：有足够 ontology projection 信息时生成 READY contract；信息不足时生成 draft/notes，不能伪造 READY contract，也不能因此阻断基础业务 skill 落盘。

当 projection 信息足够时，每个生成的技能包包含：

```text
skills/<skill_slug>/
  SKILL.md
  metadata.json
  references/
    source-digest.md
    extraction-notes.md
    quality-report.md
  contracts/
    projections/
      ontology-extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
```

生成的业务 `SKILL.md` 可以包含以下 consumer skill 章节，并按具体业务域裁剪 supported deliverables、projection types 和 local exclusions。仅当该技能确实带有 `contracts/projections/**/contract-index.json` 或 draft projection notes 时写入本章节：

```markdown
## Projection Contracts

This skill may be augmented by bound `ontology-extraction` projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Projection discovery, route selection, and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contracts/projections/ontology-extraction/contract-index.json` first, then the selected topic's `README.md` and `REVIEW.md`, and then the chosen `*.projection.json` file.

### Projection Consumption

- Read the selected projection before planning implementation details.
- Only consume the projection fields and target views this skill actually supports, especially `concept_mappings`, `relation_mappings`, `constraint_mappings`, `prompt_projection`, `delivery_artifacts`, `mapping_policy`, `open_questions`, and `dropped_items`.
- Treat the selected projection as authoritative for terminology, clarifications, dropped scope, and blocking conditions.

### Blocking Rules

- If route selection is blocked, ambiguous, or does not safely cover the request, surface that limitation instead of guessing.
- If `mapping_policy` requires `block_or_escalate`, or `open_questions` is non-empty, do not finalize the output before surfacing the issue.
- Do not recreate items listed in `dropped_items`.
```

生成 READY `contract-index.json` 时必须：

- 设置 `producer_skill` 为 `ontology-extraction`。
- 设置 `consumer_skill` 为当前生成的 `{{name}}`。
- 至少包含 1 个 topic，`domain_slug` 优先来自业务域 slug。
- 至少包含 1 个 READY view，path 指向同目录下真实存在的 `*.projection.json`。
- 默认启用 `prefer_ready_only: true` 与 `block_on_open_questions: true`。

生成 projection document 时必须：

- 使用 `docs/skill-projection-document.schema.json` 兼容结构。
- 包含 `prompt_projection`、`delivery_artifacts`、`dropped_items`、`open_questions`。
- 对 `mapping_policy.unresolved_item_policy` 使用 `block_or_escalate`。
- 将 `delivery_artifacts.path` 限定到该业务 skill 真实会产出的文件或响应结构。
- 如果用户输入中没有足够信息生成 READY projection，生成 WARNING/草稿摘要但不要伪造 READY contract，也不要阻断基础业务 skill 落盘。

## 伴随文件模板

生成业务 skill 时优先读取本技能目录下的伴随模板：

- `references/generated-skill-template.md`：生成 `SKILL.md` 的扩展业务模板。
- `references/projection-contract-template.md`：生成 consumer projection contract 的最小结构。
- `references/quality-checklist.md`：生成后质量检查与失败处理。

如果伴随模板缺失，可以使用本文件中的内联模板继续生成，但必须在 `user_summary` 中说明使用了内联降级模板。

## 质量校验

落盘前必须通过以下检查：

- 完整性：`name`、`description`、`capabilities` 必填。
- 可触发性：至少包含 1 个 trigger。
- 可执行性：每个 capability 都必须有输入、输出和兜底。
- 安全性：不得写入明文 token、密钥、密码、连接串或凭据。
- 自包含性：来源摘要、提炼说明、质量报告必须随生成 skill 一起落在 `skills/<skill_slug>/`；如生成 projection contract，也必须落在同一 skill 目录内。
- 可消费性：如生成 READY projection contract，生成 skill 的 `contract-index.json` 必须能被 runtime 从 `contracts/projections/**/contract-index.json` 自动发现。

如检测到敏感明文即将写入，拒绝写入并输出安全告警。敏感内容应替换为 `[REDACTED]`，但不要把真实值写入任何产物。

## 限制与边界

- 只生成业务技能包，不更新主 agent 行为约束。
- 不识别或吸收行为约束类信息；这类内容应交给主 skill 更新 `agent.md`。
- 不修改 `config/`、`ontology/`、`external/`。
- 不直接推送 UI，不触发诊断 skill 重跑，不更新主流程工单。
- 不覆盖旧技能，除非新 SkillSpec 的规范化 `name` 与现有技能同名。
- 不把 `skill-generation` 自身注册为 projection consumer；projection consumer 结构只写入生成出来的业务 skill。

## 失败与回退

- 上传文件不可解析：不覆盖旧技能，返回可读错误，并建议用户改用会话描述补全。
- 必填字段严重缺失：生成草稿 SkillSpec，但不落盘正式技能，在 `user_summary` 中列出待补全项。
- 模板渲染异常：保留写入前状态，返回异常上下文。
- 校验失败：阻止落盘，返回失败项列表。

## 输出格式

最终输出必须包含三个部分：

```json
{
  "technical_artifact": [
    "skills/<skill_slug>/SKILL.md",
    "skills/<skill_slug>/metadata.json",
    "skills/<skill_slug>/references/source-digest.md",
    "skills/<skill_slug>/references/extraction-notes.md",
    "skills/<skill_slug>/references/quality-report.md"
  ],
  "optional_projection_artifact": [
    "skills/<skill_slug>/contracts/projections/ontology-extraction/contract-index.json",
    "skills/<skill_slug>/contracts/projections/ontology-extraction/README.md",
    "skills/<skill_slug>/contracts/projections/ontology-extraction/<domain-slug>/<domain-slug>.<projection-type-short>.projection.json",
    "skills/<skill_slug>/contracts/projections/ontology-extraction/<domain-slug>/README.md",
    "skills/<skill_slug>/contracts/projections/ontology-extraction/<domain-slug>/REVIEW.md"
  ],
  "skill_results": [
    {
      "skill_name": "订单状态查询",
      "generation_action": "reuse_existing",
      "status": "reused",
      "artifact": "skills/order-status-query/SKILL.md"
    },
    {
      "skill_name": "退货资格初判",
      "generation_action": "generate_new",
      "status": "success",
      "artifact": "skills/seven-day-return-initial-check/SKILL.md"
    }
  ],
  "user_summary": "已新增 1 个技能：退货与订单查询助手；现在能处理退货咨询、订单状态查询和物流进度追踪。"
}
```

如果发生新增、复用和更新混合，摘要必须按复用、新增、更新、跳过、失败分类说明。`skill_results` 必须覆盖本次处理的每个技能条目，给出 `generation_action` 与结果；失败项必须包含 `status: failed`、可读 `error`，以及是否保留旧产物。

## References

- `references/generated-skill-template.md`：生成业务 `SKILL.md` 的扩展模板。
- `references/projection-contract-template.md`：生成 consumer projection contract 的最小结构。
- `references/quality-checklist.md`：落盘前质量检查与失败处理。
- `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：consumer projection 目录命名规范。
- `../../../docs/skill-projection-contract-index.schema.json`：contract index runtime schema。
- `../../../docs/skill-projection-document.schema.json`：projection document runtime schema。
