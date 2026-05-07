# Ontology Slice 下游映射规范

本文档定义 ontology slice 如何稳定映射到下游代码生成、Schema 生成、提示词编排和工作流节点配置。它不替代 `FIELD_GUIDE.md` 的字段语义说明，而是回答另一个问题：一份合法且可审阅的 slice，进入下游后应该怎样消费，才能既保留语义，又避免把 ontology 层错误压扁成实现细节。

相关文件：

- `../templates/TEMPLATE.schema.json`：结构约束来源
- `../templates/TEMPLATE.json`：工程化模板
- `../templates/PROJECTION_TEMPLATE.json`：下游投影模板
- `../templates/PROJECTION_TEMPLATE.schema.json`：下游投影结构校验规则
- `../examples/ready/sample-projection.json`：可直接改写的 READY projection 样例
- `../examples/ready/json-schema-projection.json`：面向 JSON Schema 的 READY projection 样例
- `../examples/ready/workflow-contract-projection.json`：面向 workflow contract 的 READY projection 样例
- `./FIELD_GUIDE.md`：字段语义与填报口径
- `./REVIEW_CHECKLIST.md`：人工 review 兜底规则

---

## 总体原则

### 1. ontology slice 是语义中间层，不是最终代码模型

slice 的职责是稳定表达“概念、关系、约束、来源和边界”，不是直接等同于类定义、数据库表、接口 DTO 或最终 prompt 文本。下游系统应把它当成中间语义层，再结合具体目标进行投影。

### 2. 先保真映射，再做目标裁剪

任何 codegen 或 prompt orchestration，在裁剪字段之前，先保证以下语义可追溯：

- 关键概念没有丢失
- 关键关系没有被错误改写成普通属性
- 关键约束没有被忽略
- `source_ids` 仍可回溯到依据

### 3. 一个 slice 可以映射到多个下游视图

同一份 slice 可以同时产出：

- 领域模型视图
- 校验规则视图
- prompt 术语约束视图
- 工作流节点输入输出视图

不要为了适配某一个下游，把上游 slice 设计成只服务单一消费方。

### 4. 下游缺口应显式回写，不要静默吞掉

如果下游无法消费某条关系或约束，应在生成日志、评审记录或补充文档中显式声明，而不是直接忽略。静默丢失语义会让 slice 看起来“已接入”，但实际效果失真。

---

## 映射层次

推荐把下游映射拆成三层：

### 1. Semantic Layer

直接消费原始 slice：

- `concepts`
- `relations`
- `constraints`
- `sources`
- `ambiguities`
- `uncertainties`

这一层用于保存语义全貌，不做实现绑定。

### 2. Projection Layer

按目标场景把 slice 投影成具体视图，例如：

- `domain_model_projection`
- `json_schema_projection`
- `prompt_constraint_projection`
- `workflow_contract_projection`

这一层允许裁剪，但必须记录投影规则。

### 3. Delivery Layer

输出最终可被下游直接消费的产物，例如：

- 类/接口定义
- JSON Schema
- system prompt 片段
- tool 调用约束
- workflow step contract

这一层允许目标特化，但不应反向污染 ontology slice 本体结构。

---

## 字段到下游的推荐映射

### `slice_request`

- 代码生成：映射为生成任务上下文、模块名、命名空间、目标产物说明。
- 提示词编排：映射为 prompt 的任务说明、使用范围和禁止越界提醒。
- 注意：不要把 `task_goal` 当成概念定义，它是任务上下文，不是领域对象。

### `scope.include` / `scope.exclude`

- 代码生成：决定哪些概念、关系、约束进入当前生成批次。
- 提示词编排：决定模型在当前回合允许讨论和引用的术语边界。
- 注意：`exclude` 应转成显式排除规则，而不是简单忽略。

### `sources`

- 代码生成：可映射为注释、trace metadata、生成报告或 provenance block。
- 提示词编排：可映射为“优先依据”“可信来源”“冲突来源提示”。
- 注意：不要把 `sources` 直接暴露成冗长 prompt 正文，可转成简洁引用片段或外部上下文附件。

### `summary`

- 代码生成：作为生成说明、模块摘要、设计注释。
- 提示词编排：可作为 system prompt 中的“任务概览”段。
- 注意：`summary` 是摘要，不替代结构化概念和约束。

### `concepts`

- 代码生成：优先映射为领域实体、值对象、枚举候选、事件类型或 capability 节点。
- 提示词编排：映射为术语表、角色表、对象类别、允许引用的名词集合。
- 注意：
  - 不要默认一个 concept 必然对应一个类。
  - `aliases` 适合生成术语同义映射，不适合直接生成多个等价字段。
  - `parent_ids` 更接近 taxonomy / inheritance / grouping 线索，是否落成继承结构要看目标语言和目标模型。

### `relations`

- 代码生成：可映射为引用关系、关联边、约束图、状态流转边或策略依赖边。
- 提示词编排：可映射为允许的推理路径、合法搭配、上下文联动规则。
- 注意：
  - 不要把所有 relation 都压平成对象字段。
  - 带方向性的 relation 应保留方向。
  - 带条件的 relation 应保留条件，不要只留下主体和客体。

### `constraints`

- 代码生成：映射为校验规则、 guard、 precondition、 policy、 schema rule。
- 提示词编排：映射为模型必须遵守的硬约束、拒答边界、格式约束、冲突消解规则。
- 注意：
  - `severity` 应保留，至少区分 hard / soft。
  - 约束不要只写进注释，应尽量落成可执行或可检测规则。

### `conflicts` / `ambiguities` / `uncertainties`

