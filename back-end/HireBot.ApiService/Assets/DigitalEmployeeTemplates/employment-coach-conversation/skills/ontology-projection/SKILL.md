---
name: ontology-projection
description: Internal HireBot downstream skill for mapping ontology slices to skill-specific projections. Use only when the current message is an internal downstream trigger from employment-coach-conversation with a valid artifact_payload containing workspace_root, skills array, and trigger_reason for projection pass. Do not use directly for user-facing requests that mention projection, mapping, or business data preparation; route those requests through employment-coach-conversation so stage gates and artifacts stay consistent.
compatibility: HireBot employment-coach-conversation v1.0
metadata:
  openclaw:
    emoji: "🗺️"
  category: generation
  autonomy: 90
  trigger: hiring-session-projection, skill-stage-projection
  input: ontology-slices, skill-workorder, business-rules
  output: projection-files, emit-artifact
---

# ontology-projection

Task-scoped projection mapping: takes existing ontology slices and matches them to confirmed skill definitions, producing per-skill projection files that serve as the data contract for skill generation.

## 入口门禁

本 skill 是雇佣流程内部下游执行器，不是用户可直接点名调用的对话入口。

仅在收到以下内部 payload 时继续执行：

- `employment-coach-conversation` 注入的 internal downstream trigger，且 `artifact_payload` 包含 `workspace_root`、`skills` 数组（每项含 `skill_slug`、`skill_name`、`triggers`、`description`）。

如果当前消息只是用户在聊天里提到"投影 / projection / mapping / 匹配技能数据"等词，或缺少上述 `artifact_payload`，不得发出 `ontology_projection_progress` / `ontology_projection_done`，不得写入 `ontology/projections/`。只回复一句：「匹配技能数据需要先完成技能定义收口，我先回到技能定义阶段。」

## Core Concept

不要为每个 skill 做全量本体导出，而是围绕每个 skill 的能力域构造最小投影闭包：

- `concept_mappings`：该 skill 真正依赖的核心概念及其在 slice 中的来源
- `relation_mappings`：概念之间必须保留的关系
- `constraint_mappings`：会影响该 skill 判断、实现或生成结果的规则边界
- `dropped_items`：slice 中存在但与该 skill 无关的项（含剔除原因）
- `open_questions`：无法从现有 slice 和 business_rules 中解决的未决问题

目标不是泛泛介绍 ontology，而是交付一个能被 `skill-generation` 直接消费的、per-skill 的结构化数据契约。

## When to Use

| Trigger | Action |
| --- | --- |
| `skill_workorder_summary` 已确认，用户已确认匹配技能数据 | 为每个业务 skill 匹配已有 slice 并产出专属 projection |
| 需要将通用本体切片约束到特定 skill 的语义空间 | 执行 projection pass，产出 per-skill projection 文件 |
| `skill-generation` 需要稳定、可校验的数据契约 | 产出结构化 projection JSON，作为 skill 生成的稳定输入 |

> **注意**：本 skill 只负责投影映射。从资料中抽取本体切片由独立的 `ontology-slice-extraction` skill 负责。

## 业务规则消费规则（最高优先级）

本 skill 接收的 `artifact_payload` 中**必须**包含 `business_rules` 字段（来自 `skill_workorder_summary` 阶段收集的业务规则）。

### 消费规则

1. **先消费，后提问**：处理每个 skill 的投影前，先遍历 `business_rules` 中已有值的规则，直接映射到该 skill 的 `constraint_mappings`。
2. **只对缺口精确提问**：仅当某条关键约束在 slice 和 `business_rules` 中都找不到定义时，才以选项题形式向用户精确提问。每条提问必须给出 2-5 个可选答案。
3. **禁止笼统追问**：不得输出"要不要补齐业务口径？""还需要补充什么规则吗？"等开放式追问。
4. **缺口提问格式**：必须指明"哪个 skill 缺哪条规则"，并给出具体选项。例如：
   > 「插单可行性评估」需要知道 CIP 清洗换线规则，你们当前的清洗路径是：
   > A 香型→颜色→设备 三级清洗，B 仅按香型清洗，C 按设备专用无需清洗，D 不确定/需要补充

### business_rules 与 constraint_mappings 的对应关系

