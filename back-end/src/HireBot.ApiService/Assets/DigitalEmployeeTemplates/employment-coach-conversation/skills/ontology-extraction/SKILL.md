---
name: ontology-extraction
description: 当用户提到 ontology、本体、slice、schema/projection/mapping、taxonomy 或概念关系建模时，从文档、schema 或代码中抽取当前任务所需的最小可验证 slice，保留 concepts、relations、constraints 与 sources，供评审、codegen 和 prompt 编排使用。
metadata: {"openclaw":{"emoji":"🧠"}}
---

# ontology-extraction

Task-scoped ontology slicing for extracting the smallest verifiable subgraph needed by the current job.

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
| 用户明确提到 `projection`、`schema mapping`、`mapping` | 将 slice 约束到下游 projection 语义，并保留映射关系与边界 |
| 需要从大图中只拿当前子域 | 收缩范围，构造最小闭包 |
| 需要统一术语、层级、约束 | 输出标准化概念/关系/规则 |
| 需要判断某实体或规则属于哪层 | 标注边界、上位概念和排除项 |
| 需要给 projection / codegen / prompt 提供稳定输入 | 产出结构化 slice JSON，作为下游消费的稳定语义输入 |

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

## Employment Coach Handoff Contract

当本 skill 由 `employment-coach-conversation` 通过 `<dispatch target=ontology-extraction>` 调起时，输入是一组阶段一 material Handoff todo。优先按 Handoff todo 合约处理，不要把它当普通会话描述重新追问或重新归类。

输入形态：

```yaml
dispatch:
  target: ontology-extraction
  handoff_ids: [m_cs_nonstandard_rules_001, m_cs_dialogue_style_001]
  mode: incremental

handoff_todos:
  - session_id: session_20260508_001
    handoff_id: m_cs_nonstandard_rules_001
    kind: handoff_todo
    stage: material
    target_skill: ontology-extraction
    intent: 抽出非标退货场景的判定规则与处置路径
    category: 决策规则
    payload:
      objective: 抽取《非标退货处理规则》里的判定条件、处置档位、分流到经理的触发条件
      source_files: [非标退货处理规则.docx]
      scene_hint: customer_service
      mode: incremental
    source: 用户上传《非标退货处理规则.docx》并说明先处理这批资料
    acceptance: ontology 中包含退货判定条件、处置档位和人工分流触发节点，并给出 slice 文件
    status: ready_to_dispatch
```

处理规则：

1. 入口先校验 dispatch target 与 Handoff 范围：只处理本次 `dispatch.handoff_ids` 中存在、且 `kind: handoff_todo`、`stage: material`、`target_skill: ontology-extraction`、`status: ready_to_dispatch | dirty` 的 Handoff todo。
2. `drafting`、`dispatched`、`confirmed`、`needs_review`、`dismissed` 或 stage / target_skill 不匹配的 Handoff todo 不得落盘正式 slice；必须在 `todo_results` 中标为 `skipped` 或 `failed`，并给出可读原因。
3. 每条 Handoff todo 的完整结构只作为输入使用，不写入 slice 产物；上游确认关系由 `dispatch_callback` 的 `handoff_ids` 和 `todo_results` 承载。
4. 按 Handoff todo 的 `payload.source_files` 收集上传资料；文件路径可能来自沙箱上传通道，也可能是系统解析后的可读路径。
5. 直接读取 `payload.source_files` 指向的资料，并围绕 Handoff todo 的 `objective`、`category`、`scene_hint`、`source` 和 `acceptance` 构造最小可验证 slice；不要调用不存在的中间接入 skill 或工具。
6. `payload.mode` 优先，缺失时使用 dispatch `mode`，仍缺失时默认为 `incremental`。`incremental` 表示在现有同主题 slice 上增量合并；`full_replace` 表示替换同主题 slice 的内容。两种模式都只作用于本 skill 产出的 slice，不删除人工维护的其他 ontology 文件。
7. 每条 todo 产出的 slice 必须同时落盘 `.md` 与 `.json`，并在 `sources` 中回指本轮资料或其他权威来源。
8. 如果多个 Handoff todo 属于同一业务主题，可以合并为一份 slice，但必须在 `meta.notes`、人读版和回传 `todo_results[].artifacts` 中列出覆盖的 Handoff id，避免回传时无法逐条确认。

回传给主 skill 时输出 `dispatch_callback` 兼容结构化摘要，必须支持批量 todo 的部分成功 / 部分失败：

