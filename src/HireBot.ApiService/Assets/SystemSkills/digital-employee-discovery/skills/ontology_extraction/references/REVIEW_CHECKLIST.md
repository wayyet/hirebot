# Ontology Slice 与 Projection 统一评审清单

本文档把两类产物收敛进同一套评审框架：

- slice：回答“当前任务相关的 ontology 子图是否被抽得够准、够稳、够可追溯”
- projection：回答“这份 slice 是否被忠实、安全地投影到了 codegen、prompt orchestration 或 workflow contract 等下游视图”

对应样例：

- slice 正向样例：[sample.json](../examples/ready/sample.json)
- slice 黄灯样例：[warning-sample.json](../examples/warning/warning-sample.json)
- slice 失败样例：[invalid-sample.json](../examples/invalid/invalid-sample.json)
- projection 正向样例：[sample-projection.json](../examples/ready/sample-projection.json)
- projection 正向样例：[json-schema-projection.json](../examples/ready/json-schema-projection.json)
- projection 正向样例：[workflow-contract-projection.json](../examples/ready/workflow-contract-projection.json)
- projection 黄灯样例：[warning-projection.json](../examples/warning/warning-projection.json)
- projection 失败样例：[invalid-projection.json](../examples/invalid/invalid-projection.json)

这份清单的目标不是替代 schema 校验器，而是给团队补上人工评审层。推荐做法是先跑结构校验器，再用本清单做人工评审：

- 如果当前目录位于 `ontology_extraction` 技能根目录内：slice 校验使用 `../scripts/validate-slice.py`，projection 校验使用 `../scripts/validate-projection.py`
- 如果从仓库根目录执行普通结构校验：slice 使用 `scripts/validate-ontology-slice.py`，projection 使用 `scripts/validate-ontology-projection.py`

---

## 评审对象

### A. Slice 评审

关注一份 ontology slice 本身是否成立：

- 结构是否通过 `../templates/TEMPLATE.schema.json`
- 概念、关系、约束是否足够稳定
- 来源、冲突、歧义和不确定项是否足够支持当前任务

### B. Projection 评审

关注一份 projection 是否对 slice 做了安全、可追溯的下游投影：

- 结构是否通过 `../templates/PROJECTION_TEMPLATE.schema.json`
- 是否保留关键语义，而不是把 ontology 压扁成实现碎片
- mapping policy、dropped items、open questions 和 prompt assumptions 是否足以支撑下游消费

---

## 统一评审分层

无论是 slice 还是 projection，都按下面三层来评：

### 第一层：结构合法性

关注点：当前文件能不能通过对应 schema。

典型结论：

- 通过：结构合法，可以进入下一层评审
- 失败：结构不合法，直接退回修改

### 第二层：语义质量

关注点：即使结构合法，它是否已经足够稳定、精确、可复用。

典型结论：

- 通过：可作为正式沉淀结果继续使用
- 黄灯：结构没问题，但证据、边界、映射或精度还不足
- 失败：虽然可能通过 schema，但语义风险已经高到不宜继续使用

### 第三层：评审状态

关注点：它当前适不适合直接进入下游消费，比如文档固化、代码生成、规则生成、workflow 编排或 CI 校验。

典型结论：

- `READY`：可以进入下游使用
- `WARNING`：仍需人工确认或补证据
- `FAIL`：不应进入下游使用

---

## A. Slice 统一判定标准

