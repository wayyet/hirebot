# Ontology Slice 字段语义说明

本文档用于解释 `templates/TEMPLATE.schema.json` 中各字段的业务语义、推荐填法和统一口径，目标不是重复 schema 的技术约束，而是帮助团队在不同任务中产出风格一致、语义稳定的 ontology slice JSON。

相关文件：

- `../templates/TEMPLATE.schema.json`：结构校验规则
- `../templates/TEMPLATE.json`：合法示例模板
- `../templates/TEMPLATE.md`：人工整理版模板
- `./SCHEMA_MIGRATION.md`：schema 版本迁移说明
- `./DOWNSTREAM_MAPPING_GUIDE.md`：下游代码生成 / 提示词编排映射规范

---

## 总体原则

### 1. 切片优先，不做全量本体导出

输出应只覆盖当前任务真正依赖的概念、关系和约束。与当前任务无关的平行领域、历史遗留层、实验性定义，优先放到 `scope.exclude`，不要混入 `concepts` 或 `relations`。

### 2. 先语义一致，再追求字段完整

如果某个字段暂时无法确定，优先通过 `uncertainties` 明确记录缺口，而不是用模糊描述硬填。错误但看似完整的数据，比缺失更危险。

### 3. source 驱动，不凭常识补造

所有核心概念、关系、约束，都应该能回溯到 `sources` 中的至少一个来源。`source_ids` 不是可选注释，而是可追溯性的基础。

### 4. JSON 面向程序消费，措辞要稳定

字段值应尽量可复用、可比较、可校验。避免同一语义在不同切片里反复改写表述方式。

### 5. 先做 slice，再考虑下游映射

这一层先回答“概念、关系、约束是什么”，再去考虑如何映射到代码、schema 或提示词。不要为了迎合某个生成器，把概念层提前压扁成实现细节。

---

## 根字段说明

### `$schema`

- 含义：声明当前 JSON 使用哪个 schema 文件校验。
- 推荐填法：按当前 JSON 文件所在目录填写相对路径。在本规范包内，`templates/TEMPLATE.json` 保持为 `./TEMPLATE.schema.json`，`examples/*/*.json` 保持为 `../../templates/TEMPLATE.schema.json`。
- 注意：这是校验入口，不是业务数据。

### `schema_version`

- 含义：当前 ontology slice 输出结构版本。
- 当前口径：固定为 `1.0.0`。
- 注意：只有结构发生兼容性变化时才升级，不要因为业务内容变化就改版本号。

### `template_type`

- 含义：当前输出文档类型。
- 当前口径：固定为 `ontology_slice`。
- 注意：它用于区分输出类别，不表示业务领域。

---

## `slice_request`

这个对象描述“为什么要切这份 slice”。它不是结果，而是任务上下文。

### `task_name`

- 含义：当前任务名称。
- 推荐写法：用一个稳定、可检索的任务标题，例如“支付风控规则建模”而不是“帮我看一下这个”。

### `slice_topic`

- 含义：本次切片聚焦的主题子域。
- 推荐写法：比 `task_name` 更聚焦，例如“支付风险判断”“标签召回关系建模”“审批状态流转”。

### `task_goal`

- 含义：这份切片后续将服务什么动作。
- 推荐写法：描述下游用途，例如“用于规则引擎建模”“用于约束 LLM 输出术语”“用于生成 JSON Schema”。

### `expected_output`

- 含义：期望这份切片最终支持的输出物。
- 推荐写法：使用稳定短语，例如 `concept_table`、`relation_table`、`constraint_list`、`schema_generation`。
- 注意：这里写的是产物类别，不是执行步骤。

---

## `scope`

这个对象负责明确“边界”。团队协作里最容易失控的就是边界不清，所以这部分必须认真填。

### `scope.include`

- 含义：本次切片明确纳入的对象。
- `type` 口径：
  - `concept`：概念、实体、值对象、事件、规则主体
  - `relation`：概念与概念之间的关系
  - `constraint`：会约束概念或关系使用方式的规则
- `id` 口径：填对应对象 ID，推荐与最终 `concepts`、`relations`、`constraints` 中的 ID 保持一致。
- `reason` 口径：说明“为什么纳入”，而不是重复名称。

### `scope.exclude`

- 含义：本次明确不纳入的对象或领域。
- 推荐写法：写出排除项和排除原因，例如“排序特征工程，不属于标签召回切片”。
- 注意：排除项最好写“领域块”或“概念群”，不是零碎字段。

---

## `sources`

这个数组定义切片依据。没有 `sources`，整份输出就无法被审计和复核。

### `sources[].id`

