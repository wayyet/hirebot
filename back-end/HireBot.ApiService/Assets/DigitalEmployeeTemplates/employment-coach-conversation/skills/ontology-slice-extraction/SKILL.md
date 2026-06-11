---
name: ontology-slice-extraction
description: Internal HireBot downstream skill for ontology slice extraction from business materials. Use only when the current message is an internal downstream trigger from employment-coach-conversation with a valid artifact_payload containing material_handoff_summary data. Do not use directly for user-facing requests that merely mention ontology, slice, taxonomy, schema, or concept modeling; route those requests through employment-coach-conversation so stage gates and artifacts stay consistent.
compatibility: HireBot employment-coach-conversation v1.0
metadata:
  openclaw:
    emoji: "🧠"
  category: generation
  autonomy: 90
  trigger: hiring-session-ontology-slice, material-stage-completed
  input: uploaded-files, source-documents, material-summary
  output: ontology-slices, emit-artifact
---

# ontology-slice-extraction

Task-scoped ontology slicing for extracting the smallest verifiable subgraph needed by the current job.

## 入口门禁

本 skill 是雇佣流程内部下游执行器，不是用户可直接点名调用的对话入口。

仅在收到以下内部 payload 时继续执行：

- `employment-coach-conversation` 注入的 internal downstream trigger，且 `artifact_payload` 包含资料阶段 terminal 摘要字段：`workspace_root`、`template_slug`、`items` 或 `total_items`。

如果当前消息只是用户在聊天里提到"本体 / ontology / slice / 切片"等词，或缺少上述 `artifact_payload`，不得发出 `ontology_slice_extraction_progress` / `ontology_slice_extraction_done`，不得写入 `ontology/`。只回复一句：「本体抽取需要先完成资料阶段收口，我先回到资料阶段帮你把可抽取来源整理清楚。」

## Core Concept

不要导出整份 ontology，而是围绕当前任务构造最小语义闭包：

- `concepts`：当前任务真正依赖的核心概念
- `relations`：概念之间必须保留的关系
- `constraints`：会改变判断、实现或生成结果的规则边界
- `sources`：所有结论的可追溯依据

目标不是泛泛介绍 ontology，而是交付一个能被评审、校验、代码生成或提示词编排消费的局部本体。

## When to Use

| Trigger | Action |
| --- | --- |
| 用户明确提到 `ontology`、`本体`、`slice` | 抽取任务相关本体切片 |
| 需要从大图中只拿当前子域 | 收缩范围，构造最小闭包 |
| 需要统一术语、层级、约束 | 输出标准化概念/关系/规则 |
| 需要判断某实体或规则属于哪层 | 标注边界、上位概念和排除项 |
| 需要给 projection / codegen / prompt 提供稳定输入 | 产出结构化 slice JSON，作为下游消费的稳定语义输入 |

> **注意**：本 skill 只负责从资料中抽取本体切片。将切片映射到具体技能（projection pass）由独立的 `ontology-projection` skill 负责。

## Output Contract

默认输出必须同时包含两份文件：

- `.md`：面向人工阅读、评审和讨论，遵循 `templates/TEMPLATE.md`
- `.json`：面向工程消费、校验、codegen 和 prompt 编排，遵循 `templates/TEMPLATE.json` 与 `templates/TEMPLATE.schema.json`

两份文件必须描述同一个 ontology slice，保持相同的 `slice_request`、`scope`、`sources`、核心 `concepts`、`relations`、`constraints` 与未决项；不得只输出其中一种格式。

JSON 至少覆盖：

```yaml
slice_request: 当前任务、主题、目标、期望产出
scope: 纳入范围 + 排除范围
sources: 切片依据与信任度
summary: 一句话结论与选取依据
concepts: 核心概念、定义、类型、关键属性
relations: 主体、谓词、客体、条件、来源
constraints: 规则、触发条件、禁止项、严重级别
ambiguities/uncertainties: 未决问题
next_actions: 后续衔接动作
meta: 生成信息
```

Markdown 可先用 `templates/TEMPLATE.md` 草拟，但交付前必须同步落到 JSON；如果先生成 JSON，也必须补齐对应 Markdown 人读版。

## Slice 文件命名与写入规范（强制）

### 文件命名