| business_rules 中的 key | 映射到的 constraint 类型 | 示例值 |
|------------------------|------------------------|--------|
| `due_date_policy` | 交期约束 | `no_due_date_shift_only_feasible_quantity` |
| `fallback_policy_when_infeasible` | 不可行兜底策略 | `split_delivery_partial_on_time_partial_delayed` |
| `split_preference` | 拆单偏好 | `by_quantity_ratio` |
| `priority_rules` | 优先级规则 | `live_streaming_first` |
| `cip_matrix` | CIP 清洗矩阵 | `fragrance→color→equipment` |
| `material_availability_check` | 齐套校验口径 | `all_components_ready_before_start` |

## Projection Pass emit_artifact 规范

本 skill 执行期间须在两个关键节点调用 `emit_artifact`，推动前端投影阶段更新。

### 投影进度节点（isTerminal: false）

在开始处理第一个 skill 的 projection 之前调用：

```json
{
  "kind": "data",
  "artifactType": "ontology_projection_progress",
  "label": "正在为 {N} 个技能匹配数据...",
  "skillName": "ontology-projection",
  "stage": "stage2_skill",
  "isTerminal": false,
  "displayHint": "progress",
  "data": {
    "total_skills": 3,
    "completed_projections": 0
  }
}
```

### 投影完成节点（isTerminal: true）

所有 skill 处理完毕后调用：

```json
{
  "kind": "data",
  "artifactType": "ontology_projection_done",
  "label": "技能数据已匹配完成，{M}/{N} 个技能可开始生成",
  "skillName": "ontology-projection",
  "stage": "stage2_skill",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_skills": 3,
    "projected_count": 2,
    "skipped_count": 1,
    "projection_paths": [
      "ontology/projections/return-eligibility-check/return-policy.workflow-contract.projection.json",
      "ontology/projections/order-status-query/order-workflow.workflow-contract.projection.json"
    ],
    "skipped_skills": ["appointment-booking"],
    "skip_reasons": {
      "appointment-booking": "no_matching_slice"
    }
  }
}
```

### 零投影场景（projected_count === 0 时的 done 示例）

当 `ontology/` 下找不到任何合法 slice 文件时，仍必须发出 done，附带 `diagnostic` 字段：

```json
{
  "kind": "data",
  "artifactType": "ontology_projection_done",
  "label": "技能数据已匹配完成，但当前资料暂不足以支撑技能生成",
  "skillName": "ontology-projection",
  "stage": "stage2_skill",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_skills": 3,
    "projected_count": 0,
    "skipped_count": 3,
    "projection_paths": [],
    "skipped_skills": ["return-eligibility-check", "order-status-query", "appointment-booking"],
    "skip_reasons": {
      "return-eligibility-check": "no_available_slices",
      "order-status-query": "no_available_slices",
      "appointment-booking": "no_available_slices"
    },
    "diagnostic": "slices_not_ready",
    "diagnostic_detail": "ontology/ 目录下未找到 *.slice.json 或含 slice_request 字段的 JSON 文件；请确认 ontology-slice-extraction 阶段已将切片写入文件系统"
  }
}
```

### 落盘验证（发出 ontology_projection_done 的前置条件）

**在发出 `ontology_projection_done` 之前，必须按下列规则确认产物状态**：

1. 当 `projected_count > 0` 时：`projection_paths` 中列出的每个路径对应的文件，必须确实存在于文件系统（通过 `read_file` 或等价的文件存在性检查工具确认），并且 JSON 顶层包含 `projection_type`、`source_slice`、`concept_mappings` 字段；不得仅在对话里描述内容就视为已写入。
2. **就绪等待**：若发现某路径尚未就绪（文件不存在 / 内容为空 / JSON 截断或正在写入），按 500ms 间隔轮询重读，**最长等待 5 秒**；不要立即放弃，也不要无限阻塞。
3. **超时降级**：等待超时后仍未就绪的路径，从 `projection_paths` 中剔除，对应 skill 移入 `skipped_skills`，`skip_reasons` 标记为 `"slices_not_ready"`，重新计算 `projected_count` / `skipped_count` 后再发出 done。
4. **零投影必须给原因**：当最终 `projected_count === 0` 时，`data.diagnostic` 字段必填，且只能取下列枚举值之一。

#### `diagnostic` 字段枚举（projected_count === 0 时必填）

| 枚举值 | 触发场景 |
| --- | --- |
| `"no_matching_slice"` | `ontology/` 下已扫描到合法 slice，但没有任何 skill 能与现有 slice 在 concepts / relations / constraints 维度建立匹配（真零投影） |
| `"slices_not_ready"` | 扫描时 `ontology/` 下的 slice 文件尚未就绪：目录不存在、目录为空、文件正在写入或经 5 秒就绪等待后仍不可读 |
| `"scan_error"` | 扫描过程发生异常：目录权限不足、读取 IO 错误、JSON 解析失败等导致无法完成 slice 发现 |