- 含义：来源唯一标识。
- 格式：必须以 `S` 开头，例如 `S1`、`S_core_rules`。
- 建议：在同一份 slice 内保持短小稳定，不要频繁改名。

### `path`

- 含义：来源定位信息。
- 推荐写法：文件路径、文档名、代码位置、配置键路径。
- 注意：应尽量可回到原始出处，不要只写“群里讨论”“历史经验”。

### `source_type`

- 含义：来源类别。
- 枚举口径：
  - `document`：需求文档、设计文档、说明文档
  - `code`：代码、类型定义、枚举、常量、实现逻辑
  - `config`：配置文件或配置项
  - `schema`：结构定义、接口 schema、数据 schema
  - `ontology`：显式本体、词汇表、taxonomy
  - `data`：样例数据、已有结构化记录
- 注意：如果来源本身是“规范定义”，优先用 `schema` 或 `ontology`，不要笼统写 `document`。

### `role`

- 含义：该来源在本次切片中的职责。
- 推荐写法：例如“定义核心实体”“补充术语映射”“提供状态约束”。

### `priority`

- 含义：来源优先级，数值越小越优先。
- 推荐口径：主定义来源一般为 `1`，补充来源为 `2` 或更大。
- 注意：优先级表达“采信顺序”，不是“文件重要程度”。

### `trust_level`

- 含义：当前团队对该来源稳定性的主观信任等级。
- 推荐口径：
  - `high`：正式规范、主实现、稳定 schema
  - `medium`：实现存在，但文档不完整或存在待确认点
  - `low`：临时材料、样例数据、未经确认的补充来源

---

## `conflicts`

用于记录来源之间的冲突，而不是把冲突偷偷抹平。

### `item`

- 含义：冲突对象名称。
- 推荐写法：用概念名、关系名或约束名，不要写抽象描述。

### `conflicts[].source_ids`

- 含义：发生冲突的来源集合。
- 要求：至少两个来源。

### `resolution`

- 含义：当前如何处理冲突。
- 推荐写法：明确“采用谁、为什么、影响是什么”。

### `status`

- `open`：已识别冲突，但尚未定稿
- `resolved`：已明确采用方案
- `deferred`：知道有冲突，但暂时搁置，后续处理

---

## `summary`

这是给人快速读懂切片的摘要层，不代替底层结构。

### `topic`

- 含义：切片主题，应与 `slice_request.slice_topic` 基本一致。

### `one_line_conclusion`

- 含义：一句话总结这份切片解决了什么。
- 推荐写法：说明范围和作用，不要写空泛结论。

### `selection_basis`

- 含义：为什么纳入这些概念、关系和约束。

### `exclusion_basis`

- 含义：为什么排除其他部分。

---

## `concepts`

这是核心对象层。一个 `concept` 应该对应一个可被稳定引用的语义单元。

### `concepts[].id`

- 格式：必须以 `C` 开头。
- 推荐口径：同一领域内尽量保持稳定 ID，不要本次 `C1` 下次又换成别的命名体系。

### `name_zh` / `name_en`

- 含义：中文主名称与英文或系统标识符。
- 推荐口径：
  - `name_zh` 面向业务理解
  - `name_en` 面向代码、schema、字段或内部标识复用

### `aliases`

- 含义：别名、旧名、同义词。
- 注意：如果没有别名，传空数组，不要省略字段。

### `kind`

- `entity`：独立业务对象
- `value_object`：值对象、附属结构、无独立身份对象
- `event`：状态变化、动作、发生记录
- `rule`：规则性概念本身

### `definition`

- 含义：该概念在当前切片中的工作定义。
- 注意：应写“是什么”，不要写“怎么用”。

### `parent_concept_id`

- 含义：上位概念 ID。
- 推荐口径：
  - 有明确上位概念就填对应 `C...`
  - 没有就填 `null`

### `key_properties`

- 含义：当前任务必须关心的关键属性。
- 注意：不是所有属性的全集，只保留当前切片所需最小集。

#### `key_properties[].type`

- 枚举口径：`string`、`number`、`integer`、`boolean`、`object`、`array`、`enum`
- 推荐：如果属性值域有限且受控，优先使用 `enum`。

### `source_ids`

- 含义：支撑该概念定义的来源。
- 要求：至少一个。

---

## `relations`

关系层用来表达概念之间如何连接，不要把关系信息塞进概念定义文本里。

### `relations[].id`

- 格式：必须以 `R` 开头。

### `subject_concept_id` / `object_concept_id`

- 含义：关系两端概念。
- 要求：都应引用 `concepts` 中已有的 ID。

### `predicate`

