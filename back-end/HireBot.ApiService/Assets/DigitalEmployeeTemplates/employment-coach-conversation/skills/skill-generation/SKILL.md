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
- 即便本轮技能条目全部是 `reuse_existing`，也仍要执行一轮 `skill-generation`：负责完成复用结果的统一校验、落盘和完成信号，不得因为“没有新增技能”而整步跳过。

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
      ontology_extraction/
        contract-index.json          # 投影选择与路由索引入口（runtime 发现入口）
        README.md                    # 命名空间级说明
        <domain-slug>/
          <domain-slug>.domain-model.projection.json
          <domain-slug>.json-schema.projection.json
          <domain-slug>.prompt-constraint.projection.json
          <domain-slug>.workflow-contract.projection.json
```

目录必须自包含：生成 skill 所需的摘要、来源、质量报告都放在该 skill 目录内；如生成 projection contract 或 draft notes，也必须放在该 skill 目录内。不要把生成过程依赖散落到 `config/`、`ontology/`、`external/` 或临时目录。

**Projection Pass 预生成检查**：若 `ontology/projections/<skill-slug>/` 目录已存在（由上游 `ontology-extraction` projection pass 预生成），Phase 3 将直接读取该目录中的 projection 文件来生成 consumer contract 结构，无需重新推导；consumer 侧仍统一落盘到 `contracts/projections/ontology_extraction/`。

**Projection 绑定强制覆盖规则**：若输入 payload 中包含 `projection_binding_confirmed: true` 或 `projection_contract_mode: "required"`，则本轮运行视为“用户已确认把 producer projection 绑定进 skill”。此时 consumer contract 为**强制产物**：必须从 `<workspace_root>/ontology/projections/<skill-slug>/` 成功 materialize 出 `contracts/projections/ontology_extraction/contract-index.json` 与 4 个标准 view 文件；若 source 缺失、source 无效、结构校验失败，或无法完成落盘确认，则本轮 `skill-generation` **必须阻断并返回失败原因**，不得以“仅基础 skill 文件”作为成功结果，也不得发出 `skill_generation_done`。

### Phase 1: 输入采集与来源归档

对不同输入执行不同采集策略：

- 结构化工单：保留 `origin`、`generation_action`、`skill_name`、`skill_description`、`trigger`、`expected_output`，写入 `references/source-digest.md` 的 workorder source 区块。
- 会话描述：保留用户原话，写入 `references/source-digest.md` 的 conversation source 区块。
- 上传文件：解析 Markdown、文本、JSON、YAML；zip 递归读取候选 skill 文件；保留文件清单、解析结论和不可解析项。**当由上游 `employment-coach-conversation` 触发时，输入以 `skill_workorder_summary` 为主工单；若工单条目中附带 `source_path` 或来源文件提示，直接按这些真实路径读取，不要运行 `shell: ls` 探索文件系统。** `source_path` 为 `null` 的条目是纯文本描述，无对应文件。`skill_workorder_summary` 的 `data.workspace_root` 是雇佣教练会话初始化时由沙箱解压工具创建并锁定的真实绝对路径（运行时确定，本 skill 当作不透明字符串使用），本 skill 的所有产物必须写入 `<workspace_root>/skills/<skill-slug>/`（用 artifact 收到的真实路径替换 `<workspace_root>`）；若 `workspace_root` 缺失，停下来报错，不要靠 `ls /workspace` 推断或自行拼接 `/workspace/<slug>`。
- 混合输入：上传文件作为基线，会话描述作为增量补充，不用会话描述覆盖文件里更明确的能力定义。
- **Projection 发现（可选 / 条件阻断）**：当 `workspace_root` 可用时，对每个待生成 skill 检查 `<workspace_root>/ontology/projections/<skill-slug>/` 目录是否存在。若存在，扫描其中的 `*.projection.json` 文件；按 `domain-slug` 聚合已有 view，读取每个文件内的 `open_questions` 字段：为空或 null 则视为 **READY projection**，非空则视为 **WARNING projection**。记录已有 view 列表与路径，供 Phase 3 使用。若目录不存在：
  - 当本轮未要求绑定 projection 时，可继续正常流程；
  - 当 `projection_binding_confirmed: true` 或 `projection_contract_mode: "required"` 时，立即记为阻断原因，Phase 3 Step 2 不得降级跳过。
  - 若发现有效 projection source，Phase 3 Step 2 必须优先使用 `scripts/materialize-consumer-projection-contract.py` 将 source 稳定展开为 consumer 侧 4 个标准 view；如果当前环境无法运行 Python，则按该脚本算法等价手动写入，不得退化为只写 1 个 view。

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

**⚠️ 执行顺序强制约束**：本阶段分为 Step 1（基础文件）和 Step 2（投影契约）。Step 1 是**强制且不可跳过**的——无论投影是否存在、无论降级与否，都必须先完成 Step 1 的全部文件写入并确认落盘，再处理 Step 2。

#### Step 1: 写入基础技能文件（强制，不可跳过）

按模板渲染并**立即调用 write_file 写入**以下文件：

1. `skills/<skill-slug>/SKILL.md`：业务技能说明、触发条件、能力清单、处理流程、边界、不做事项、对话示例。模板中的 `{{projection_contracts_section}}` 只能在 Step 2 已真实写出 `contracts/projections/ontology_extraction/contract-index.json` 后再展开；如果 Step 2 跳过、失败或仅保留 notes，必须把该占位替换为空字符串，**绝不允许**在没有 contract 文件时保留 Projection Contracts 章节。
2. `skills/<skill-slug>/metadata.json`：完整 SkillSpec、质量门、来源模式、版本和生成策略。
3. `skills/<skill-slug>/references/source-digest.md`：来源归档摘要。
4. `skills/<skill-slug>/references/extraction-notes.md`：抽取说明。
5. `skills/<skill-slug>/references/quality-report.md`：质量检查报告（Phase 4 后更新）。

**写入确认**：每个文件写入后，用 read_file 确认文件已落盘且内容非空。若写入失败，立即重试。**在 Step 1 全部 5 个文件确认落盘之前，不得进入 Step 2，也不得发出任何 "生成完毕" 的 artifact。**

#### Step 2: 写入投影契约（条件必做；若 projection_binding_confirmed=true，则本步骤为强制阻断门）

Step 1 全部确认落盘后，按以下三条路径处理投影契约：

- **强制覆盖规则（优先于路径 A/B/C）**：若输入 payload 包含 `projection_binding_confirmed: true` 或 `projection_contract_mode: "required"`：
  - 路径 B 和路径 C 只能用于定位失败原因，**不能**作为成功完成路径；
  - 只有当路径 A 成功落出完整 consumer contracts 并通过读取确认后，才允许继续本轮 skill 生成；
  - 任何“source 目录不存在 / source 无效 / 无法补全 4 个标准 view / 结构校验失败 / 写入确认失败”都必须阻断本轮运行，向上游返回失败原因，并明确指出需要回到 projection pass 或前置材料阶段处理。

- **路径 A（projection pass 预生成 — 优先）**：若 Phase 1 发现了来自 `ontology/projections/<skill-slug>/` 的 projection 文件：
  0. **优先使用确定性脚本落盘**：运行：
     ```bash
     python "{baseDir}/scripts/materialize-consumer-projection-contract.py" --workspace-root "<workspace_root>" --skill-slug "<skill-slug>" --skill-name "<skill-name>"
     ```
     脚本会读取 `<workspace_root>/ontology/projections/<skill-slug>/*.projection.json`，生成 `contract-index.json`、`README.md` 和 4 个标准 view 文件，并在写入后执行结构校验。脚本返回 `status: "done"` 时，直接进入第 6 步读取确认；若脚本不可用，必须按下面第 1-7 步等价手动执行。
  1. 读取该 skill 在 `<workspace_root>/ontology/projections/<skill-slug>/` 目录下的全部 projection 文件，并按 `domain-slug` 聚合已有 view。
  2. **验证源文件完整性**：每个候选源文件必须是以下二者之一：
     - consumer flat shape：顶层至少包含 `projection_type`、`source_slice`、`intended_consumers`、`concept_mappings`
     - canonical ontology shape：顶层包含 `projection` 对象，且 `projection` 内至少包含 `projection_type`、`source_slice`、`intended_consumers`，同时顶层包含 `concept_mappings`
     若文件仅含 `note`/`source_projection_path` 等 stub 引用，视为**源文件无效**，跳过该源文件。
  3. **固定产出 4 个本地 view**：无论上游当前给了 1 个还是多个源 view，consumer contract 都必须落盘 4 个标准 view：`domain-model`、`json-schema`、`prompt-constraint`、`workflow-contract`。若上游缺少某些 view，则以最相关的源 projection 为语义真源，拆分并派生缺失 view，保持“薄文件、分职责”的四视图结构。
  4. 在 `skills/<skill-slug>/contracts/projections/ontology_extraction/` 下写入以下文件：
     - `contract-index.json`（投影选择与路由索引，格式见下文）
     - `README.md`（命名空间级说明）
     - `<domain-slug>/<domain-slug>.domain-model.projection.json`
     - `<domain-slug>/<domain-slug>.json-schema.projection.json`
     - `<domain-slug>/<domain-slug>.prompt-constraint.projection.json`
     - `<domain-slug>/<domain-slug>.workflow-contract.projection.json`
     4 个文件都必须是完整 JSON，而不是 stub 引用；同时将 `source_slice.path` 更新为从 skill 目录出发的相对路径。
  4. `contract-index.json` 最小必要格式：
     ```json
     {
       "producer_skill": "ontology_extraction",
       "consumer_skill": "<skill-slug>",
       "default_selection_policy": {
         "prefer_ready_only": true,
         "block_on_open_questions": true
       },
       "topics": [
         {
           "domain_slug": "<domain-slug>",
           "intent_keywords": ["..."],
           "default_target_view": "workflow-contract",
            "views": [
              {
               "target_view": "domain-model",
               "projection_type": "domain_model_projection",
               "status": "READY",
               "path": "<domain-slug>/<domain-slug>.domain-model.projection.json"
              },
              {
               "target_view": "json-schema",
               "projection_type": "json_schema_projection",
               "status": "READY",
               "path": "<domain-slug>/<domain-slug>.json-schema.projection.json"
              },
              {
               "target_view": "prompt-constraint",
               "projection_type": "prompt_constraint_projection",
               "status": "READY",
               "path": "<domain-slug>/<domain-slug>.prompt-constraint.projection.json"
              },
              {
               "target_view": "workflow-contract",
               "projection_type": "workflow_contract_projection",
               "status": "READY",
               "path": "<domain-slug>/<domain-slug>.workflow-contract.projection.json"
              }
            ]
          }
        ]
     }
     ```
     可选追加 `topic_scoring`、`target_view_scoring`、`selection_algorithm` 等完整选择逻辑，但最小结构只需上述字段。
  5. 若源 projection 为 WARNING：对应 topic 的 4 个本地 view 都继承 `"WARNING"` 状态，并追加 `open_questions` 透传非空内容。**WARNING 不是跳过 contract 的理由**；只要源 projection 有效，就仍然要写完整的 consumer contract 文件。
  6. 写入后用 read_file 确认 `contract-index.json`、`README.md` 以及 4 个 projection 文件均已落盘。
  7. 只有在第 6 步全部通过后，才允许回写 `skills/<skill-slug>/SKILL.md` 中的 `{{projection_contracts_section}}`，把 Projection Contracts 章节真正展开；若任一 contract 文件缺失、为空或结构不完整，必须：
     - 删除或回退不完整的 contract 文件；
     - 把 `SKILL.md` 中的 `{{projection_contracts_section}}` 替换为空字符串；
     - 在 `references/quality-report.md` 记录“发现 projection source，但 consumer contract 落盘失败，本轮 skill-generation 已阻断，等待回到 projection 准备阶段修复”。
- **路径 B（原有推导逻辑 — 仅限未绑定模式）**：Phase 1 未发现任何 projection 文件时，**或路径 A 源文件无效时**，仅当本轮**未**要求绑定 projection，才允许沿用原逻辑：信息足够则生成 READY contract，否则写 draft/notes。若本轮要求绑定 projection，则路径 B 只能输出诊断信息，不能视为成功。
- **路径 C（无投影信息 — 仅限未绑定模式）**：Phase 1 未发现投影且无法从上下文推导足够信息时，仅当本轮**未**要求绑定 projection，才允许跳过 `contracts/` 目录并把 Step 1 基础文件作为完整产物；若本轮要求绑定 projection，则必须阻断并返回“没有可消费 producer projection”的失败原因。

模板参考文件位于本技能目录：

- `references/generated-skill-template.md`
- `references/projection-contract-template.md`
- `references/quality-checklist.md`

### Phase 4: 质量验证

生成后必须执行最小验证：

- Base File Check（强制）：确认 `skills/<skill_slug>/SKILL.md`、`metadata.json`、`references/source-digest.md`、`references/extraction-notes.md`、`references/quality-report.md` 全部存在且内容非空。若任何基础文件缺失，Phase 3 Step 1 写入失败，必须回到 Step 1 重新写入，不得跳过。
- Sanity Check：用 2-3 个典型用户请求检查触发词、能力选择和输出边界是否匹配。
- Edge Case：用 1 个信息不足或越界请求检查是否会补槽、拒绝或转交，而不是编造结果。
- Contract Check（仅当 Step 2 生成了 contracts/ 时）：确认 `contracts/projections/ontology_extraction/contract-index.json` 存在且结构完整（含 `producer_skill`、`consumer_skill`、`topics`），每个 topic 必须固定包含 4 个 view：`domain-model`、`json-schema`、`prompt-constraint`、`workflow-contract`，且它们的 `path` 都指向 `contracts/projections/ontology_extraction/` 下真实存在的 projection 文件；projection 文件含 `projection_type`、`source_slice`、`concept_mappings`、`delivery_artifacts`、`dropped_items`、`open_questions`；如信息不足，只写 draft/notes，不把 contract 标为 READY，也不阻断基础业务 skill 落盘。**额外验证**：读取每个 `*.projection.json` 文件，确认其 JSON 至少含 `projection_type`、`source_slice`、`concept_mappings` 三个顶层字段——若文件内容仅为 `{ "note": "...", "source_projection_path": "..." }` 等 stub 引用，则 Contract Check **不通过**，必须重新生成该 projection 文件的完整内容。
- Projection Consistency Check（强制）：反向检查 `SKILL.md` 与 `metadata.json`。
  - 若 `SKILL.md` 含 `## Projection Contracts` 章节，则 `contracts/projections/ontology_extraction/contract-index.json` 与该 topic 的 4 个标准 view 文件必须全部存在。
  - 若 `metadata.json` 记录了 projection source（例如 `sources[].type == "projection"` 或 `projection.source_projection_paths` 非空），但 contract 文件不存在，则 `SKILL.md` 必须移除 Projection Contracts 章节，并在 `references/quality-report.md` 明确记录“仅发现 source projection，未生成 consumer contract”的原因。
  - **绝不允许**出现“`SKILL.md`/`metadata.json` 声称已绑定 projection，但 `skills/<skill-slug>/contracts/` 目录为空”的半成品状态。
- Safety Check：确认产物不含明文 token、密钥、密码、连接串或凭据。
- Self-contained Check：复制整个 `skills/<skill_slug>/` 后仍能独立被 loader 发现和人工审阅。

**⚠️ 重要**：Contract Check 失败只影响 contracts/ 目录（删除或回退为 draft），不影响已通过 Base File Check 的基础技能文件。基础文件只要通过 Base File Check + Sanity Check + Safety Check 即可保留落盘。

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
6. **写入基础文件（强制）**：按模板渲染 `SKILL.md`、`metadata.json`、`references/` 三类文件并立即调用 write_file 写入磁盘。**每个文件写入后用 read_file 确认落盘。这一步不可跳过，不依赖投影是否可用。**
7. Projection 契约生成（可选）：仅在基础文件确认落盘后执行。有足够本体 projection 信息时，为产出的业务 skill 生成 READY consumer-skill projection 目录（contracts/）；信息不足时只记录 draft/notes 或跳过，不伪造 READY contract，不阻断已落盘的基础文件。
8. 质量校验：Base File Check 强制通过；Contract Check 仅当 contracts/ 存在时执行。
9. 返回摘要：输出 `technical_artifact`、`skill_results` 与 `user_summary`。

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

生成出来的业务 skill 可以按本仓库 consumer skill 方式接入 `ontology_extraction` producer namespace 下的 projection contract。projection contract 是条件增强：有足够 ontology projection 信息时生成 READY contract；信息不足时生成 draft/notes，不能伪造 READY contract，也不能因此阻断基础业务 skill 落盘。

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
      ontology_extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.domain-model.projection.json
          <domain-slug>.json-schema.projection.json
          <domain-slug>.prompt-constraint.projection.json
          <domain-slug>.workflow-contract.projection.json
```

生成的业务 `SKILL.md` 可以包含以下 consumer skill 章节，并按具体业务域裁剪 supported deliverables、projection types 和 local exclusions。仅当该技能确实带有 `contracts/projections/ontology_extraction/contract-index.json` 时写入本章节：

```markdown
## Projection Contracts

This skill may be augmented by bound `ontology_extraction` projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Projection discovery and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contracts/projections/ontology_extraction/contract-index.json` first, optionally read the namespace `README.md`, and then the chosen `*.projection.json` file.

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

- `producer_skill` 设为 `"ontology_extraction"`。
- `consumer_skill` 设为当前生成的 `{{name}}`。
- `default_selection_policy` 至少含 `prefer_ready_only` 和 `block_on_open_questions`。
- `topics` 至少包含 1 个条目，`domain_slug` 优先来自业务域 slug。
- 每个 topic 必须固定包含 4 个标准 view，且 `views[].path` 分别指向：
  - `<domain-slug>/<domain-slug>.domain-model.projection.json`
  - `<domain-slug>/<domain-slug>.json-schema.projection.json`
  - `<domain-slug>/<domain-slug>.prompt-constraint.projection.json`
  - `<domain-slug>/<domain-slug>.workflow-contract.projection.json`
- 每个 view 的 `status` 默认为 `"READY"`；有未决问题时设为 `"WARNING"`。
- 可选追加 `topic_scoring`、`target_view_scoring`、`selection_algorithm` 等完整选择路由逻辑。

生成 projection documents（`contracts/projections/ontology_extraction/<domain-slug>/` 下的 4 个标准 view 文件）时必须：

- 使用 consumer flat shape，顶层包含完整 projection 结构：`projection_type`、`source_slice`、`intended_consumers`、`concept_mappings`、`relation_mappings`、`constraint_mappings`、`mapping_policy`、`prompt_projection`、`delivery_artifacts`、`dropped_items`、`open_questions`。不要在 consumer contract 文件中只写 `{ "projection": ... }` 而遗漏这些顶层可消费字段。
- 对 `mapping_policy.unresolved_item_policy` 使用 `block_or_escalate`。
- 将 `delivery_artifacts.path` 限定到该业务 skill 真实会产出的文件或响应结构。
- 4 个 view 都要生成，但保持薄文件：`workflow-contract` 为主视图；`prompt-constraint`、`json-schema`、`domain-model` 只承载各自层级的最小职责。
- 如果用户输入中没有足够信息生成 READY projection，生成 WARNING/草稿摘要但不要伪造 READY contract；仅当本轮**未**要求绑定 projection 时，才允许不阻断基础业务 skill 落盘。若本轮要求绑定 projection，则必须阻断并要求回到 projection pass 或前置材料阶段补足信息。

## 伴随文件模板

生成业务 skill 时优先读取本技能目录下的伴随模板：

- `references/generated-skill-template.md`：生成 `SKILL.md` 的扩展业务模板。
- `references/projection-contract-template.md`：生成 consumer projection contract 的最小结构。
- `references/quality-checklist.md`：生成后质量检查与失败处理。

如果伴随模板缺失，可以使用本文件中的内联模板继续生成，但必须在 `user_summary` 中说明使用了内联降级模板。

## 质量校验

落盘前必须通过以下检查（按 Phase 3 Step 1 → Step 2 顺序执行）：

**基础文件检查（Step 1 产物，强制通过）**：
- 完整性：`name`、`description`、`capabilities` 必填。
- 可触发性：至少包含 1 个 trigger。
- 可执行性：每个 capability 都必须有输入、输出和兜底。
- 安全性：不得写入明文 token、密钥、密码、连接串或凭据。
- 自包含性：来源摘要、提炼说明、质量报告必须随生成 skill 一起落在 `skills/<skill_slug>/`。
- 落盘确认：5 个基础文件全部通过 read_file 确认存在且非空。

**投影契约检查（Step 2 产物）**：
- 可消费性：如生成 READY projection contract，`contracts/projections/ontology_extraction/contract-index.json` 必须存在，且每个 topic 固定包含 4 个标准 view，并全部指向 `contracts/projections/ontology_extraction/` 下的真实文件。
- 若本轮输入包含 `projection_binding_confirmed: true` 或 `projection_contract_mode: "required"`，则 `contracts/projections/ontology_extraction/contract-index.json` 与 4 个标准 view 文件为**必需产物**；缺任一项都必须判定本轮失败，不得让“只有基础 skill 文件”的结果通过。
- 仅当本轮**未**要求绑定 projection 时，`contracts/` 不存在才可视为非阻断状态。

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
- 用户已确认绑定 projection，但 producer projection 无法 materialize 为 consumer contracts：阻断本轮 skill-generation，返回缺失目录、无效 source、校验失败或写入失败的具体原因。
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
    "skills/<skill_slug>/contracts/projections/ontology_extraction/contract-index.json",
    "skills/<skill_slug>/contracts/projections/ontology_extraction/<domain-slug>/<domain-slug>.domain-model.projection.json",
    "skills/<skill_slug>/contracts/projections/ontology_extraction/<domain-slug>/<domain-slug>.json-schema.projection.json",
    "skills/<skill_slug>/contracts/projections/ontology_extraction/<domain-slug>/<domain-slug>.prompt-constraint.projection.json",
    "skills/<skill_slug>/contracts/projections/ontology_extraction/<domain-slug>/<domain-slug>.workflow-contract.projection.json"
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
- `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：consumer projection 目录命名规范（以本文件 Phase 3 路径 A 为准生成 contract 结构，layout guide 定义最终目录与命名规范）。
