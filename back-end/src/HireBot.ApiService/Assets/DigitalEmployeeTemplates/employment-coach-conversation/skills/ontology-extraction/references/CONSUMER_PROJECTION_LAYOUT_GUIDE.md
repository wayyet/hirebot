# Consumer Skill 专用 Projection 目录与命名规范

本文档定义一套可直接落地的目录结构和文件命名规则，用于让其他 skill 稳定消费 `ontology-extraction` 生成的 projection 文件。

它要解决的不是"projection 是什么"，而是另外两个更具体的问题：

- projection 文件应该放在哪里，团队最不容易混乱。
- projection 文件应该怎么命名，才能一眼看出它服务谁、表达什么、属于哪类交付视图。

---

## 设计目标

这套规范优先满足四个目标：

- 让消费方 skill 一眼知道要读哪份 projection。
- 让同一 skill 可以并存多份 projection，而不靠口头约定区分。
- 让 projection 文件名本身表达主题和目标类型，而不是只叫 `sample-projection.json`。
- 让后续 review、升级和替换时，不需要猜"哪个文件才是当前生效版本"。

---

## 默认目录规范（扁平化）

对"consumer skill 专用"的 projection，默认放在消费方 skill 自己目录下的 `contracts/` 目录，采用扁平结构。

推荐结构：

```text
skills/<consumer-skill>/
  contracts/
    projection-index.json            # 投影元数据索引
    <domain-slug>.projection.json    # 完整投影内容（每个主题一个文件）
```

把这个结构展开成真实示例，建议长这样：

```text
skills/daily-news-digest/
  contracts/
    projection-index.json
    article-selection.projection.json
```

这里的含义是：

- `contracts/`：表明这里放的是被当前 skill 当作机器输入消费的 projection 契约。
- `projection-index.json`：索引文件，列出所有可用的投影及其元数据。
- `<domain-slug>.projection.json`：完整的投影内容文件，每个主题一个。

---

## projection-index.json 结构

索引文件的推荐 schema：

```json
{
  "schema_version": "2.0",
  "producer_skill": "ontology-extraction",
  "consumer_skill": "<skill-slug>",
  "status": "READY",
  "generated_by": "projection-pass",
  "topics": [
    {
      "domain_slug": "<domain-slug>",
      "projection_type": "workflow-contract",
      "file": "<domain-slug>.projection.json",
      "status": "READY",
      "open_questions": []
    }
  ]
}
```

索引文件的职责：

- 列出当前 skill 所消费的所有 projection 文件。
- 标明每个 topic 的状态、投影类型和文件名。
- 提供 producer/consumer 关系的元数据。
- 作为 runtime 发现投影的唯一入口。

---

## 为什么默认用 contracts，而不是 references

如果 projection 会被消费方 skill 当成实际输入边界，默认放 `contracts/`，不要放 `references/`。

区分规则：

- `contracts/`：表示当前 skill 会真正读取、依赖并执行这份 projection。
- `references/`：表示当前 skill 只把它当说明材料或 review 旁证。

因此，consumer skill 专用 projection 的默认落点是：

- 首选：`contracts/`
- 仅文档型引用：`references/`

不要把真正会被消费的 projection 放在 `examples/`。那会让"示例"和"生效契约"混在一起。

---

## 文件命名规范

推荐统一格式：

```text
<domain-slug>.projection.json
```

例如：

- `article-selection.projection.json`
- `skill-loading.projection.json`
- `risk-routing.projection.json`
- `visitor-reservation-and-review.projection.json`

命名规则说明：

- `<domain-slug>`：表达业务主题、任务域或概念边界。
- 固定后缀 `.projection.json`：表达这就是 projection contract，而不是普通 JSON。
- 投影类型（projection_type）记录在文件内部和 `projection-index.json` 中，不再编码到文件名里。

这样做的好处：

- 文件名更短，更易扫描。
- 一个主题如果将来从 `workflow-contract` 切换到 `domain-model`，不需要重命名文件。
- projection 类型信息在索引和文件内部都有记录，不会丢失。

---

## 一个 consumer skill 有多份 projection 时怎么放

如果同一个 consumer skill 需要消费多个主题的 projection，直接在 `contracts/` 目录下并列放置：

```text
contracts/
  projection-index.json
  article-selection.projection.json
  source-ranking.projection.json
  content-safety.projection.json
```

所有投影文件的元数据统一记录在 `projection-index.json` 的 `topics` 数组中。

---

## 是否需要版本号进文件名

默认不建议把版本号放进文件名。

推荐做法：

- 文件名保持稳定。
- 结构版本继续放在 JSON 内部的 `projection_version`。
- 内容演进通过 Git 历史追踪。

只有在同一目录下必须并存多个活跃版本时，才把版本号放进文件名末尾：

```text
<domain-slug>.v2.projection.json
```

例如：

- `skill-loading.v2.projection.json`

但这应视为过渡状态，而不是长期默认。

---

## 不推荐的组织方式

下面这些方式容易出问题：

### 1. 深层嵌套目录

例如：

```text
contracts/
  projections/
    ontology-extraction/
      article-selection/
        article-selection.prompt-constraint.projection.json
        README.md
        REVIEW.md
```

问题是层级太深，增加了导航和维护负担，且 `contract-index.json`、`README.md`、`REVIEW.md` 等配套文件使结构过重。

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

如果一个 skill 采用这套目录规范，建议它在自己的 `SKILL.md` 中只补稳定事实和消费边界，不要把索引中的 topic 评分、target view 评分、冲突规则或请求映射示例再手写一遍。

建议至少补上：

1. projection contract 的发现入口：`contracts/projection-index.json`。
2. 人工评审时的读取顺序：先读索引，再读对应的 `*.projection.json`。
3. 当前 skill 实际消费的字段或 view 边界。
4. blocked route、`open_questions` 和 `dropped_items` 的处理原则。

默认直接复用 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`，并只在 consumer `SKILL.md` 中补当前技能自己的字段边界、target view 边界或本地绑定路径。

---

## 最终推荐

如果你现在要开始给 consumer skill 落 projection，直接用下面这套：

```text
skills/<consumer-skill>/
  contracts/
    projection-index.json
    <domain-slug>.projection.json
```

这是当前最稳的默认方案，因为它同时兼顾了：

- 路径可发现性（只需找 `contracts/projection-index.json`）
- 目标类型可识别性（索引文件中标注 `projection_type`）
- 主题边界可分组性（每个主题一个文件，文件名即主题）
- 结构轻量性（2 层目录，无配套 README/REVIEW 负担）

如果没有特别强的例外需求，建议不要偏离这套默认结构。