- 含义：关系谓词。
- 推荐口径：使用稳定短语，例如 `depends_on`、`maps_to`、`contains`、`triggers`。
- 注意：不要在不同切片里把相同关系写成多个不同同义词。

### `cardinality`

- `1:1`：一对一
- `1:n`：一对多
- `n:1`：多对一
- `n:n`：多对多

### `direction`

- `uni`：单向关系
- `bidirectional`：双向关系

### `conditions`

- 含义：关系生效的前提条件。
- 注意：没有条件时传空数组，不要把条件塞进 `description`。

---

## `constraints`

约束层表示“什么可以、什么不可以、何时生效”。

### `constraints[].id`

- 格式：必须以 `K` 开头。

### `applies_to`

- 含义：约束作用对象。
- `concept_ids`：受约束的概念
- `relation_ids`：受约束的关系
- 注意：两者都可以为空数组，但至少应有一侧在业务上是有意义的。

### `rule`

- 含义：正向规则陈述。
- 推荐写法：写成可执行、可校验的要求。

### `trigger`

- 含义：约束何时生效。
- 推荐写法：例如“生成候选集时”“状态流转前”“写入配置前”。

### `forbidden`

- 含义：禁止项列表。
- 注意：这里写“不允许什么”，不要重复 `rule` 本身。

### `severity`

- `low`：偏提示性约束
- `medium`：一般性校验要求
- `high`：关键业务约束
- `critical`：违反会导致严重后果或结果不可用

---

## `term_mappings`

用于记录术语到概念的映射，是避免同义词漂移的关键层。

### `term`

- 含义：输入术语、别名或歧义词。

### `candidate_concept_ids`

- 含义：该术语可能对应的概念集合。

### `selected_concept_id`

- 含义：本次切片最终采用的概念。

### `reason`

- 含义：为什么选它，而不是其他候选。

---

## `ambiguities`

用于保留“当前还容易混淆，但未必是来源冲突”的问题。

### `description`

- 含义：歧义点本身。

### `impact`

- 含义：这个歧义会影响什么判断或下游动作。

### `ambiguities[].status`

- `open`：仍未解决
- `resolved`：已澄清
- `deferred`：先记录，后处理

---

## `uncertainties`

这个字段专门承接“当前无法确定”的内容，是团队协作时最重要的诚实机制之一。

### `uncertainties[].item`

- 含义：不确定对象。

### `missing_evidence`

- 含义：缺失了什么依据。

### `required_user_input`

- 含义：需要谁补什么，才能继续判断。
- 推荐写法：尽量具体到文件、目录、字段、文档或决策人。

---

## `next_actions`

用于把切片结果衔接到后续动作。

### `action`

- 含义：下一步建议动作。
- 推荐写法：写成可执行事项，例如“基于该切片生成 JSON Schema”“补齐 S3 来源中的状态机定义”。

### `owner`

- `agent`：由智能体继续执行
- `user`：需要用户或业务方处理
- `system`：应由系统流程或工具链处理

### `next_actions[].priority`

- `P1`：立即处理
- `P2`：重要但可排在后面
- `P3`：补充优化项

---

## `meta`

元数据层用于审计和追踪。

### `generated_at`

- 含义：生成时间。
- 格式：ISO-8601，例如 `2026-04-20T00:00:00Z`。

### `generated_by`

- 当前口径：固定为 `ontology-extraction`。

### `workspace`

- 含义：当前工作区、项目名或上下文标识。
- 推荐写法：尽量填可定位的项目标识，不要写泛泛描述。

### `notes`

- 含义：额外说明。
- 推荐写法：记录本次切片的特殊前提、限制或说明。

---

## 推荐填报顺序

为了减少返工，建议按以下顺序填：

1. `slice_request`
2. `scope`
3. `sources`
4. `summary`
5. `concepts`
6. `relations`
7. `constraints`
8. `term_mappings`
9. `conflicts` / `ambiguities` / `uncertainties`
10. `next_actions`
11. `meta`

---

## 常见错误

- 把 `concepts` 当成术语清单，只写名字不写定义
- `relations` 引用了不存在的概念 ID
- `source_ids` 填了来源，但 `sources` 中没有对应项
- 在 `description`、`definition` 中混入过多执行步骤
- 本应写进 `uncertainties` 的内容，硬塞成确定结论
- `scope.exclude` 空着，导致边界不清

## 最低质量门槛

一份可接受的 ontology slice，至少应满足：

- 有明确 `slice_request`
- 有至少一个可信 `source`
- 有至少一个定义清晰的 `concept`
- 关系和约束没有引用断裂
- 不确定项被显式记录
- 输出能通过 `templates/TEMPLATE.schema.json` 校验
