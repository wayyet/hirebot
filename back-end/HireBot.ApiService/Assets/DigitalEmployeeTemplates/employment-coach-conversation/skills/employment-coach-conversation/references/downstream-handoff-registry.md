# 下游 skill 渐进式披露交接注册表

本文件是 `employment-coach-conversation` 唤起下游 skill 的唯一交接清单。主 skill 不得假设下游 skill 的完整规则已经在上下文里；每次进入下游步骤时，都必须用本表里的内部触发块显式唤起对应 skill，并等待指定 terminal artifact。

## 总规则

1. **先路由，再执行**：主 skill 只做阶段判断、用户确认、artifact 推送和 payload 组装；不得代替下游 skill 写入 `ontology/`、`skills/`、`testcases/`、`reports/`，也不得直接运行下游 skill 的脚本。
2. **内部触发块必须点名 skill**：触发文本必须包含 `use skill <skill-name>`，并带 `artifact_payload` JSON。不要只写“开始生成”“继续处理”这类自然语言。
3. **等待 terminal artifact**：交接后，主 skill 必须等 `expected_terminal_artifact` 到达后再推进下一阶段；没有 terminal artifact 时不得声称完成。
4. **可选步骤也要走门**：测试用例和完整性审查可跳过，但必须先出现对应确认门；跳过只跳过该可选下游执行，不跳过后续审查门或打包门。
5. **用户可见回复保持业务语言**：内部触发块不得原样展示给用户。对用户只说“正在分析业务资料 / 正在匹配技能数据 / 正在生成技能实现 / 正在审查内容完整性”。

---

# 阶段推进披露 (Stage Transition Disclosure)

阶段推进确认本身也是一次"技能披露"——当用户确认从阶段 N 推进到阶段 N+1，LLM 必须明确知道下一阶段要读取哪些 skill 和参考文件。以下 S1-S3 条目定义每次阶段推进时的**必须加载文件清单**、阶段摘要和入场动作。

> 阶段推进披露与 R1-R6 下游 skill 交接是两层路由：S 条目负责"进入下一阶段的上下文加载"，R 条目负责"在阶段内触发下游 skill 执行"。两者互补，不可互相替代。R1 属于资料阶段，S1 只能在 R1 的成功形态 `ontology_slice_extraction_done`（`data.status === "completed"` 且 `completed_slices > 0`）到达后执行；blocked 形态只结束 R1，不进入 S1。

## S1：资料阶段完成 → 阶段 2 推进披露

**前置信号**
- `skill_definition_entry_ready` 已发出，且用户对"确认推进到技能定义阶段吗？"给出肯定回应。
- `material_handoff_summary` 已发出，且 data 中有真实 `workspace_root`、`template_slug`、`items[]`。
- R1 `ontology-slice-extraction` 已被触发并收到成功形态 `ontology_slice_extraction_done`（`data.status === "completed"` 且 `completed_slices > 0`）。