如需附加排查线索，可在 `data` 中追加自由文本字段 `diagnostic_detail`（仅作人工阅读用），但 `diagnostic` 本身必须是上述三个枚举值之一，不得写整段描述。

**⛔ 严禁**：当 `projected_count > 0` 时不做任何文件存在性确认就直接发出 done — 这会让前端阶段在文件尚未真正落盘的情况下推进，导致 skill-generation 读取 projection 失败、产物包出现空 contract。

### Projection Pass emit 约束

- **先调用后输出**：同一轮次识别到可推送的阶段事件时，先调用 `emit_artifact`，再继续文件生成或对话输出
- **data 禁止凭据**：data 字段中不得写入 token / 密钥 / 密码 / API Key
- **无论 projected_count 是否为 0 都必须发 done**：即使全部 skill 跳过，也必须发出 `ontology_projection_done`，使 employment-coach 能继续进入技能生成确认门或提示补充资料
- **projected_count > 0 时必须先通过落盘验证再发 done**：按上方"落盘验证"小节逐路径确认 `projection_paths` 中文件确实落盘，未通过验证前不得发出 done；若验证后仍有路径无法就绪，应剔除该路径并降级为 `slices_not_ready`，再发 done

## 执行流程

### 1. 发 `ontology_projection_progress`

先于任何文件生成调用 `emit_artifact`。

### 2. 扫描可用 slice

在 `<workspace_root>/ontology/` 目录下（不递归进入 `ontology/projections/` 子目录）按以下优先级查找切片文件：

- 首选：`*.slice.json`（标准命名）
- 兼容：任何 `*.json` 文件，若其顶层包含 `slice_request` 或 `concepts` 字段，视为合法 slice
- 不匹配：`.md` 文件不作为 projection 输入源（仅用于人工阅读）

读取每个合法 slice 的 `slice_request.topic`、`concepts`、`constraints`、validation 状态。

**⚠️ 若扫描结果为空**（`ontology/` 下无任何合法 JSON slice 文件）：所有 skill 记为 `no_available_slices`，立即跳到发出 `ontology_projection_done` 的步骤（`projected_count: 0`）。同时在 `ontology_projection_done.data` 中追加 `"diagnostic": "slices_not_ready"`，并可选追加自由文本字段 `"diagnostic_detail": "ontology/ 目录下未找到 *.slice.json 或含 slice_request 字段的 JSON 文件；请确认 ontology-slice-extraction 阶段已将切片写入文件系统"`。

**⚠️ 若扫描过程异常**（目录权限拒绝、IO 错误、JSON 解析失败等）：所有 skill 记为 `no_available_slices`，跳到发 done 步骤，`diagnostic` 取值 `"scan_error"`，并在 `diagnostic_detail` 中描述异常摘要。

### 3. 逐 skill 匹配

对每个 skill，以其 `triggers` + `description` 中的关键词与各 slice 的 `slice_request.topic`、`concepts[].name`、`constraints[].rule` 做语义匹配；取匹配度最高的一个 slice 作为来源。

**匹配偏好（宁投不弃）**：
- 若只有 **1 个可用 slice**：默认该 slice 对所有 skill 适用，除非 skill 描述的业务域与 slice 的 `scope.in_scope` 完全无交集（例如 slice 描述"应急流程"而 skill 描述"财务报表生成"，两者无任何共有概念）
- 若有 **多个 slice**：为每个 skill 选最相关的一个，但同一 slice 可被多个 skill 复用
- **跳过阈值**：只有在以下情况才允许判定 `no_matching_slice` — skill 的 triggers/description 中没有任何关键词能对应到任何 slice 的 concepts/relations/constraints 中的任何实体
- **部分匹配也投影**：即使 slice 只覆盖 skill 部分能力，也应生成 projection（在 `dropped_items` 中记录不相关项），不因覆盖不完整就跳过整个 skill

### 4. 生成 projection

仅当匹配成功且 slice validation ≠ FAIL：