| 维度 | 必问问题 | 通过标准 | 黄灯标准 | 失败标准 |
| --- | --- | --- | --- | --- |
| 结构合法性 | 是否通过 slice schema 校验 | 全量通过，无结构错误 | 不适用 | 任一结构错误、类型错误、缺字段或非法枚举 |
| 来源质量 | 来源是否可追溯且足以支撑定义 | 有直接、稳定、足够强的来源支撑关键概念和关系 | 来源可追溯，但偏文档化、偏叙述性或信任度不足 | 缺来源、来源失真、来源与结论明显不匹配 |
| 范围边界 | 纳入和排除范围是否清楚 | 边界明确，排除理由合理 | 边界大致可用，但仍偏粗或残留明显模糊区 | 边界混乱，导致概念和关系无法稳定解释 |
| 概念定义 | 核心概念是否清晰、可复用 | 定义稳定，能区分相邻概念 | 有概念，但仍停留在泛化标签层 | 概念名存在但定义无法支撑实际复用 |
| 关系精度 | 谓词是否足以表达真实语义 | 关系精确，可指导实现或文档 | 关系合法但过宽，只能表达高层依赖 | 关系错误、误导或自相矛盾 |
| 约束有效性 | 约束能否阻止常见误读 | 约束清晰，能收敛解释空间 | 约束存在，但仍不足以压住歧义 | 缺少关键约束或约束本身失真 |
| 冲突处理 | 冲突是否被识别和处理 | 冲突已说明并有明确处理 | 冲突已记录，但仍待后续处理 | 冲突被忽略、静默合并或无法追溯 |
| 歧义与不确定项 | 未决问题是否被显式记录 | 已记录且不阻断当前目标 | 已记录，但会影响下游使用范围 | 未记录关键不确定项，或不确定性已高到不可用 |
| 下游可消费性 | 是否适合继续生成文档、代码或规则 | 可以直接被下游消费 | 仅适合作为草案或 review 输入 | 不应进入下游链路 |

---

## B. Projection 统一判定标准

| 维度 | 必问问题 | 通过标准 | 黄灯标准 | 失败标准 |
| --- | --- | --- | --- | --- |
| 结构合法性 | 是否通过 projection schema 校验 | 全量通过，无结构错误 | 不适用 | 任一结构错误、类型错误、缺字段或非法枚举 |
| 源 slice 绑定 | projection 是否绑定到清晰、可审阅的 source slice | 明确绑定到可定位 slice，且 source slice 当前可用 | 绑定存在，但 source slice 本身仍是 warning 或边界未稳 | 未绑定 source slice，或绑定失真 / 不可追溯 |
| 语义保真 | 关键 concept / relation / constraint 是否被保留 | 关键语义仍能在 projection 中找到明确映射 | 保留了部分语义，但存在明显压扁、降级或遗漏风险 | 关键语义被错误改写、静默丢失或无法追踪 |
| Mapping Policy | 下游策略是否足够保守且可解释 | 明确保留 trace / constraints，且默认阻止静默猜测 | 允许 warn-and-continue 或较宽松假设，但有显式风险提示 | 静默允许猜测、丢弃、扁平化，且缺少治理策略 |
| Traceability | 映射是否仍能回到来源与 slice | `source_ids`、source digest 或等效 trace 机制完整 | 有 trace，但覆盖不全或需要人工补链路 | 下游结果无法回溯到来源和 slice |
| Scope Reduction | dropped items 是否被显式记录并解释 | 所有裁剪项都有理由，且不破坏目标语义 | 存在裁剪，但影响边界仍需复核 | 静默丢弃关键项，或裁剪后已改变原始语义 |
| 下游契约稳定性 | delivery artifacts 和 target 定义是否清楚 | target、artifact、runtime、representation 明确且一致 | 目标大致可用，但仍依赖 open questions 或团队口头约定 | target 不清、artifact 虚化或前后矛盾 |
| Prompt / Runtime 安全性 | prompt assumptions、reasoning paths、guardrails 是否足够安全 | 明确禁止未映射术语、误读关系和越界推理 | 允许继续推进，但需带 warning / clarification | 鼓励或默认接受补造、越界或含糊解释 |
| Open Questions | 未决问题是否被显式记录且未超出容忍范围 | 无未决问题，或未决问题不阻断当前用途 | open questions 非空，且足以影响是否可直接定稿 | 关键未决问题未记录，或已大到不应进入下游 |

---

## Slice 三类样例的标准归类

### `sample`

推荐评审状态：`READY`

判定理由：

- 结构合法
- 来源质量较高，且包含代码与分析文档
- 概念、关系、约束都有明确落点
- 冲突与歧义已被处理或显式记录
- 可以作为团队直接改写的起点

评审重点：

- 看它是否仍贴合当前仓库真实实现
- 看术语口径是否需要随代码演进更新

### `warning-sample`

推荐评审状态：`WARNING`

判定理由：

- 结构合法，能通过 schema
- 但来源强度不够，概念边界偏粗，关系谓词偏宽
- 歧义和不确定项虽然被记录，但仍未收敛
- 适合做讨论草案，不适合直接当最终本体沉淀