**披露的技能与参考文件（LLM 必须在成功形态 `ontology_slice_extraction_done` 后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/SKILL.md` § 阶段 2（第 422-504 行） | 技能阶段主流程：目的、最低门槛、阶段完成条件、技能实现确认门、反停滞红线 | 必须 |
| `skills/employment-coach-conversation/references/flow-constraints.md` § 阶段 2 引导细则 | 引导话术、story-driven 推进、字段明确度对照、决策启发式 | 必须 |
| `skills/employment-coach-conversation/references/stage-data-schema.md` | `skill_workorder_progress` / `skill_workorder_summary` 的 data payload 结构 | 必须 |
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R2, R3 | projection pass 与 skill-generation 触发规则 | 按需（对应确认门通过后） |
| `skills/employment-coach-conversation/references/emit-artifact-protocol.md` | artifact 推送字段协议 | 参考 |

**阶段摘要**
- **目的**：把岗位动作和能力清单整理成结构化 skill 定义清单，每条都有明确的名称、触发条件和期望输出
- **子步骤顺序**：skill_definition_entry_ready 确认门 → 技能定义收集 → skill_definition_ready 确认门 → skill_workorder_summary → 进入技能实现子流程 → ontology_projection_ready 确认门 → projection pass (R2，产出一系列 `ontology/projections/<skill-slug>/*.projection.json`) → skill_generation_ready 确认门 → skill-generation (R3，消费 `projection_result` 将投影物化为 `skills/<skill-slug>/contracts/projections/ontology_extraction/` 下的 4 视图 consumer contract) → skill_generation_done
- **入场动作**：读取 S1 文件清单 → 发出 `skill_workorder_progress` → 开始引导技能定义
- **最低门槛**：每个 skill 具备明确的 `name`（skill slug）+ `display_name` + `description` + `trigger` + `expected_output` + `generation_action`
- **禁止**：成功形态 `ontology_slice_extraction_done` 到达前不得发 `skill_workorder_progress` 或进入技能定义收集；blocked 形态不得解锁 S1；skill-generation 完成前不得提示"可进入外部阶段"

**关联下游 R 条目**：R2（projection pass → 产出 projection 文件，供 skill-generation 消费）、R3（skill-generation → 消费 `projection_result` + projection 文件，物化为 consumer contract）

---

## S2：阶段 2 → 阶段 3 推进披露

**前置信号**
- `skill_generation_done` 已到达
- `external_system_entry_ready` 已发出
- 用户对"继续进入外部配置"给出肯定回应（进入正常流程），或对"跳过外部，直接打包"给出肯定回应（进入系统层跳过流程）

**披露的技能与参考文件（LLM 必须在确认推进后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/SKILL.md` § 阶段 3（第 506-538 行） | 外部阶段主流程：目的、最低门槛、凭据红线、阶段完成条件、阶段门动作 | 必须 |
| `skills/employment-coach-conversation/references/flow-constraints.md` § 阶段 3 引导细则 | 引导话术、紧扣已有 skills 的套路、跳过分支 | 必须 |
| `skills/employment-coach-conversation/references/stage-data-schema.md` | `external_workorder_progress` / `external_workorder_summary` 的 data payload 结构（含 skip 形态） | 必须 |
| `skills/external-config/SKILL.md` | 外部配置写入规则、安全与校验 | 按需（系统层保存时触发） |
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R4 | 测试用例生成触发规则 | 按需（外部阶段完成后） |

**阶段摘要**
- **目的**：把支撑技能所需的外部能力和系统资源整理成有分类、有目标的外部能力清单
- **入场动作（正常）**：用户确认 `external_system_entry_ready` 并选择进入外部配置后，发出 `external_workorder_progress` → 紧扣已确认 skills 逐条引导外部能力定义
- **入场动作（跳过）**：用户选择跳过后，由系统层确定性写入 `external_workorder_summary`（isTerminal: true，stage: stage3_external，data 中 `skip: true`、`total_capabilities: 0`、`external_capabilities: []`）和 `external_config_committed`（`submissionMode: skipped`）。Coach 不得自由生成跳过形态，也不得在 `external_config_committed` 前直接跳到打包询问
- **最低门槛**：每个外部能力明确 `分类（read/write/notify/search/transform）+ 目标 + 目标系统 + 鉴权方式 + 关联 skill`；或用户明确表达"不需要外部系统"
- **凭据红线**：token/密钥/密码/API Key 绝不在会话里收集，指引用户填写右侧表单
- **禁止**：skill-generation 未完成或 `external_system_entry_ready` 未确认进入外部系统时，不得发出 `external_workorder_progress`

**关联下游 R 条目**：R4（测试用例，外部阶段完成后可选触发）

---

## S3：阶段 3 → 阶段 4 推进披露

**前置信号**
- 外部配置已保存（`external_config_committed`）或明确跳过
- 用户对"生成数字员工"或等价打包意图给出肯定回应

**披露的技能与参考文件（LLM 必须在确认推进后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R4, R5, R6 | 测试用例（可选）→ 完整性审查（可选）→ 打包执行 | 必须 |
| `skills/packaging-test-cases/SKILL.md` | 评估测试用例生成规则 | 按需（用户选择生成测试用例时） |
| `skills/digital-employee-package-completeness-review/SKILL.md` | 完整性审查规则 | 按需（用户选择审查时） |

**阶段摘要**
- **目的**：经过测试用例确认门 → 完整性审查门 → 执行打包，生成最终数字员工包
- **子步骤顺序**：`packaging_testcases_ready` 确认门 → (可选) R4 测试用例 → manifest 同步 → `review_readiness` 确认门 → (可选) R5 完整性审查 → R6 打包
- **入场动作**：发出 `packaging_testcases_ready` → 询问是否生成评估测试用例
- **禁止**：未取得真实 `fileUrl` 前不得声称数字员工包已生成；不得跳过审查门直接打包

**关联下游 R 条目**：R4（测试用例）、R5（完整性审查）、R6（打包执行）

---

## R1：资料收口后系统层触发本体切片抽取

**前置信号**
- 刚发出 `material_handoff_summary`，且 data 中有真实 `workspace_root`、`template_slug`、`items[]`。
- `ontology-slice-extraction` 已通过 `load_skill` 加载到上下文；若上下文曾被裁剪，先重新加载。
- R1 由系统层根据 terminal artifact 自动构造内部触发块；`employment-coach-conversation` 只发出 `material_handoff_summary` 和一句进度提示，不手写内部触发块。

**必须唤起**
- `ontology-slice-extraction`

**内部触发块**

````text
[Internal downstream trigger: use skill ontology-slice-extraction]
source_skill: employment-coach-conversation
trigger_reason: material_handoff_summary_completed
artifact_payload:
```json
<material_handoff_summary.data>
```
required_artifacts:
- ontology_slice_extraction_progress
- ontology_slice_extraction_done
return_to: employment-coach-conversation
````

**等待结果**
- 成功形态：`ontology_slice_extraction_done.data.status === "completed"` 且 `completed_slices > 0`。此时系统层进入 S1 阶段推进披露。
- 阻断形态：`ontology_slice_extraction_done.data.status === "blocked"` 或 `completed_slices === 0`。此时系统层不得进入 S1，不得发出 `skill_definition_entry_ready`；应停留在资料阶段，并把 `diagnostic` / `diagnostic_detail` 转成用户可理解的资料补充建议。
- 等待期间仍处于资料阶段，不得发 `skill_workorder_progress` 或进入技能定义收集。

**禁止**
- 不得在 `ontology_slice_extraction_done` 前发 `skill_workorder_progress`。
- 不得由主 skill 自己输出或写入本体切片文件。
- 不得由主 skill 手写或复述 R1 内部触发块，避免与系统层自动调度重复。

## R2：匹配技能数据确认后触发投影 (Projection Pass)

**前置信号**
- 已发出 `skill_workorder_summary`。
- 已发出 `ontology_projection_ready`。
- 用户对“是否开始匹配技能数据”给出肯定。

**必须唤起**
- `ontology-projection`

**内部触发块**

````text
[Internal downstream trigger: use skill ontology-projection]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_ontology_projection
artifact_payload:
```json
{
  “workspace_root”: “<skill_workorder_summary.data.workspace_root>”,
  “template_slug”: “<skill_workorder_summary.data.template_slug>”,
  “skills”: <skill_workorder_summary.data.items>,
  “business_rules”: <skill_workorder_summary.data.business_rules_captured_so_far>
}
```
required_artifacts:
- ontology_projection_progress
- ontology_projection_done
return_to: employment-coach-conversation
````

> `ontology-projection` 自行扫描 `<workspace_root>/ontology/` 下的 `*.slice.json`，无需上游传入 `material_summary` 或 `ontology_result`。`business_rules` 来自技能定义阶段已收集的业务规则，`ontology-projection` 必须先消费已有规则再判断是否需要提问。

**等待结果**
- `ontology_projection_done`
- 当 `ontology_projection_done` 可消费时，`skill_generation_ready` 由系统层确定性追加；coach 不得重复 emit 该确认门，不得用普通文本作为确认门状态来源。

**禁止**
- `ontology-projection` 产出 projection 文件时必须调用沙箱文件写入工具（优先 `write_file`，否则使用当前环境等价的 `create_file` / `save_file`），并用 `read_file` 读回验证；不得用 shell / Python here-doc / echo / 仅对话描述代替真实文件写入。
- 若文件写入工具不可用，或读回验证失败，不得发出成功形态的 `ontology_projection_done`；应将对应技能降级为 `slices_not_ready` / 跳过并说明原因。
- 若 projection 已真实落盘但包含 `open_questions`，这是生成前确认项，不是“匹配技能数据失败”，也不是“业务信息不足 / 还不够直接落地”；应保留已匹配结果，向用户提出对应的精确业务问题，不得要求用户重跑同一步或回到业务信息整理。
- 当 `projection_paths[]` 可消费且 slug 校验通过时，面向用户只能表达为“技能数据已匹配完成，确认以下业务口径后即可生成技能实现”；不得再提供“补资料 / 重跑业务信息准备 / 继续”三选一路线。
- 不得在 `ontology_projection_done` 前调用 `skill-generation`。
- 不得把 `projection_binding_confirmed` 写进 `ontology_projection_ready` 或 `skill_generation_ready`；该字段只属于 R3 的内部触发 payload。
- 不得在用户确认 `ontology_projection_ready` 前触发 R2。
- 面向用户只说“匹配技能数据”，不得暴露 projection pass、R2、slice、结构化文件等内部术语。

---

# ontology-slice-extraction / ontology-projection ↔ skill-generation 跨 skill 关系与执行顺序

本节统一描述 `ontology-slice-extraction`、`ontology-projection` 与 `skill-generation` 三个下游 skill 之间的依赖关系、数据契约和执行顺序。R1/R2/R3 的单个触发规则见下方对应 R 条目，本节为**交叉索引**——从 producer-consumer 视角展示完整的数据管道。

## 关系概述

- **ontology-slice-extraction 是 slice producer**：从业务资料中抽取本体切片（R1）。所有产物写入 `<workspace_root>/ontology/` 目录。
- **ontology-projection 是 projection producer**：读取本体切片，按已确认技能清单投影为每项技能专属的 projection 文件（R2）。产物写入 `<workspace_root>/ontology/projections/`。
- **skill-generation 是 consumer**：读取 projection 文件生成完整技能包。Phase 3 Step 1 写入基础技能文件（`SKILL.md`、`metadata.json`、`references/`），Phase 3 Step 2 将 projection 文件物化（materialize）为 consumer 侧标准 4 视图 contract，写入 `<workspace_root>/skills/<slug>/contracts/projections/ontology_extraction/`。
- **执行严格顺序**：R1（slice）→ 技能定义收集 → R2（projection pass）→ R3（skill-generation）。R2 必须先完成（emit `ontology_projection_done`）且 `projected_count > 0`，R3 才能触发。
- **数据交接介质**：`ontology/projections/<skill-slug>/` 目录下的 `*.projection.json` 文件是 projection 与 skill-generation 之间的核心数据契约载体。

## 执行顺序总览

| 步骤 | 动作 | 负责 Skill | 触发规则 | Terminal Artifact | 产出数据 | 消费数据 |
|------|------|-----------|---------|-------------------|---------|---------|
| 1 | 本体切片抽取 | `ontology-slice-extraction` | R1 | `ontology_slice_extraction_done` | `ontology/<topic>.slice.json` + `.slice.md` | `material_handoff_summary.data`（workspace_root、items[]） |
| 2 | 技能定义收集 | `employment-coach-conversation` | — | `skill_workorder_summary` | `items[]` 技能清单（含 name、description、trigger、expected_output、business_rules_captured_so_far） | 用户对话确认 |
| 3 | 投影匹配 | `ontology-projection` | R2 | `ontology_projection_done` | `ontology/projections/<skill-slug>/<domain>.<type>.projection.json` | `skill_workorder_summary.data.items[]` + `business_rules_captured_so_far` + `ontology/*.slice.json`（自行扫描） |
| 4 | 技能生成 | `skill-generation` | R3 | `skill_generation_done` | `skills/<slug>/SKILL.md` + 基础文件 + consumer contracts（4 视图） | R3 payload（含 `projection_result`、`items`、`projection_binding_confirmed: true`、`projection_contract_mode: “required”`） |

## 数据契约字段映射（ontology-projection 产出 → skill-generation 消费）

| # | 源字段 / 文件（ontology-projection 产出） | 中间位置 | 目标字段 / 逻辑（skill-generation 消费） | 消费阶段 | 校验规则 |
|---|------------------------------------------|---------|----------------------------------------|---------|---------|
| 1 | `ontology_projection_done.data.projection_paths[]` | R3 payload 的 `projection_result.projection_paths[]` | Phase 1 投影发现（扫描 `<workspace_root>/ontology/projections/<slug>/`） | Phase 1（输入采集） | 必须非空；`projected_count > 0` 才可触发 R3 |
| 2 | `projection_paths[]` 中解析出的 `<skill-slug>` 部分 | R3 payload 的 `projection_skill_slugs[]` | Phase 0.25 slug 白名单校验 | Phase 0.25（slug 校验） | 必须全部在 `confirmed_skill_slugs` 中；若不匹配则阻断 |
| 3 | `ontology/projections/<slug>/*.projection.json`（文件系统实体） | 文件系统（`<workspace_root>` 下） | Phase 3 Step 2 consumer contract 物化（优先运行 `materialize-consumer-projection-contract.py`） | Phase 3 Step 2（投影契约生成） | 文件必须存在且 JSON 顶层包含 `projection_type`、`source_slice`、`concept_mappings` 三个字段；仅含 `note`/`source_projection_path` 的 stub 引用视为无效 |
| 4 | `projection.json` 的 `open_questions` 字段 | 透传 | Phase 3 Step 2 判定 view 状态：为空 → READY；非空 → WARNING（透传未决问题） | Phase 3 Step 2 | WARNING 不跳过 contract 生成，但状态标记为 WARNING |
| 5 | `projection.json` 的 `intended_consumers` 字段 | 透传 | Phase 0.25 校验 projection 归属：确认该 projection 声明为当前 skill 消费 | Phase 0.25 | 必须包含当前 `skill_slug`；不匹配视为该 projection 不属于此 skill |
| 6 | `projection.json` 的 `concept_mappings` / `relation_mappings` / `constraint_mappings` | 透传 | Phase 3 Step 2 物化为 4 个标准 view：`domain-model`、`json-schema`、`prompt-constraint`、`workflow-contract` | Phase 3 Step 2 | 4 个 view 都必须以 consumer flat shape 落盘；薄文件、分职责 |
| 7 | `projection.json` 的 `mapping_policy` / `delivery_artifacts` / `dropped_items` / `prompt_projection` | 透传 | Phase 3 Step 2 写入对应 view 文件，供 runtime 发现和注入 | Phase 3 Step 2 | `delivery_artifacts.path` 限定到该业务 skill 真实会产出的文件或响应结构 |

## 跨 skill 交接校验规则

以下规则在构造 R3 内部触发块之前和 skill-generation 入口门禁处分别执行：

### employment-coach 侧（构造 R3 payload 前）

- `projection_result` 必须来自最近一次 `ontology_projection_done.data`，不得编造或从旧 artifact 拼接
- `projection_result.projection_paths[]` 非空且 `projected_count > 0`
- `projection_skill_slugs[]`（从 `projection_paths[]` 解析）必须全部为 `confirmed_skill_slugs` 子集
- `projection_binding_confirmed` 硬编码为 `true`
- `projection_contract_mode` 硬编码为 `”required”`

### skill-generation 侧（Phase 0.25 门禁 + Phase 3 Step 2 阻断）

- Phase 0.25：`projection_skill_slugs[]` 全部在 `confirmed_skill_slugs` 中，否则阻断本轮运行
- Phase 1 投影发现：`ontology/projections/<slug>/` 目录必须存在且含 `*.projection.json`；当 `projection_binding_confirmed: true` 且该目录不存在时，记为阻断原因
- Phase 3 Step 2：每个 projection 文件必须是非 stub 的完整 JSON；若源文件无效，当 `projection_contract_mode: “required”` 时必须阻断，不得降级为”仅基础 skill 文件”
- Phase 4 Contract Check：`contracts/projections/ontology_extraction/contract-index.json` 存在且结构完整，每个 topic 固定包含 4 个标准 view 文件

### 反 stub 验证（关键）

Projection 源文件和 consumer contract 文件都**不得**为以下 stub 形态：

```json
{ “note”: “...”, “source_projection_path”: “...” }
```

合法文件必须包含完整 projection 结构：`projection_type`、`source_slice`、`intended_consumers`、`concept_mappings`、`relation_mappings`、`constraint_mappings`、`mapping_policy`、`prompt_projection`、`delivery_artifacts`、`dropped_items`、`open_questions`。若源 projection 文件仅为 stub 引用，等同于”projection 源无效”，在 `projection_binding_confirmed: true` 时阻断 R3。

## 关联参考文档

| 文档 | 位置 | 用途 |
|------|------|------|
| Projection 消费指南 | `ontology-projection/references/PROJECTION_CONSUMPTION_GUIDE.md` | 下游 skill（如 skill-generation）如何读取和使用 projection 文件 |
| Consumer Projection 目录与命名规范 | `ontology-projection/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md` | consumer contract 的 4 视图结构、目录布局与命名约定 |
| Skill 生成质量检查清单 | `skill-generation/references/quality-checklist.md` | 生成后质量校验，含 Contract Check 与 Projection Consistency Check 规则 |
| Projection Contract 模板 | `skill-generation/references/projection-contract-template.md` | 生成 consumer projection contract 的最小结构模板 |

---

## R3：技能生成确认后触发技能实现生成

**前置信号**
- 已收到 `ontology_projection_done`。
- `ontology_projection_done.data.projected_count > 0`。
- `ontology_projection_done.data.projection_paths[]` 非空，且路径能对应 `skill_workorder_summary.data.items[].name`。
- 若 `ontology_projection_done.data` 缺少可消费的 `projection_paths[]`，但最近一次 R2 日志或文件写入记录显示 projection 文件已落盘，不得直接要求用户补资料；必须先在当前 `workspace_root` 下按已确认 skill slug 做一次受限恢复：只检查 `<workspace_root>/ontology/projections/<skill-slug>/` 中的 `*.projection.json`，读回并校验顶层 `projection_type`、`source_slice`、`concept_mappings` 后，重新构造带 `projected_count` 与 `projection_paths[]` 的聚合结果。恢复失败后才回到补资料或重跑业务信息准备。
- 已发出 `skill_generation_ready`。
- 用户对“是否开始生成技能实现”给出肯定。

**必须唤起**
- `skill-generation`

**内部触发块**

````text
[Internal downstream trigger: use skill skill-generation]
source_skill: employment-coach-conversation
trigger_reason: projection_done_generate_skills
artifact_payload:
```json
{
  "workspace_root": "<skill_workorder_summary.data.workspace_root>",
  "template_slug": "<skill_workorder_summary.data.template_slug>",
  "items": <skill_workorder_summary.data.items>,
  "confirmed_skill_slugs": ["<items[].name>"],
  "projection_binding_confirmed": true,
  "projection_contract_mode": "required",
  "projection_result": <ontology_projection_done.data>,
  "projection_skill_slugs": ["<parsed from projection_paths>"]
}
```
required_artifacts:
- skill_generation_progress
- skill_generation_done
return_to: employment-coach-conversation
````

**等待结果**
- `skill_generation_done`

### R3 字段来源验证清单（ontology-projection → skill-generation 交接校验）

构造 R3 内部触发 payload 前，**必须**逐项确认以下 ontology-projection 产出已就绪。本清单与上文"跨 skill 交接校验规则"互补：上文描述校验逻辑，本清单提供 employment-coach 侧构造 R3 payload 时的逐项核对表。

| # | 校验项 | 来源 | 阻断级别 | 说明 |
|---|--------|------|---------|------|
| 1 | `projection_result.projection_paths[]` 非空 | `ontology_projection_done.data`，必要时从当前 `workspace_root` 的已确认 skill projection 目录受限恢复 | **阻断**（不可触发 R3） | 若为空或 `projected_count === 0`，先尝试受限恢复已落盘 projection；恢复失败才停留在阶段 2，提示用户补充业务资料 |
| 2 | `projection_skill_slugs[]` 全部在 `confirmed_skill_slugs` 中 | 从 `projection_paths[]` 解析 `<skill-slug>` 部分 | **阻断**（projection 目录与已确认技能不一致） | 若存在不匹配的 slug，不得自行创建新目录或改名，必须报告不一致 |
| 3 | 每个 `projection_paths[]` 指向的 `.projection.json` 文件存在且可读 | 文件系统（`read_file` 验证） | **阻断**（projection 文件未落盘） | 若文件不存在，先执行有界等待（500ms 间隔，最长 5s）；超时仍未就绪则阻断 |
| 4 | 每个 projection 文件的 JSON 顶层包含 `projection_type`、`source_slice`、`concept_mappings`（非 stub） | 文件内容（`read_file` + JSON 解析） | **阻断**（无效 projection 源） | 若文件仅含 `note`/`source_projection_path` 等 stub 字段，视为源文件无效；`projection_contract_mode: "required"` 时必须阻断 |
| 5 | `projection_binding_confirmed` 显式为 `true`（布尔值，非字符串） | R3 payload 构造 | **阻断**（skill-generation 入口门禁条件 #4） | 该字段只在 R3 内部 payload 中出现，禁止写入 `skill_generation_ready` 或 `ontology_projection_ready` |
| 6 | `projection_contract_mode` 显式为 `"required"`（字符串） | R3 payload 构造 | **阻断**（skill-generation 不执行投影绑定，Phase 3 Step 2 可能被跳过） | 该字段只在 R3 内部 payload 中出现，禁止写入 `skill_generation_ready` 或 `ontology_projection_ready` |

> **与 SKILL.md 中 R3 字段来源检查清单的关系**：SKILL.md 中的检查清单（workspace_root、template_slug、items、confirmed_skill_slugs、projection_binding_confirmed、projection_contract_mode、projection_result、projection_skill_slugs）覆盖 R3 payload 的 8 个必要字段。本清单覆盖的是 projection 本身的质量和可消费性——两个清单互补，构造 R3 前必须同时通过。

**失败分支**
- 如果 projection 结果不可消费，停留在阶段 2，提示用户补业务资料或调整技能定义；不得降级直连 `skill-generation`。

## R4：外部配置完成后触发可选测试用例生成

**前置信号**
- 已收到 `external_config_committed`，或系统层确认外部配置已跳过。
- 已发出 `packaging_testcases_ready`。
- 用户明确选择生成评估测试用例。

**必须唤起**
- `packaging-test-cases`

**内部触发块**

````text
[Internal downstream trigger: use skill packaging-test-cases]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_packaging_testcases
artifact_payload:
```json
{
  "trigger_after": "external_config_committed",
  "workspace_root": "<workspace_root>",
  "template_slug": "<template_slug>",
  "latest_material_summary": <latest material_handoff_summary.data>,
  "latest_skill_summary": <latest skill_workorder_summary.data>,
  "latest_external_summary": <latest external_workorder_summary.data>,
  "external_config": <latest external_config_committed.data>
}
```
required_artifacts:
- packaging_testcases_progress
- packaging_testcases_done
return_to: employment-coach-conversation
````

**跳过分支**
- 用户选择跳过时，不调用 `packaging-test-cases`，直接进入 R5 审查门准备；不得把测试用例缺失列为打包等待项。

## R5：打包前触发可选完整性审查

**前置信号**
- 已完成打包预检和 manifest 同步。
- 已发出 `review_readiness`。
- 用户明确选择审查。

**必须唤起**
- `digital-employee-package-completeness-review`

**内部触发块**

````text
[Internal downstream trigger: use skill digital-employee-package-completeness-review]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_package_review
artifact_payload:
```json
{
  "package_root": "<workspace_root>",
  "workspace_root": "<workspace_root>",
  "template_slug": "<template_slug>",
  "report_path": "reports/package-completeness-review.md"
}
```
required_artifacts:
- review_progress
- review_report
return_to: employment-coach-conversation
````

**等待结果**
- `review_report`
- 收到并发出 `review_report` 后必须停止本轮，不得再用普通 assistant 文本追问用户是否修复、重跑审查或继续打包；后续用户显式输入由前端基于 `review_report` artifact 路由。

**禁止**
- 主 skill 不得直接运行 `validate_digital_employee_package.py`。
- 主 skill 不得根据 validator 输出直接修改 manifest 或技能文件；若用户选择修复，回到对应阶段或确定性同步步骤后再重新审查。

## R6：审查完成或跳过后执行打包

**前置信号**
- 用户已跳过审查，或已收到 `review_report` 且用户选择继续。

**执行**
- 发 `packaging_progress`，`data.status = "packing"`。
- 调用可用的打包/导出工具；没有专用工具时，在真实 `workspace_root` 内按白名单生成 zip。
- 验证 zip 根层级后，发 `template_package`，`kind = "file"`，`fileUrl` 使用真实返回值。

**禁止**
- 未取得真实 `fileUrl` 前，不得声称数字员工包已生成。

---

# 关联文档索引

以下文档不在本注册表中，但与本注册表的 R1-R3（ontology-slice-extraction / ontology-projection ↔ skill-generation）交接链路直接相关。除本表外，这些文档是构造 R1/R2/R3 内部触发块和理解 producer-consumer 数据契约的必要补充阅读。

| 文档 | 相对于 skill 根目录的路径 | 用途 |
|------|--------------------------|------|
| Projection 消费指南 | `ontology-projection/references/PROJECTION_CONSUMPTION_GUIDE.md` | 下游 skill（如 skill-generation、evaluation-expert-consumer）如何读取和使用 projection 文件 |
| Consumer Projection 目录与命名规范 | `ontology-projection/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md` | consumer contract 的标准 4 视图结构、目录布局与文件命名约定 |
| Skill 生成质量检查清单 | `skill-generation/references/quality-checklist.md` | 生成后质量校验，含 Contract Check 与 Projection Consistency Check 规则 |
| 下游映射指南 | `ontology-projection/references/DOWNSTREAM_MAPPING_GUIDE.md` | 投影如何映射到下游 codegen / prompt 编排 |
| Projection Contract 模板 | `skill-generation/references/projection-contract-template.md` | 生成 consumer projection contract 的最小结构模板 |