- 读取 slice JSON（路径 `<workspace_root>/<slice_path>`）
- 以 `{baseDir}/templates/PROJECTION_TEMPLATE.json` 为骨架
- 将 `projection.source_slice.path` 指向该 slice 的相对路径
- `projection.projection_type`：默认 `workflow_contract_projection`；纯话术/引导类 skill 使用 `prompt_constraint_projection`
- `projection.intended_consumers`：填入当前 `skill_slug`
- 在 `concept_mappings`、`relation_mappings`、`constraint_mappings` 中只保留与该 skill 能力域直接相关的项（最小闭包原则，不相关的项写入 `dropped_items` 并附 reason）
- **将 `business_rules` 中已有规则映射到 `constraint_mappings`**：遍历 payload 中的 `business_rules`，对每条已有值的规则，在对应 skill 的 `constraint_mappings` 中追加一条约束项，`source` 标注为 `"business_rules_captured_in_skill_definition"`
- `open_questions`：若 slice validation 为 WARNING 且原 slice 有 open_questions，透传到此处；若 `business_rules` 中存在缺口（关键约束缺失），以结构化选项形式记录到 `open_questions`，待用户回答后回填
- **调用 `write_file` 工具写入**完整 projection JSON 到 `<workspace_root>/ontology/projections/<skill-slug>/<domain-slug>.<type-short>.projection.json`；其中 `<skill-slug>` 必须等于输入 `skills[].skill_slug`，不能使用 display_name、英文同义词或重新生成的 slug。
  - 优先使用沙箱文件写入工具 `write_file`；若当前工具清单没有 `write_file`，只能使用等价的 `create_file` / `save_file` 文件写入工具。
  - 禁止用 shell、Python here-doc、echo、cat 重定向或“只在对话里输出 JSON”来替代文件写入工具。
  - 如果当前环境没有任何可用文件写入工具，停止本轮 projection，发出不可用/跳过原因；不得把未落盘内容计入 `projected_count`，也不得发出成功形态的 `ontology_projection_done`。
  - `domain-slug`：由 slice 的 `slice_request.topic` 衍生（转小写、空格换短横线、保留字母数字短横线）
  - `type-short`：`workflow-contract` 或 `prompt-constraint`
- **写入后验证**：立即读取刚写入的文件，确认其包含完整的 projection 结构（至少含 `projection_type`、`source_slice`、`intended_consumers`、`concept_mappings`）。若验证失败，重新写入

**⛔ 禁止 stub 引用**：写入的 projection 文件**必须是完整的 JSON 结构**，至少包含以下顶层字段：
```json
{
  "projection_type": "workflow_contract_projection",
  "source_slice": { "path": "...", "topic": "..." },
  "intended_consumers": ["<skill-slug>"],
  "concept_mappings": [...],
  "relation_mappings": [...],
  "constraint_mappings": [...],
  "dropped_items": [...],
  "open_questions": [],
  "prompt_projection": { ... },
  "delivery_artifacts": [...]
}
```
**绝不允许**写入仅含 `note`/`source_projection_path` 的占位引用文件——这种 stub 文件无法被 skill-generation 消费，会导致产物包中 projection contract 无实质内容。

### 5. 无匹配时

记录到 `skipped_skills` + `skip_reasons`，不写文件，不阻断后续 skill 处理。

- `skip_reasons` 枚举值：`no_matching_slice` / `slice_validation_fail` / `no_available_slices`

### 6. Projection 文件就绪性验证

发 done 前的强制检查，扫描完成与发出 done 之间不可省略：

- **存在性**：用 `read_file` 确认文件已落盘
- **完整性**：JSON 顶层包含 `projection_type`、`source_slice`、`concept_mappings` 字段，且 JSON 可解析（未截断）
- **就绪等待**：若文件尚未就绪（不存在 / 内容空 / JSON 截断 / 正在写入），按 500ms 间隔轮询重读，**最长等待 5 秒**
- **写入失败回退**：5 秒等待窗口内若怀疑落盘失败，可立即重新写入后再次验证
- **超时降级**：等待超时后仍未就绪的路径，从 `projection_paths` 中剔除，对应 skill 移入 `skipped_skills`（`skip_reasons: "slices_not_ready"`），重新计算 `projected_count` / `skipped_count`
- **零结果处置**：若全部路径验证失败导致 `projected_count` 归零，按"落盘验证"小节给 `data.diagnostic` 赋值 `"slices_not_ready"`

### 7. 发 `ontology_projection_done`

所有 skill 处理完毕、且步骤 6 文件就绪性验证全部通过后才能发出。即使 `projected_count === 0` 也必须发出；当 `projected_count === 0` 时必须附带合法的 `diagnostic` 枚举值。

## 质量约束

