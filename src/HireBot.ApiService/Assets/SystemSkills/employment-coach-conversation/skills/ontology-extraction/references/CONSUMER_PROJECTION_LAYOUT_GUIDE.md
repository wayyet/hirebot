# Consumer Skill 专用 Projection 目录与命名规范

本文档定义一套可直接落地的目录结构和文件命名规则，用于让其他 skill 稳定消费 `ontology-extraction` 生成的 projection 文件。

它要解决的不是“projection 是什么”，而是另外两个更具体的问题：

- projection 文件应该放在哪里，团队最不容易混乱。
- projection 文件应该怎么命名，才能一眼看出它服务谁、表达什么、属于哪类交付视图。

---

## 设计目标

这套规范优先满足四个目标：

- 让消费方 skill 一眼知道要读哪份 projection。
- 让同一 skill 可以并存多份 projection，而不靠口头约定区分。
- 让 projection 文件名本身表达主题和目标类型，而不是只叫 `sample-projection.json`。
- 让后续 review、升级和替换时，不需要猜“哪个文件才是当前生效版本”。

---

## 默认目录规范

对“consumer skill 专用”的 projection，默认放在消费方 skill 自己目录下，而不是放回 `ontology-extraction` 的 examples 中。

推荐结构：

```text
src/OpenClaw.Gateway/skills/<consumer-skill>/
  contracts/
    projections/
      <producer-skill>/
        <domain-slug>.
          <domain-slug>.<projection-type>.projection.json
          README.md
          REVIEW.md
```

把这个结构展开成真实示例，建议长这样：

```text
src/OpenClaw.Gateway/skills/daily-news-digest/
  contracts/
    projections/
      ontology-extraction/
        article-selection/
          article-selection.prompt-constraint.projection.json
          README.md
          REVIEW.md
```

这里的含义是：

- `contracts/projections/`：表明这里放的是被当前 skill 当作机器输入消费的 projection 契约。
- `<producer-skill>/`：表明这份 projection 是由哪个上游 skill 产出的。当前场景通常是 `ontology-extraction/`。
- `<domain-slug>/`：表明这组 projection 服务的是哪个任务域或主题，而不是哪个文件格式。

---

## 为什么默认用 contracts，而不是 references

如果 projection 会被消费方 skill 当成实际输入边界，默认放 `contracts/`，不要放 `references/`。

区分规则：

- `contracts/`：表示当前 skill 会真正读取、依赖并执行这份 projection。
- `references/`：表示当前 skill 只把它当说明材料或 review 旁证。

因此，consumer skill 专用 projection 的默认落点是：

- 首选：`contracts/projections/`
- 仅文档型引用：`references/projections/`

不要把真正会被消费的 projection 放在 `examples/`。那会让“示例”和“生效契约”混在一起。

---

## 文件命名规范

推荐统一格式：

```text
<domain-slug>.<projection-type-short>.projection.json
```

例如：

- `article-selection.prompt-constraint.projection.json`
- `skill-loading.workflow-contract.projection.json`
- `tool-capability.json-schema.projection.json`
- `risk-routing.domain-model.projection.json`

命名规则说明：

- `<domain-slug>`：表达业务主题、任务域或概念边界。
- `<projection-type-short>`：表达目标视图类型。
- 固定后缀 `projection.json`：表达这就是 projection contract，而不是普通 JSON。

---

## projection-type-short 映射规范

文件名里不要直接塞完整 schema 枚举名，推荐用短名，但要保持一一对应。

建议映射如下：

| projection_type | 文件名短名 |
| --- | --- |
| `domain_model_projection` | `domain-model` |
| `json_schema_projection` | `json-schema` |
| `prompt_constraint_projection` | `prompt-constraint` |
| `workflow_contract_projection` | `workflow-contract` |

这样做的原因很简单：

- 文件名更短。
- 人眼更容易扫。
- 仍然能稳定回到 schema 中的正式类型。

不建议把文件名直接写成：

- `article-selection-projection.json`
- `projection.json`
- `sample-projection.json`

这些名字都不表达 target type，也不表达主题边界。

---

## 目录名与文件名的职责分工

建议把“谁在消费”“谁生产”“投影的是什么”分散到不同层，而不是全堆进文件名。

推荐分工：