```yaml
dispatch_callback:
  source_dispatch_target: ontology-extraction
  handoff_ids: [m_cs_nonstandard_rules_001, m_cs_dialogue_style_001]
  user_summary: 已从这批资料中抽出退货判定条件、处置档位和话术风格特征；结果已写入 ontology，并标出仍需确认的边界。
  technical_artifact:
    ontology_dir: ontology
    extraction_summary: 本轮资料解析、切片范围和更新模式摘要
    validation: PASS | WARNING | FAIL
  artifacts:
    - path: ontology/return-policy.slice.json
      kind: ontology_slice_json
    - path: ontology/return-policy.slice.md
      kind: ontology_slice_markdown
  todo_results:
    - handoff_id: m_cs_nonstandard_rules_001
      status: success | warning | failed | skipped
      validation: PASS | WARNING | FAIL
      artifacts:
        - path: ontology/return-policy.slice.json
          kind: ontology_slice_json
        - path: ontology/return-policy.slice.md
          kind: ontology_slice_markdown
      extraction_summary: 本轮资料解析、切片范围和更新模式摘要
      errors: []
    - handoff_id: m_cs_dialogue_style_001
      status: failed
      validation: FAIL
      artifacts: []
      extraction_summary: 无
      errors:
        - 来源文件无法读取，或资料不足以支撑该 todo 的 acceptance
  status: success | partial | failed
  errors: []
```

`user_summary` 必须能被雇佣教练用一两句话复述给业务用户；不要只返回文件列表。`todo_results` 必须覆盖本次 dispatch 的每个 Handoff id，让主 skill 能单独确认成功项、重发 dirty / failed 项，或跳过不合法项。若 schema 校验失败、来源不足以支撑结论，或某条 Handoff todo 的 `acceptance` 未达成，对应 `todo_results[].validation` 标为 `FAIL` 或 `WARNING`，并在 `todo_results[].errors` 与 `user_summary` 中说明需要补什么。整体 `status` 规则：全部成功为 `success`，成功与失败 / warning 混合为 `partial`，全部失败为 `failed`。

## Workflow

### 1. Identify the slice boundary

先识别：

- 领域主题
- 核心实体
- 关键关系
- 约束条件
- 下游用途

如果用户要求“整份 ontology”，先收缩到当前任务直接相关的子图。

### 2. Read source files and write ontology slices

如果用户给的是上传文件，而不是已经整理好的 slice JSON，本 skill 自己读取资料并产出 slice：

- 从 Handoff todo 的 `payload.source_files` 收集资料路径。
- 支持 Markdown、文本、JSON、YAML 等可读资料；无法读取的文件必须写入 `todo_results[].errors`。
- 如果遇到 zip 或二进制文档，只有在运行时已经提供可读文本或解析后路径时才处理；不要假设存在额外解析工具。
- 默认使用 `incremental` 模式更新当前主题 slice；用户明确要求“全量替换”时使用 `full_replace` 替换当前主题 slice。
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

如果当前请求已经明确要求“解析上传文件并写入沙箱”，则本 skill 应直接基于资料生成或更新 `ontology/*.slice.json` 与 `ontology/*.slice.md`，并把这些 slice 作为后续 projection 的输入。

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
- 用户提供上传文件时，直接读取资料并生成或更新 ontology slice，再继续投影
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
- 在没有来源的情况下声称“这是标准 ontology 结构”

## References

- `{baseDir}/templates/TEMPLATE.md`：人工阅读和讨论模板
- `{baseDir}/templates/TEMPLATE.json`：工程化输出模板
- `{baseDir}/templates/TEMPLATE.schema.json`：严格结构校验规则
- `{baseDir}/templates/PROJECTION_TEMPLATE.json`：下游投影输出模板
- `{baseDir}/templates/PROJECTION_TEMPLATE.schema.json`：下游投影结构校验规则
- `{baseDir}/references/FIELD_GUIDE.md`：字段语义与填报口径
- `{baseDir}/references/REVIEW_CHECKLIST.md`：三态样例统一评审标准
- `{baseDir}/references/DOWNSTREAM_MAPPING_GUIDE.md`：下游代码生成 / 提示词编排映射规范
- `{baseDir}/references/PROJECTION_CONSUMPTION_GUIDE.md`：其他 skill 如何消费 projection.json
- `{baseDir}/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：consumer skill 专用 projection 目录与命名规范
- `{baseDir}/references/SCHEMA_MIGRATION.md`：slice 与 projection 的 schema 版本迁移说明
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