| 文件类型 | 命名模式 | 示例 |
|---------|---------|------|
| JSON slice | `<topic-slug>.slice.json` | `emergency-response.slice.json` |
| Markdown slice | `<topic-slug>.slice.md` | `emergency-response.slice.md` |

**`topic-slug` 生成规则**：取 `slice_request.topic` 的值，若过长（超过 40 字符）或包含非路径安全字符，则截取语义核心部分，转小写、空格和下划线换短横线、保留 `[a-z0-9-]`、合并连续短横线。

**示例**：
- `slice_request.topic = "emergency-response-and-incident-sop"` → 文件名 `emergency-response.slice.json`（可简化）
- `slice_request.topic = "return-policy"` → 文件名 `return-policy.slice.json`

**⛔ 禁止命名**：
- `ontology.json`（无 `.slice` 后缀，与约定文档 `ontology-slice.md` 混淆）
- `slice-1.json`、`data.json`（无语义，下游 projection 虽可兼容发现但增加匹配歧义）
- 含大写字母或空格的文件名

### 写入目录

所有 slice 文件必须写入 `<workspace_root>/ontology/` 目录（不创建子目录）。

### 写入方式（关键）

**必须调用沙箱文件写入工具**将 slice 内容实际写入文件系统。本 skill 运行时可用的写入工具名称以沙箱暴露的工具清单为准（常见名称：`write_file`、`create_file`、`save_file`）。

**正确做法**：
```
工具调用：write_file
路径：<workspace_root>/ontology/emergency-response.slice.json
内容：{ "slice_request": { ... }, "concepts": [...], ... }
```

然后调用同工具写入对应 `.slice.md`。

**⛔ 错误做法**：
- 只在对话中输出 JSON 代码块而不调用 write_file —— 这等于没有写入
- 调用 `shell: echo '...' > file.json` 写入超过数行的 JSON —— 易截断、易转义错误
- 假设"描述了内容就等于写入了文件" —— 这是导致下游 projection 0-投影 的首要原因

### 写入后验证

每个 slice 文件写入后，**立即**用文件读取工具（`read_file` / `cat`）确认文件存在且内容非空。若验证失败，重新写入。

## emit_artifact 使用规范

本 skill 执行期间须在两个关键节点调用 `emit_artifact`，推动前端阶段更新。

### 进度节点（isTerminal: false）

在开始处理第一份资料、产出第一个 ontology slice 之前调用：

```json
{
  "kind": "data",
  "artifactType": "ontology_slice_extraction_progress",
  "label": "正在从资料中整理业务信息，共 {N} 条资料待处理",
  "skillName": "ontology-slice-extraction",
  "stage": "stage1_material",
  "isTerminal": false,
  "displayHint": "progress",
  "data": {
    "total_sources": 2,
    "completed_slices": 0
  }
}
```

### 完成节点（isTerminal: true）

所有 ontology slice 产出并校验通过后调用：

```json
{
  "kind": "data",
  "artifactType": "ontology_slice_extraction_done",
  "label": "业务信息已整理完成，共产出 {N} 份材料，准备进入技能定义阶段",
  "skillName": "ontology-slice-extraction",
  "stage": "stage1_material",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_sources": 2,
    "completed_slices": 2,
    "slice_paths": ["ontology/return-policy.slice.json", "ontology/dialogue-style.slice.json"],
    "validation": "PASS"
  }
}
```

### 落盘验证（发出 ontology_slice_extraction_done 的前置条件）

**在发出 `ontology_slice_extraction_done` 之前，必须确认以下条件全部满足**：

1. `<workspace_root>/ontology/` 目录下至少存在一个 `*.slice.json` 文件（真实写入文件系统，不是只在对话中描述）
2. `slice_paths` 中列出的每个路径对应的文件确实存在
3. 每个 `.slice.json` 文件同时有对应的 `.slice.md` 文件

**若资料不足以产出任何合格 slice**：不发 `ontology_slice_extraction_done`，改为在对话中向用户说明缺口，并建议补充资料。

**⛔ 严禁**：仅在对话中描述切片内容而不实际写入 `*.slice.json` + `*.slice.md` 文件就发出 done artifact — 这会导致下游 projection pass 和 skill-generation 无法读取到本体产物。