- skill 名：放在技能根目录路径里。
- producer skill 名：放在 `contracts/projections/<producer-skill>/` 这一层。
- domain/topic：放在 `<domain-slug>/` 目录和文件名前缀里。
- projection type：放在文件名中间。

这样可以避免出现这种又长又脆弱的名字：

```text
daily-news-digest-from-ontology-extraction-article-selection-prompt-constraint-projection.json
```

这个名字理论上信息全，但不利于维护。

---

## 一个 consumer skill 有多份 projection 时怎么放

如果同一个 consumer skill 需要消费多个主题的 projection，继续按主题分目录，不要全堆一个文件夹。

推荐：

```text
contracts/
  projections/
    ontology-extraction/
      article-selection/
        article-selection.prompt-constraint.projection.json
      source-ranking/
        source-ranking.workflow-contract.projection.json
      content-safety/
        content-safety.domain-model.projection.json
```

如果同一主题下存在多类交付视图，也放在同一主题目录里并列：

```text
contracts/
  projections/
    ontology-extraction/
      skill-loading/
        skill-loading.domain-model.projection.json
        skill-loading.json-schema.projection.json
        skill-loading.workflow-contract.projection.json
```

这样 review 时可以一眼看出：同一主题下有多个投影面，而不是多个不明来历的 JSON 文件。

---

## README 与 REVIEW 文件怎么配

每个 `<domain-slug>/` 目录下，建议至少带两个文档文件：

- `README.md`
- `REVIEW.md`

推荐职责：

- `README.md`：说明这组 projection 服务哪个 consumer skill 场景、当前有效文件是哪几个、应该由谁消费。
- `REVIEW.md`：记录当前评审状态、已知风险、open questions、替换历史和升级注意事项。

这样做的好处是：

- JSON 保持机器输入干净。
- 人类需要的治理说明不塞进文件名。
- 后续替换 projection 时，有地方记录决策，而不是靠 PR 对话回忆。

---

## 是否需要版本号进文件名

默认不建议把版本号放进文件名。

推荐做法：

- 文件名保持稳定。
- 结构版本继续放在 JSON 内部的 `projection_version`。
- 内容演进通过 Git 历史和 `REVIEW.md` 追踪。

只有在同一目录下必须并存多个活跃版本时，才把版本号放进文件名末尾：

```text
<domain-slug>.<projection-type-short>.v2.projection.json
```

例如：

- `skill-loading.workflow-contract.v2.projection.json`

但这应视为过渡状态，而不是长期默认。

---

## 不推荐的组织方式

下面这些方式容易出问题：

### 1. 把所有 projection 都扔到一个目录

例如：

```text
contracts/projections/
  a.json
  b.json
  c.json
```

问题是无法从目录层看出主题、producer 和 target type。

### 2. 用 sample、final、new、latest 之类的词命名

例如：

- `final-projection.json`
- `latest-projection.json`
- `new-workflow.json`

这些名字一旦时间过去就会失真。

### 3. 在文件名里重复 skill 名

例如：

```text
daily-news-digest.article-selection.daily-news-digest.prompt-constraint.projection.json
```

skill 名已经在路径里，重复写进文件名只会增加噪音。

---

## 对 consumer skill 的最小要求

如果一个 skill 采用这套目录规范，建议它在自己的 `SKILL.md` 中只补稳定事实和消费边界，不要把 `contract-index.json` 里的 topic 评分、target view 评分、冲突规则或请求映射示例再手写一遍。

建议至少补上：

1. projection contract 的发现入口或目录根路径。
2. 人工评审时的读取顺序。
3. 当前 skill 实际消费的字段或 view 边界。
4. blocked route、`open_questions` 和 `dropped_items` 的处理原则。

默认直接复用 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`，并只在 consumer `SKILL.md` 中补当前技能自己的字段边界、target view 边界或本地绑定路径。

---

## 最终推荐

如果你现在要开始给 consumer skill 落 projection，直接用下面这套：

```text
src/OpenClaw.Gateway/skills/<consumer-skill>/
  contracts/
    projections/
      ontology-extraction/
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
```

这是当前最稳的默认方案，因为它同时兼顾了：

- 路径可发现性
- 目标类型可识别性
- 主题边界可分组性
- review 和演进可治理性

如果没有特别强的例外需求，建议不要偏离这套默认结构。
