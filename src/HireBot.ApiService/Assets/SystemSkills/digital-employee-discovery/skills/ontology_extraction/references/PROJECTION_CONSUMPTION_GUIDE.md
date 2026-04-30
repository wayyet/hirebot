# 其他 Skill 如何消费 projection.json

本文档回答一个具体问题：由 `ontology_extraction` 生成的 projection 文件，怎样在其他 skill 中真正落地，而不是只当成一个旁路文档。

核心原则只有一句：projection 不是最终产物，而是其他 skill 的下游语义契约。

这意味着其他 skill 不应直接重新理解原始 ontology，也不应绕开 projection 自行补造映射，而应把 projection 当成受约束的输入层。

---

## 一句话定位

- slice 负责表达最小语义闭包。
- projection 负责把 slice 收敛成某个目标视图。
- 其他 skill 负责在这个目标视图内继续生成 prompt、代码、schema、workflow 或 review 结果。

如果另一个 skill 已经拿到了 projection，就不应再回退到“重新抽 ontology”模式，除非 projection 明确不足、失效，或 review 结论不允许继续下游消费。

---

## 推荐接入模型

推荐把其他 skill 对 projection 的消费分成三步。

### 第一步：把 projection 当成显式输入

在其他 skill 的工作流里，先读取 projection 文件，而不是只读取自然语言需求。

最低要求：

- 读取 `projection.projection_type`，确认当前 skill 处理的是哪类目标视图。
- 读取 `projection.target_format` 和 `projection.target_runtime`，确认当前输出层是否匹配。
- 读取 `projection.source_slice`，保留回溯链路。
- 读取 `mapping_policy`，把它当成执行边界，而不是说明文字。

如果目标 skill 不理解当前 `projection_type`，正确做法是停止并说明“不支持该投影类型”，而不是强行继续。

### 第二步：只在映射结果允许的范围内工作

其他 skill 处理 projection 时，应该优先消费这些字段：

- `concept_mappings`：定义可以落成哪些对象、名称、目标路径和目标种类。
- `relation_mappings`：定义哪些关系仍然必须保留，以及应该以什么形式表达。
- `constraint_mappings`：定义哪些规则必须继续进入下游执行层。
- `delivery_artifacts`：定义当前 skill 预期要交付哪些目标物。
- `dropped_items`：定义哪些内容已经被显式裁掉，不能偷偷补回去。
- `open_questions`：定义哪些未决项会阻断继续生成。

换句话说，projection 不是“建议”，而是其他 skill 的工作边界。

### 第三步：把 projection 中的风险信号继续传下去

其他 skill 不应只消费正向映射，还应继续消费治理信息：

- `mapping_policy.unresolved_item_policy = block_or_escalate` 时，遇到未映射项应停止或升级，而不是猜测。
- `mapping_policy.prompt_assumption_policy = disallow_unmapped_terms` 时，prompt 类 skill 不应补造新术语。
- `open_questions` 非空时，通常不应直接生成最终定稿产物。
- `dropped_items` 非空时，应明确告知当前输出是裁剪后的结果。

这一步的目标是避免其他 skill 在下游重新把语义风险“洗掉”。

---

## 三种典型消费方式

### 1. Prompt 类 Skill

适用对象：总结、写作、提示词编排、agent policy、review assistant 一类 skill。

优先消费：

- `prompt_projection.allowed_terms`
- `prompt_projection.forbidden_assumptions`
- `prompt_projection.required_clarifications`
- `prompt_projection.reasoning_paths`
- `prompt_projection.source_digest`

接入方式：

- 把 `allowed_terms` 当成允许使用的术语边界。
- 把 `forbidden_assumptions` 当成禁止补造的规则。
- 把 `required_clarifications` 当成澄清触发器。
- 把 `reasoning_paths` 当成允许的推理路径。
- 把 `source_digest` 压成引用来源或 grounding 摘要。

最常见错误是：只拿 `summary` 或 `projection_goal` 写 prompt，完全丢掉 `constraints` 和 `forbidden_assumptions`。

### 2. Codegen / Schema 类 Skill

适用对象：代码生成、接口生成、JSON Schema 生成、配置契约生成一类 skill。