**⛔ 严禁**：在没有读到上传资料正文、只有 `source_hint` 或文件名的情况下，写"占位 slice"后再发 `ontology_slice_extraction_done`。这种产物会把上游假成功传播到 projection pass。

### 约束

- **先调用后输出**：识别到可推送事件时，先调用 `emit_artifact`，再继续后续对话或文件输出
- **data 禁止凭据**：data 字段中不得写入 token / 密钥 / 密码 / API Key
- **label 用业务语言**：描述对用户有意义的进度，不暴露内部字段名

## Workflow

### 1. Identify the slice boundary

先识别：

- 领域主题
- 核心实体
- 关键关系
- 约束条件
- 下游用途

如果用户要求"整份 ontology"，先收缩到当前任务直接相关的子图。

### 2. Read source files and write ontology slices

如果用户给的是上传文件，而不是已经整理好的 slice JSON，本 skill 自己读取资料并产出 slice：

- **定位上传文件（优先级最高）**：当由上游 `employment-coach-conversation` 触发时，输入中会包含 `material_handoff_summary` 数据，其中每条物料都有 `source_path` 字段。**直接使用 `source_path` 作为文件路径读取，不要运行 `shell: ls` 或 `shell: find` 来探索工作区**。`source_path` 为 `null` 的物料是纯文本描述，无对应文件。
- **上传资料门禁**：如果条目标记为用户上传但缺少 `source_path`，或 `source_path` 指向的文件不存在 / 不可读，立即把它视为阻断条件而不是可降级事实。此时只能请求补齐路径或重新上传，**不得**靠 `source_hint` 臆造 slice，也**不得**写占位 slice。
- **有界自愈恢复**：考虑到上传同步和 `source_path` 回填可能存在短暂竞态，对"刚上传但当前不可读"的情况先做一次窄范围恢复：
  - 若 `source_path` 已存在但目标文件不存在：按约 500ms 间隔重试读取原路径，最长等待 5 秒。
  - 若 `source_path` 缺失，但条目明确来自用户上传：只允许在 `<workspace_root>/uploads/` 目录内做一次窄范围候选恢复。优先用 `title` 文件名，其次从 `source_hint` 中提取原始文件名；若**恰好**匹配到 1 个可读文件，则把该相对路径视为恢复后的 `source_path` 并继续处理。
  - 若 5 秒后仍不可读，或候选匹配结果为 0 个 / 多个，则再进入阻断；不要做更宽泛的目录猜测，也不要跨出 `<workspace_root>/uploads/`。
- **工作区根目录**：`material_handoff_summary` 的 `data.workspace_root` 是雇佣教练在会话初始化时由沙箱解压工具创建的真实绝对路径（运行时确定，本 skill 把它当作不透明字符串使用，不要解析或重组）。本 skill 的所有产物必须写入 `<workspace_root>/ontology/` 目录（用 artifact 里收到的真实路径替换 `<workspace_root>`）。若 `workspace_root` 字段缺失，停下来通过下游 fallback artifact 报错，**不要**靠 `ls /workspace` 推断或自行拼接 `/workspace/<slug>`。
- 支持 Markdown、文本、JSON、YAML 等可读资料；无法读取的文件必须在摘要中说明，并保持为阻断项而不是降级为"占位资料"。
- 如果遇到 zip 或二进制文档，只有在运行时已经提供可读文本或解析后路径时才处理；不要假设存在额外解析工具。
- 默认使用 `incremental` 模式更新当前主题 slice；用户明确要求"全量替换"时使用 `full_replace` 替换当前主题 slice。
- 返回给用户的摘要必须说明资料解析情况、切片范围、更新模式和产物路径，而不是只给一个文件列表。

这一阶段的目标就是产出可审阅、可校验的 ontology slice；不存在额外的资料入库中间产物。

### 3. Locate authoritative sources

优先查找：

- 文档说明
- schema / taxonomy / vocabulary
- JSON / YAML / Markdown / RDF / OWL / Turtle 等结构化定义
- 代码中的类型系统、枚举、关系映射、命名常量

如果有多个来源：

- 优先最新、最近、最稳定、最贴近事实源的材料
- 明确记录本次采用了哪些来源
- 把冲突写进 `conflicts`，不要静默合并

如果没有可信来源：

- 直接说明缺失
- 说明是缺切片文件，还是只有零散术语
- 不要臆造 ontology 内容