评审重点：

- 是否缺高信任度事实源
- 概念是否仍停留在宽泛标签层
- 关系是否只表达了“看起来合理”的高层语义

### `invalid-sample`

推荐评审状态：`FAIL`

判定理由：

- 存在明确 schema 违规项
- 连结构稳定性都不能保证
- 不应进入语义质量讨论或下游使用阶段

评审重点：

- 报错是否覆盖预设失败点
- 报错是否精确、可理解、可修复

---

## Projection 三类样例的标准归类

### `sample-projection`

推荐评审状态：`READY`

判定理由：

- 结构合法，能通过 projection schema
- 绑定到一个 READY slice，而不是未定型语义源
- 关键 concept、relation 和 constraint 都有明确映射
- mapping policy 保守，traceability 清楚，dropped scope 有明确理由
- 可以作为团队改写 projection 的正向基线

评审重点：

- source slice 是否仍与仓库实现一致
- 映射后的 relation / constraint 是否仍忠实于 ontology 语义
- dropped items 是否仍然不会破坏目标场景

### `warning-projection`

推荐评审状态：`WARNING`

判定理由：

- 结构合法，能通过 projection schema
- 但它绑定的是 warning slice，而不是 READY slice
- mapping policy 明确允许 `warn_and_continue` 和较弱的 unmapped-term 策略
- open questions 非空，prompt 侧也显式带 warning 前进
- 适合作为讨论草案，不适合直接硬化成最终下游契约

评审重点：

- warning 是来自 source slice 还是 projection 决策本身
- 下游消费方是否真的能承受这种草案级映射
- 哪些 open questions 必须先澄清，才能从 WARNING 收敛到 READY

### `invalid-projection`

推荐评审状态：`FAIL`

判定理由：

- 存在明确 projection schema 违规项
- 根对象、mapping policy、mappings、prompt projection、meta 都被故意打坏
- 连结构层都没过，不应进入 projection 语义质量讨论

评审重点：

- 报错是否覆盖 projection 侧预设失败点
- 报错路径和文案是否足够清晰、可修复

---

## ReviewMode 启发式 warning 信号

### Slice ReviewMode

`../scripts/validate-slice.py --review-mode` 会在结构校验结果后，额外输出 `READY / WARNING / FAIL`。

如果从仓库根目录执行，`scripts/validate-ontology-slice.py` 只承载普通结构校验入口，不暴露 `--review-mode`。

当前用途：快速分流，不替代人工评审。

#### Slice 当前启发式结论含义

- `FAIL`：结构校验未通过，当前 slice 不能进入后续语义评审。
- `READY`：结构校验通过，且当前没有命中 warning 信号；内置 `sample.json` 也直接作为 READY 基线。
- `WARNING`：结构校验通过，但命中了 warning 信号；内置 `warning-sample.json` 也直接作为 WARNING 黄灯基线。

#### Slice 当前会触发 `WARNING` 的信号

- `sources` 中没有任何 `trust_level = high` 的来源。
- `sources` 中存在任意 `trust_level = low` 的来源。
- `conflicts` 中存在 `status = open` 或 `status = deferred` 的冲突。
- `ambiguities` 中存在 `status = open` 或 `status = deferred` 的歧义。
- `uncertainties` 数组非空。

### Projection ReviewMode

`../scripts/validate-projection.py --review-mode` 会在结构校验结果后，额外输出 `READY / WARNING / FAIL`。

如果从仓库根目录执行，`scripts/validate-ontology-projection.py` 只承载普通结构校验入口，不暴露 `--review-mode`。

当前用途：快速分流 projection 风险，不替代 projection review。

#### Projection 当前启发式结论含义

- `FAIL`：结构校验未通过，当前 projection 不能进入后续语义评审。
- `READY`：结构校验通过，且当前没有命中 projection warning 信号；内置 `sample-projection.json` 也直接作为 READY 基线。
- `WARNING`：结构校验通过，但命中了 projection warning 信号；内置 `warning-projection.json` 也直接作为 WARNING 黄灯基线。

#### Projection 当前会触发 `WARNING` 的信号