优先消费：

- `concept_mappings`
- `relation_mappings`
- `constraint_mappings`
- `delivery_artifacts`

接入方式：

- 只生成 `projection_action = map` 的项。
- 依据 `target_kind` 决定生成实体、值对象、枚举、schema rule 或 guard。
- 依据 `target_path` 和 `artifact.path` 决定输出位置。
- 依据 `severity_mapping` 决定校验规则的硬度。

最常见错误是：看见 concept 就直接生成类，忽略 relation 和 constraint。

### 3. Workflow / Orchestration 类 Skill

适用对象：工作流编排、tool contract、执行节点设计、planner 一类 skill。

优先消费：

- `workflow_contract_projection` 类型的 projection
- `relation_mappings` 中的 `workflow_edge`
- `constraint_mappings` 中的 `workflow_precondition`
- `delivery_artifacts` 中的 workflow contract 目标

接入方式：

- 把概念映射成 step input、step output 或 shared enum。
- 把关系映射成流程边，而不是注释。
- 把约束映射成 gating precondition、审批条件或阻断条件。

最常见错误是：把 workflow contract 当普通说明文档处理，没有真正进入执行顺序和前置条件层。

---

## 其他 Skill 的最小写法

如果你要让另一个 skill 正式消费 projection，建议它在自己的 SKILL.md 里只写稳定的消费原则，不要重复抄写 topic 评分、target view 评分、冲突规则或请求示例。真正会变化的路由逻辑应留在 `contract-index.json` 和各 topic 自己的 contract 文档里。

SKILL.md 至少应写清楚四件事：

1. 这个 skill 的 projection contract 由 runtime 或约定路径发现，而不是靠本文件手工路由。
2. 人工评审时先读哪里，再读哪里。
3. 它会消费 projection 的哪些字段。
4. 遇到 blocked route、未映射项或 `open_questions` 时怎么处理。

默认直接复用 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`，再只补当前 consumer skill 的本地约束，而不是在每个 `SKILL.md` 里手写一份新版本。

---

## 放在哪里最合适

有两种常见落法。

如果你需要一套可直接执行的目录与命名规范，不要只看本节概要，直接看 [CONSUMER_PROJECTION_LAYOUT_GUIDE.md](./CONSUMER_PROJECTION_LAYOUT_GUIDE.md)。

### 方式 A：把 projection 当成外部输入文件

适合团队已经有独立治理目录的情况。

做法：

- 其他 skill 在运行时读取一个明确路径的 projection 文件。
- skill 自己不复制 projection，只引用它。
- 适合希望 projection 作为单一事实源持续演进的团队。

### 方式 B：在消费 skill 目录下保留一份绑定版 projection

适合某个 skill 长期依赖固定 target contract 的情况。

做法：

- 在该 skill 目录下新增 `references/` 或 `contracts/` 子目录。
- 将该 skill 实际消费的 projection 文件放进去。
- 在 SKILL.md 的 References 段中明确引用。

这种做法的好处是 skill 自包含，代价是需要处理版本同步。

---

## 什么时候不该直接消费 projection

下面几种情况，不应把 projection 当成稳定输入继续用：

- projection 结构未通过 schema 校验。
- projection 的评审结论仍是 WARNING，但当前任务需要最终定稿产物。
- `open_questions` 为空并不代表安全，但如果 `source_digest` 明确带 conflict 或 warning，仍应先做评审。
- 目标 skill 实际需要的是另一类交付视图，例如当前只有 prompt projection，却想直接生成 workflow contract。

这时应回到 slice 或 projection 层先修正，不要在消费 skill 内静默修补。

---

## 推荐实践总结

最稳妥的接法是：

1. 用 `ontology_extraction` 产出 slice。
2. 用 projection 把 slice 收敛成目标视图。
3. 让其他 skill 明确声明“我消费哪类 projection”。
4. 让其他 skill 严格遵守 `mapping_policy`、`dropped_items` 和 `open_questions`。
5. 最终生成结果时继续保留 traceability，而不是只保留最终文本或代码。

如果做到这五点，projection 才真正成为 skill 间可传递的语义契约，而不是一份只能给人看的中间 JSON。