如果当前请求已经明确要求"解析上传文件并写入沙箱"，则本 skill 应直接基于资料生成或更新 `ontology/*.slice.json` 与 `ontology/*.slice.md`，并把这些 slice 作为后续 projection 的输入。

### 4. Build the minimal semantic closure

只保留完成任务所需的：

- 目标实体及其直接相关实体
- 关键属性
- 关键关系
- 必须继承的上位概念
- 会改变判断结果的约束、规则、禁止项

默认排除：

- 无关平行领域
- 不再生效的历史定义
- 无法确认真伪的补充概念
- 只会增加噪音的扩展属性

### 5. Normalize terminology

统一输出：

- 中文名称
- 英文名称或原始标识符
- 别名 / 同义词
- 上下位关系
- 易混淆概念差异

如果不同来源命名不一致，显式写术语映射，不默认完全等价。

### 6. Validate and hand off

交付前至少确认：

- `source_ids` 能回到 `sources`
- 概念、关系、约束引用不悬空
- 冲突、歧义、不确定项已显式记录
- Markdown 与 JSON 文件同时存在，且表达的是同一个 ontology slice
- 如果本轮读取了上传资料，确认 `sources`、slice 产物和 `extraction_summary` 彼此一致
- 能通过 `{baseDir}/templates/TEMPLATE.schema.json` 对应校验，或直接运行 `{baseDir}/scripts/validate-slice.py`；如果从仓库根目录执行，则使用 `scripts/validate-ontology-slice.py`

## Quality Rules

- 优先做切片，不做全量转储
- 优先保留关系和约束，不只列名词
- 优先基于用户指定文件或目录加载
- ontology slice 输出必须同时提供 `.md` 与 `.json`，方便人工评审和工程消费对齐
- 用户提供上传文件时，直接读取资料并生成或更新 ontology slice
- 找不到来源时直接说明，不补造本体
- 当前任务已隐含切片范围时，不反复追问无关问题

## Clarify Before Proceeding

以下情况应先澄清或显式声明假设：

- 同时存在多个 ontology 来源且定义冲突
- 用户要求范围过大，已经变成全量 ontology 导出
- 当前任务缺少明确主题，无法判断切哪一层
- 用户要求基于不存在或未提供的 ontology 文件继续推理

## Forbidden Moves

- 把未验证的常识当作正式本体定义
- 把示例数据误当作概念层
- 省略关键约束后直接给出结论
- 在没有来源的情况下声称"这是标准 ontology 结构"

## References

- `{baseDir}/templates/TEMPLATE.md`：人工阅读和讨论模板
- `{baseDir}/templates/TEMPLATE.json`：工程化输出模板
- `{baseDir}/templates/TEMPLATE.schema.json`：严格结构校验规则
- `{baseDir}/references/FIELD_GUIDE.md`：字段语义与填报口径
- `{baseDir}/references/REVIEW_CHECKLIST.md`：三态样例统一评审标准
- `{baseDir}/references/DECISION_GUIDE.md`：切片决策指南
- `{baseDir}/examples/ready/sample.json`：READY 基线样例
- `{baseDir}/scripts/validate-slice.py`：skill 目录内真实 Python 校验器，支持 `--schema-path` 与 `--review-mode`
- `scripts/validate-ontology-slice.py`：仓库根目录 Python 包装入口，适合从任意当前目录直接校验，支持 `paths` 与 `--schema-path`
- `{baseDir}/README.md`：规范包总览

## Instruction Scope

该 skill 作用于工作区内的 ontology 相关文件、模板、样例和本地校验脚本。可读取和生成 `templates/`、`references/`、`examples/`、`scripts/` 下的内容。

进行结构校验时，优先按所在层级选择入口：

- 在 skill 目录内直接工作时，使用 `{baseDir}/scripts/validate-slice.py`
- 需要从仓库根目录或任意当前目录直接校验时，使用 `scripts/validate-ontology-slice.py`
- review 辅助模式仅真实校验器支持：使用 `--review-mode`
- 两类入口默认都落到 `templates/TEMPLATE.schema.json`，默认样例都是 `examples/ready/sample.json`

它不会自动发明缺失 ontology，也不会在没有来源的情况下把经验性描述写成本体结论。