- 代码生成：映射为生成阻断、 warning、 TODO、人工确认项。
- 提示词编排：映射为“遇到该情况时需降级回答 / 请求澄清 / 标注不确定”的策略块。
- 注意：不要把这些字段丢掉；它们决定系统是否应在不确定时停手。

### `next_actions`

- 代码生成：映射为后续生成队列、补充建模任务、缺口列表。
- 提示词编排：映射为 agent 后续建议动作或需要继续收集的上下文。
- 注意：它是操作建议，不是当前 slice 已确认事实。

---

## 面向代码生成的规则

### 1. 优先生成“领域层”，再决定“技术层”

推荐顺序：

1. 从 `concepts` 生成领域对象候选
2. 从 `relations` 补引用与交互边
3. 从 `constraints` 生成校验或 guard
4. 最后再决定 DTO、数据库模型、API contract 是否需要二次投影

不要直接从 slice 跳到数据库字段，否则很容易丢掉关系和约束语义。

### 2. 关系不等于属性

以下情况应保留为关系，而不是平铺属性：

- 多对多依赖
- 状态流转
- 条件触发
- 角色授权
- 跨聚合引用

只有在目标产物明确要求扁平结构时，才允许把 relation 投影成属性，并且应保留来源说明。

### 3. 约束应尽量变成可执行逻辑

推荐投影方式：

- `hard` 约束 -> schema rule / validator / runtime guard
- `soft` 约束 -> warning / advisory / lint rule
- `forbidden` 类规则 -> explicit rejection path

如果约束只能留在注释里，说明当前代码生成还不完整。

### 4. 生成结果应保留溯源锚点

建议在生成结果旁边保留至少一种 trace 机制：

- 注释中的 `source_ids`
- 单独的 provenance json
- 生成报告中的 concept-to-file 映射

这样后续 review 才能追问“这段代码是由哪条 ontology 规则驱动的”。

---

## 面向提示词编排的规则

### 1. concepts 优先映射为术语边界，不是样例句子

对 prompt 来说，`concepts` 的主要价值是约束模型用词、对象分类和概念边界，而不是直接拼成 few-shot。few-shot 可以另做，但不应替代术语层。

### 2. relations 决定允许的推理路径

提示词中至少应把以下信息转成明确约束：

- 哪些概念可以关联
- 关联方向是什么
- 哪些关系需要前置条件
- 哪些关系不能被模型自行脑补

### 3. constraints 决定硬边界与拒答策略

推荐映射为：

- 输出格式要求
- 允许/禁止判断规则
- 必须澄清的情形
- 发生冲突或不确定时的降级策略

### 4. conflicts / ambiguities / uncertainties 必须进入 orchestration

这些字段至少应进入以下任一位置：

- system prompt 的风险提示段
- planner 的澄清触发条件
- tool 调用前的验证逻辑
- review 模式下的人工确认入口

如果编排链路丢掉了这些字段，模型会倾向于过度自信地产生补造内容。

### 5. sources 进入 prompt 时应做压缩表达

推荐格式：

- `Primary source: S1 product-policy.md`
- `Fallback source: S3 legacy-rulebook.md`
- `Conflict note: S2 vs S4 differ on eligibility threshold`

不要把整段原文无差别塞进 system prompt，除非当前任务明确需要原文 grounding。

---

## 推荐输出形态

### 1. 代码生成投影

建议直接基于 `../templates/PROJECTION_TEMPLATE.json` 维护一个显式投影结构，例如：

```json
{
  "projection_type": "domain_model_projection",
  "source_slice": "payment-risk-slice",
  "concept_mappings": [],
  "relation_mappings": [],
  "constraint_mappings": [],
  "dropped_items": [],
  "open_questions": []
}
```

### 2. prompt 编排投影

建议直接基于 `../templates/PROJECTION_TEMPLATE.json` 维护一个显式投影结构，例如：

```json
{
  "projection_type": "prompt_constraint_projection",
  "allowed_terms": [],
  "forbidden_assumptions": [],
  "required_clarifications": [],
  "reasoning_paths": [],
  "source_digest": []
}
```

这样做的好处是：下游拿到的不是一份被静默改写过的 slice，而是一份“可解释的投影结果”。

如果团队需要统一产出格式，优先复制 `../templates/PROJECTION_TEMPLATE.json`，然后按目标类型填写 `projection_type`、`concept_mappings`、`relation_mappings`、`constraint_mappings` 和 `delivery_artifacts`。提交前再用 `../templates/PROJECTION_TEMPLATE.schema.json` 做结构校验。

如果团队不想从空模板开始，可以先按目标类型选现成样例：

- `../examples/ready/sample-projection.json`：组合型 domain model + prompt policy 基线
- `../examples/ready/json-schema-projection.json`：JSON Schema 契约基线
- `../examples/ready/workflow-contract-projection.json`：workflow contract 基线

---

## 常见错误

- 直接把 `concepts` 生成为数据库表，完全跳过关系和约束
- 把所有 `relations` 压平成对象字段，丢失方向和条件
- 只把 `summary` 塞进 prompt，完全不带 `constraints`
- 生成代码时忽略 `conflicts` / `uncertainties`
- 为了适配单个下游，把 ontology slice 结构反向改坏
- 丢弃 `source_ids`，导致生成结果无法追溯

---

## 最低落地标准

一条合格的下游映射链路，至少应满足：

- 能说明 slice 被投影到了哪类下游产物
- 能说明哪些 concepts / relations / constraints 被保留
- 能说明哪些项被裁剪，以及为什么裁剪
- 对 `conflicts`、`ambiguities`、`uncertainties` 有明确处理策略
- 能回溯到 `sources`
- 不把 ontology 层直接偷换成实现层

如果这些条件做不到，就不应宣称“已经完成 ontology slice 接入”。