- slice validation 为 **PASS** 或 **NOT_RUN**：生成 READY projection（`open_questions: []`）。`NOT_RUN` 表示 slice 已写入但未经过形式校验脚本，视为可用
- `NOT_RUN` 只表示"文件已真实写入但未跑结构校验"；**不表示**"内容没读到也可以靠占位 slice 继续"。
- slice validation 为 **WARNING** 且原 slice `open_questions` 为空：生成 READY projection
- slice validation 为 **WARNING** 且原 slice `open_questions` 非空：生成 WARNING projection（`open_questions` 透传非空）。只要 projection 文件有效且已落盘，后续 skill-generation 仍必须进入技能生成流程，并把它物化为完整 consumer contract；contract topic/view 保持 `WARNING` 状态并透传 `open_questions`，不得因此退回“补资料 / 重跑业务信息准备 / 重跑匹配技能数据”路线
- WARNING projection 的用户侧语义固定为“技能数据已匹配完成，但存在生成前确认项”。不得把它描述成“业务信息不足”“还不够直接落地”，也不得建议用户重跑业务信息准备；只列出具体选项题等待用户拍板。
- slice validation 为 **FAIL**：跳过，记入 `skipped_skills`
- slice 中无 `meta.validation` 字段（字段缺失）：等同于 `NOT_RUN`，生成 READY projection
- 无任何 `*.slice.json` 可用：所有 skill 记为跳过（`skip_reasons: "no_available_slices"`），仍发 `ontology_projection_done`（`projected_count: 0`）

## 输出范围约束

- 只写 `<workspace_root>/ontology/projections/` 目录（位于 `ontology/` 内，属于本 skill 的合法写入范围）
- 不修改已有 `ontology/*.slice.json` 或 `ontology/*.slice.md`
- 不写 `skills/`、`config/`、`external/` 目录
- 若 `workspace_root` 缺失，停下来报错，不要自行推断路径

## Skill slug 不可变规则

`skills[].skill_slug` 是雇佣流程确认后的业务技能主键，Projection Pass 必须逐字原样使用它。禁止把 `skill_slug` 按语义改写、同义替换、重新排序词根或重新生成新 slug。所有 projection 文件路径必须写入 `<workspace_root>/ontology/projections/<skill_slug>/...`，`projection.intended_consumers` 也必须只包含该原始 `skill_slug`。如果发现输入中同一技能存在多个候选 slug，必须阻断并向上游说明 slug 冲突，不能自行选择一个新目录继续。

## Quality Rules

- 优先做最小投影，不做全量映射
- 优先保留关系和约束，不只列名词
- 每个 skill 的投影必须是自包含的完整 JSON，不得为 stub 引用
- 找不到匹配 slice 时直接记录跳过，不编造映射
- `business_rules` 中已有规则不得重复提问，直接映射到 `constraint_mappings`
- 缺口提问必须精确到"哪个 skill 缺哪条规则"，给出选项，不笼统追问

## Forbidden Moves

- 把未验证的常识当作正式约束写入 projection
- 在 slice 未就绪时写占位 projection
- 改写或重命名 skill_slug
- 对 `business_rules` 中已有规则重新提问
- 输出仅含 `note`/`source_projection_path` 的 stub 文件

## References

- `{baseDir}/templates/PROJECTION_TEMPLATE.json`：下游投影输出模板
- `{baseDir}/templates/PROJECTION_TEMPLATE.schema.json`：下游投影结构校验规则
- `{baseDir}/references/PROJECTION_CONSUMPTION_GUIDE.md`：其他 skill 如何消费 projection.json
- `{baseDir}/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：consumer skill 专用 projection 目录与命名规范
- `{baseDir}/references/DOWNSTREAM_MAPPING_GUIDE.md`：下游代码生成 / 提示词编排映射规范
- `{baseDir}/references/SCHEMA_MIGRATION.md`：slice 与 projection 的 schema 版本迁移说明
- `{baseDir}/examples/ready/sample-projection.json`：READY 基线样例
- `{baseDir}/examples/ready/minimal-projection.json`：最小投影样例
- `{baseDir}/scripts/validate-projection.py`：skill 目录内真实 Python 校验器

## Instruction Scope

该 skill 作用于工作区内的 ontology/projections/ 目录、模板、样例和本地校验脚本。可读取和生成 `templates/`、`references/`、`examples/`、`scripts/` 下的内容。

它不会自动发明缺失 ontology slice，也不会在没有来源的情况下把经验性描述写成投影约束。