- projection 绑定的是 `warning-sample.json`。
- `mapping_policy.unresolved_item_policy` 不是 `block_or_escalate`。
- `mapping_policy.prompt_assumption_policy` 不是 `disallow_unmapped_terms`。
- `mapping_policy.relation_flattening_policy = allow`。
- `open_questions` 非空。
- `dropped_items` 非空，需要额外检查 scope reduction 是否安全。
- `prompt_projection.source_digest` 中显式带有 warning / conflict / no high-trust source 等信号。

### 如何使用这两组信号

- 如果评审状态是 `FAIL`，先修结构，不做语义讨论。
- 如果评审状态是 `WARNING`，优先检查 warning 是否会阻断下游消费。
- 如果评审状态是 `READY`，也不要跳过人工评审；它只表示当前没有命中这组快速风险信号。

### 这两组信号的边界

- 它们适合做保守分流，不适合直接作为准入门槛。
- 它们覆盖的是“容易自动发现的明显风险”，不是完整质量模型。
- 真正的接受、黄灯或退回结论，仍应以本清单前面的统一评审标准为准。

---

## 标准评审流程

建议统一按下面顺序执行：

1. 先确认你在评审的是 slice 还是 projection。
2. 先跑对应结构校验。
3. 如果结构失败，直接归类为 `FAIL`，不进入后续语义评审。
4. 如果评审的是 slice，再检查来源质量、概念边界、关系精度、约束有效性和未决问题。
5. 如果评审的是 projection，再检查 source slice 绑定、语义保真、mapping policy、traceability、dropped items 和 open questions。
6. 根据语义风险决定评审状态是 `READY` 还是 `WARNING`。
7. 只有当结构与语义都满足目标时，才允许进入下游消费。

---

## Slice 评审问题清单

每次评审 slice，至少回答下面九个问题：

1. 这份 slice 是否通过 schema 校验。
2. 关键概念和关系是否都有可追溯来源支撑。
3. 来源是否足够强，而不只是“看起来有关”。
4. 纳入范围和排除范围是否能解释当前边界。
5. 核心概念是否已经具备稳定定义，而不只是临时标签。
6. 关系谓词是否足够精确，能否直接指导实现、文档或规则。
7. 关键约束是否足以阻止常见误读。
8. 冲突、歧义和不确定项是否被显式记录，并且没有超出当前可接受范围。
9. 这份 slice 当前的评审状态应是 `READY`、`WARNING` 还是 `FAIL`。

## Projection 评审问题清单

每次评审 projection，至少回答下面九个问题：

1. 这份 projection 是否通过 schema 校验。
2. 它是否绑定到一个清晰、可定位的 source slice。
3. 关键 concept、relation 和 constraint 是否仍被明确保留，而不是被静默压扁。
4. mapping policy 是否足够保守，能阻止静默猜测和过度扁平化。
5. `source_ids`、source digest 或其他 trace 机制是否足够支持回溯。
6. dropped items 是否被显式记录，并且没有破坏当前目标语义。
7. target、artifact、representation 和 runtime 是否前后一致。
8. prompt assumptions、guardrails 和 required clarifications 是否足以约束下游行为。
9. 这份 projection 当前的评审状态应是 `READY`、`WARNING` 还是 `FAIL`。

---

## 推荐输出模板

### Slice 评审结论模板

- 结构结果：`PASS / FAIL`
- 评审状态：`READY / WARNING / FAIL`
- 来源质量：`通过 / 黄灯 / 失败`
- 范围边界：`通过 / 黄灯 / 失败`
- 概念定义：`通过 / 黄灯 / 失败`
- 关系精度：`通过 / 黄灯 / 失败`
- 约束有效性：`通过 / 黄灯 / 失败`
- 冲突与不确定项：`通过 / 黄灯 / 失败`
- 当前结论：`适合作为正向参考样例 / 适合作为黄灯讨论样例 / 适合作为失败路径测试样例`

可直接附一句总结：

- 这份 slice 结构合法且语义稳定，可进入下游使用。
- 这份 slice 结构合法，但仍需补充证据或收紧边界后再进入下游使用。
- 这份 slice 结构不合法，应先修复 schema 违规项。

### Projection 评审结论模板

