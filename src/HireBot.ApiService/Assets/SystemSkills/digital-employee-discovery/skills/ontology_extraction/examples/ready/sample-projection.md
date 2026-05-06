# sample-projection 评审说明

这份文档是 [sample-projection.json](sample-projection.json) 的人工评审配套说明。它解释了为什么这个 projection 不仅在结构上合法，而且也是一个可供下游代码生成和提示词编排复用的 `READY` 参考样例。

---

## 样例定位

- 角色：`READY` projection 参考样例。
- 目的：展示一个既保留 ontology 语义、又可以被团队直接改造成实际产物的 projection 应该长什么样。
- 最佳用法：当团队要从一个已批准的 slice 推导出面向 codegen 或 prompt 的 projection 文件时，以它作为起点。

## 使用方式

```powershell
..\..\scripts\validate-projection.py .\sample-projection.json --review-mode
```

上面的命令适用于当前目录位于 `ontology_extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology_extraction\examples\ready\sample-projection.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

评审时，建议先从 [sample.json](sample.json) 入手理解它对应的源 slice，再对照 [sample-projection.json](sample-projection.json) 查看哪些内容被保留、哪些被投影出去、以及哪些内容被有意不输出。对任何改过的 projection 文件，都应使用 [../../templates/PROJECTION_TEMPLATE.schema.json](../../templates/PROJECTION_TEMPLATE.schema.json) 做校验。

## 结构层状态

- 结构结果：`PASS`
- 评审状态：`READY`
- 当前解释：这个样例适合作为一个合法且语义稳定的 projection 基线。

## 为什么这个 projection 是 READY

- 它来源于一个现成的 `READY` slice，而不是从凭空想象或不完整的 ontology 片段临时拼出来的。
- 它通过 `source_ids` 保留了可追溯性，而不是在 projection 过程中把来源信息丢掉。
- 它保留了一个核心 relation 和一个核心 constraint，而不是把所有语义都压平成普通字段。
- 它在 `dropped_items` 里记录了一项有意省略的内容，使 projection 的边界是显式的。
- 它同时包含面向代码和面向 prompt 的产物，因此这个样例展示的是实际下游用途，而不是一个玩具式映射。

## 推荐评审方式

1. 确认 [sample.json](sample.json) 中的源 slice 仍然与当前仓库行为保持一致。
2. 确认投影后的 `concept_mappings`、`relation_mappings` 和 `constraint_mappings` 仍然匹配源 slice 的语义。
3. 确认没有任何重要规则在 projection 过程中被静默降级。
4. 确认生成目标仍然准确代表预期的下游产物。

## 建议的评审结论模板

- projection 合法性：通过 `PROJECTION_TEMPLATE.schema.json` 校验
- 评审状态：`READY`
- 语义保留：关键 concept、relation 和 constraint 仍被表达出来
- 可追溯性：有来源支撑，且可被评审
- 当前结论：适合作为团队二次改造时的正向 projection 基线

## 最适合怎么用

- 帮助团队熟悉 projection 格式
- 为 codegen 工作提供一个具体的 projection 基线
- 展示 prompt policy 产物如何与 domain-model 产物并存
- 演示如何把被裁掉的范围显式记录下来

---

## 详细讲解

### projection 目标

- projection ID：`openclaw-skill-loading-domain-model-v1`
- projection 类型：`domain_model_projection`
- 目标名称：`OpenClawSkillLoadingModel`
- 目标格式：`csharp_domain_model`
- 目标运行时：`dotnet`
- 目标：把 skill loading ontology slice 投影成供下游 codegen 和 orchestration 使用的领域模型、校验规则和 prompt 约束。

这是一个 `READY` 目标，因为它紧贴具体的下游用途，没有把 ontology 语义和最终实现细节混在一起。

### 源 slice 绑定

- 源 slice：[sample.json](sample.json)
- 源主题：`skill loading order, source precedence, and eligibility filtering`
- Schema 版本：`1.0.0`

这很重要，因为这个 projection 锚定在一个明确批准过的 slice 上，而不是一次自由发挥式的重解释。

### 映射策略

| 策略 | 取值 | 为什么它支撑 `READY` |
| --- | --- | --- |
| `preserve_source_trace` | `true` | 让来源链对评审和调试仍然可见。 |
| `preserve_constraints` | `true` | 防止在 projection 过程中丢失规则。 |
| `relation_flattening_policy` | `disallow_by_default` | 避免把 relation 变成语义含糊的普通字段。 |
| `unresolved_item_policy` | `block_or_escalate` | 倾向于显式升级处理，而不是静默猜测。 |
| `dropped_item_policy` | `record_with_reason` | 让范围收缩过程可被评审。 |
| `prompt_assumption_policy` | `disallow_unmapped_terms` | 降低 prompt 侧凭空生成术语的风险。 |

这些策略是本文件之所以是 `READY` 而不只是“schema 合法”的最强信号。

### Concept 映射

| Concept ID | 目标 | 目标类型 | 为什么这是好的 projection |
| --- | --- | --- | --- |
| `C1` | `SkillsConfigModel` | `domain_entity` | 保留了顶层聚合，而不是把配置语义打散到各处。 |
| `C3` | `SkillDefinitionModel` | `domain_entity` | 保留了主要的可选 skill 实体。 |
| `C4` | `SkillSourceTier` | `enum` | 正确地把 source tier 当作一个有优先级语义的有界集合。 |

这之所以是 `READY`，是因为样例保留了表达下游领域模型所需的最小 concept 集合，而不是假装要把 slice 里的每个 concept 都投影出去。

### Relation 映射

- 已映射的 relation：`R3`
- 目标：`SkillDefinitionOriginatesFromSource`
- 表达形式：`domain_association`

这很重要，因为一个弱 projection 往往会把 source 信息降成普通字符串字段。保留 association，才能把后续驱动优先级和过滤逻辑的 ontology 边保留下来。

### Constraint 映射

- 已映射的 constraint：`K1`
- 目标：`HigherPrioritySourceOverridesLowerPriority`
- 类型：`runtime_guard`
- 严重级别映射：`high -> blocking_validation`

这之所以是 `READY`，是因为 projection 没有把优先级规则只留在 prose 说明里，而是把它变成了一个下游可执行的 guard。

### Prompt 投影

这个样例还包含一个面向 prompt 的视图：

- `allowed_terms` 保留了已批准术语表。
- `forbidden_assumptions` 用来阻止常见 projection 错误。
- `required_clarifications` 让文档与代码之间尚未解决的问题保持可见。
- `reasoning_paths` 说明下游允许沿哪些语义路径推理。
- `source_digest` 把来源信息压缩成适合 prompt 使用的形式。

这也是它适合作为 `READY` 基线的原因之一：它展示了同一个 slice 可以同时支持 domain-model 输出和 prompt-policy 输出，而不会破坏语义层。

### 交付产物

| 产物 | 类型 | 状态 | 为什么重要 |
| --- | --- | --- | --- |
| `SkillsConfigModel.cs` | `code_file` | `planned` | 展示了面向代码的目标。 |
| `SkillLoadingPromptPolicy.md` | `prompt_fragment` | `planned` | 展示了面向 prompt 的目标。 |

即使这两个产物目前都还是 `planned`，这个 projection 仍然是 `READY`，因为映射本身已经足够具体、可评审，并且绑定在一个合法的源 slice 上。

### 显式裁掉的范围

- 被裁掉的项：`C2`
- 原因：`SkillLoadConfig` 在这个 projection 中不会作为独立产物输出。

这提升了 `READY` 程度，因为省略是显式记录的。更弱的样例通常会直接把这个 concept 丢掉，却不给任何解释。

### 未决问题

- `open_questions` 为空。

这在这里是可以接受的，因为源 slice 已经解决了这条 projection 路径所需的主要冲突，剩余边界也已经在 `dropped_items` 和 `required_clarifications` 中显式记录。

---

## 实操评审标准

只有在下面这些条件都继续成立时，才能把这个 projection 视为 `READY`：

- [sample.json](sample.json) 仍然是一个有效的正向 slice 基线。
- `C1`、`C3`、`C4`、`R3` 和 `K1` 仍然是这个下游目标所需的正确核心子集。
- 优先级规则仍然应该落成可执行 guard，而不只是文档描述。
- prompt projection 仍然匹配当前仓库的术语和冲突处理方式。

如果这些前提中的任何一条发生漂移，就应该更新这个文件，而不是原样继续沿用。