- 结构结果：`PASS / FAIL`
- 评审状态：`READY / WARNING / FAIL`
- 源 slice 绑定：`通过 / 黄灯 / 失败`
- 语义保真：`通过 / 黄灯 / 失败`
- Mapping Policy：`通过 / 黄灯 / 失败`
- Traceability：`通过 / 黄灯 / 失败`
- Scope Reduction：`通过 / 黄灯 / 失败`
- 下游契约稳定性：`通过 / 黄灯 / 失败`
- Prompt / Runtime 安全性：`通过 / 黄灯 / 失败`
- 当前结论：`适合作为正向 projection 基线 / 适合作为黄灯 projection 讨论样例 / 适合作为 projection 失败路径测试样例`

可直接附一句总结：

- 这份 projection 结构合法、语义保真且可追溯，可进入下游使用。
- 这份 projection 结构合法，但 source slice、mapping policy 或 open questions 仍需复审。
- 这份 projection 结构不合法，应先修复 schema 违规项。

---

## 快速映射

如果只需要快速判断，可直接按下面映射：

- `sample` = slice 通过样例，说明“什么叫结构和语义都基本过关”
- `warning-sample` = slice 黄灯样例，说明“什么叫结构过关但语义仍需 review”
- `invalid-sample` = slice 失败样例，说明“什么叫连结构关都没过”
- `sample-projection` = projection 通过样例，说明“什么叫忠实、安全、可追溯地下游投影”
- `json-schema-projection` = projection 通过样例，说明“什么叫把 slice 稳定投影成 JSON Schema 契约”
- `workflow-contract-projection` = projection 通过样例，说明“什么叫把 slice 稳定投影成 workflow step 契约”
- `warning-projection` = projection 黄灯样例，说明“什么叫 projection 合法但仍不应直接定稿”
- `invalid-projection` = projection 失败样例，说明“什么叫 projection 连结构层都没过”

这些样例合起来，构成团队统一评审基线。

---

## 六态速查表

如果只想快速横向对比六类样例，可以直接看下表：

| 维度 | `sample` | `warning-sample` | `invalid-sample` | `sample-projection` | `warning-projection` | `invalid-projection` |
| --- | --- | --- | --- | --- | --- | --- |
| 对应文档 | `sample.md` | `warning-sample.md` | `invalid-sample.md` | `sample-projection.md` | `warning-projection.md` | `invalid-projection.md` |
| 产物层 | slice | slice | slice | projection | projection | projection |
| 结构结果 | `PASS` | `PASS` | `FAIL` | `PASS` | `PASS` | `FAIL` |
| 评审状态 | `READY` | `WARNING` | `FAIL` | `READY` | `WARNING` | `FAIL` |
| 典型定位 | slice 正向参考样例 | slice 黄灯讨论样例 | slice 失败路径样例 | projection 正向参考样例 | projection 黄灯讨论样例 | projection 失败路径样例 |
| 主要风险特征 | 风险已被控制在可接受范围 | 来源弱、边界粗、未决项多 | 结构已损坏 | 映射保真、trace 清晰、策略保守 | source slice / mapping policy / open questions 仍偏草案 | projection schema 大量违规 |
| 推荐动作 | 作为基线复制改写，并做常规 review | 补证据、收紧边界、确认是否阻断下游使用 | 先修 schema 错误，再重新校验 | 作为 projection 基线复制改写，并复核 source slice 是否仍成立 | 先确认 warning 是否会阻断 codegen / prompt orchestration | 先修 projection schema 错误，再重新校验 |
| 是否适合直接下游消费 | 是 | 否，先 review 再决定 | 否 | 是 | 否，先 review 再决定 | 否 |

可以把这张表理解为一个最小判断矩阵：

- 想看“什么叫合格 slice 基线”：看 `sample`
- 想看“什么叫合格 projection 基线”：看 `sample-projection`
- 想看“什么叫合格 JSON Schema projection 基线”：看 `json-schema-projection`
- 想看“什么叫合格 workflow contract projection 基线”：看 `workflow-contract-projection`
- 想看“什么叫结构过关但仍需 review”：看 `warning-sample` 和 `warning-projection`
- 想看“什么叫应该直接退回修结构”：看 `invalid-sample` 和 `invalid-projection`
