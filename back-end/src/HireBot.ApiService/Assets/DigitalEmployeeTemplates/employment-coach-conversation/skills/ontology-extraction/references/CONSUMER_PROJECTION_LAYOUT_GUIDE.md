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

## 默认目录规范

对"consumer skill 专用"的 projection，默认放在消费方 skill 自己目录下的 `contracts/` 目录。

推荐结构：

```text
skills/<consumer-skill>/
  contracts/
    contract-index.json                                       # 投影选择索引入口（运行时发现）
    README.md                                                 # 人工总览
    <domain-slug>/                                            # 每个主题一个子文件夹
      <domain-slug>.<target-view>.projection.json             # 投影内容文件
      README.md                                               # 主题说明
      REVIEW.md                                               # 评审记录
```

把这个结构展开成真实示例：

```text
skills/daily-news-digest/
  contracts/
    contract-index.json
    README.md
    article-selection/
      article-selection.workflow-contract.projection.json
      README.md
      REVIEW.md
    source-ranking/
      source-ranking.domain-model.projection.json
      source-ranking.prompt-constraint.projection.json
      README.md
      REVIEW.md
```

这里的含义是：

- `contracts/`：表明这里放的是被当前 skill 当作机器输入消费的 projection 契约。
- `contract-index.json`：选择索引文件，定义 topic / target view 的评分规则、选择算法和路由定义，是 runtime 发现投影的唯一入口。
- `README.md`（contracts 级）：人工总览，说明当前有哪些主题、推荐读取顺序。
- `<domain-slug>/`：每个主题一个独立子文件夹，包含该主题下的所有 target view 投影。
- `<domain-slug>.<target-view>.projection.json`：投影内容文件，文件名同时包含主题和视图类型，便于在文件夹内快速区分多个视图。
- `README.md`（主题级）：主题说明，列出当前目录文件与推荐读取顺序。
- `REVIEW.md`：评审记录，记录当前主题下各投影的 READY/WARNING 状态、评审备注与核对顺序。

---

## contract-index.json 结构

选择索引文件是 runtime 发现投影的入口，同时承载 topic / target view 选择逻辑。它的结构包含：

- `producer_skill` / `consumer_skill`：生产者与消费者标识
- `default_selection_policy`：全局选择策略（READY only、open questions 阻断、view 回退顺序）
- `topic_conflict_resolution`：多 topic 冲突时的消解规则
- `topic_scoring`：topic 维度的评分信号与权重
- `target_view_scoring`：target view 维度的评分信号与权重
- `selection_algorithm`：确定的二阶段选择算法（先选 topic，再选 view）
- `target_view_hints`：各视图的触发信号与适用场景
- `topics`：topic 列表，每项包含 domain_slug、intent_keywords、default_target_view、example_requests、views（具体投影文件路径与状态）

索引文件的职责：

- 列出当前 skill 所消费的所有 projection 文件及其路径。
- 标明每个 topic 的状态、可用的 target view 及对应文件。
- 定义 topic 和 target view 的选择规则，供 runtime 做评分解析。
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
<domain-slug>.<target-view>.projection.json
```

其中 `<target-view>` 取值为：`domain-model`、`json-schema`、`prompt-constraint`、`workflow-contract`。

例如：

- `article-selection.workflow-contract.projection.json`
- `skill-loading.domain-model.projection.json`
- `skill-loading.json-schema.projection.json`
- `risk-routing.prompt-constraint.projection.json`
- `memory-session.workflow-contract.projection.json`

命名规则说明：

- `<domain-slug>`：表达业务主题、任务域或概念边界。
- `<target-view>`：表达该投影的目标视图类型，与文件内部的 `projection_type` 保持一致。
- 固定后缀 `.projection.json`：表达这就是 projection contract，而不是普通 JSON。

这样做的好处：

- 同一主题下可能并存多个 target view，文件名中的视图类型让文件列表一目了然。
- `<domain-slug>` 保持稳定，新增视图只需添加新文件，不需要重命名已有文件。
- projection 类型信息在文件名、文件内部和 `contract-index.json` 的 views 路径中三重记录，不会丢失。

---

## 一个 consumer skill 有多份 projection 时怎么放

如果同一个 consumer skill 需要消费多个主题的 projection，每个主题一个子文件夹：

```text
contracts/
  contract-index.json
  README.md
  article-selection/
    article-selection.workflow-contract.projection.json
    README.md
    REVIEW.md
  source-ranking/
    source-ranking.domain-model.projection.json
    source-ranking.prompt-constraint.projection.json
    README.md
    REVIEW.md
  content-safety/
    content-safety.prompt-constraint.projection.json
    README.md
    REVIEW.md
```

同一主题多视图时，所有视图文件放在同一个主题文件夹下。所有投影文件的元数据统一记录在 `contract-index.json` 的 `topics[].views[]` 中。

---

## 是否需要版本号进文件名

默认不建议把版本号放进文件名。

推荐做法：

- 文件名保持稳定。
- 结构版本继续放在 JSON 内部的 `projection_version`。
- 内容演进通过 Git 历史追踪。

只有在同一主题下必须并存同一视图的多个活跃版本时，才把版本号放进文件名：

```text
<domain-slug>.<target-view>.v2.projection.json
```

例如：

- `skill-loading.domain-model.v2.projection.json`

但这应视为过渡状态，而不是长期默认。

---

## 不推荐的组织方式

下面这些方式容易出问题：

### 1. 将投影类型藏入文件内部但文件名不体现

例如在只有一个主题的 consumer skill 中把所有视图都命名为：

```text
<domain-slug>.projection.json
```

问题是当同一主题扩展出多个 target view 时，仅靠文件名无法区分视图类型，必须打开每个文件才能判断。

### 2. 在 contracts 下再嵌套 projections/<producer>/ 层级

例如：

```text
contracts/
  projections/
    ontology-extraction/
      <domain-slug>/
        ...
```

这额外增加了两层无信息量的中间目录（`projections/`、`ontology-extraction/`），使路径变长且不增加任何区分度。consumer skill 只需要知道自己在消费 projection，不需要在路径中重复 producer skill 名。

### 3. 用 sample、final、new、latest 之类的词命名

例如：

- `final-projection.json`
- `latest-projection.json`
- `new-workflow.json`

这些名字一旦时间过去就会失真。

### 4. 在文件名里重复 skill 名

例如：

```text
daily-news-digest.article-selection.daily-news-digest.prompt-constraint.projection.json
```

skill 名已经在路径里，重复写进文件名只会增加噪音。

---

## 对 consumer skill 的最小要求

如果一个 skill 采用这套目录规范，建议它在自己的 `SKILL.md` 中只补稳定事实和消费边界，不要把索引中的 topic 评分、target view 评分、冲突规则或请求映射示例再手写一遍。

建议至少补上：

1. projection contract 的发现入口：`contracts/contract-index.json`。
2. 人工评审时的读取顺序：先读 `contracts/contract-index.json` 确定主题和 target view，再进入对应主题子文件夹，读 `<domain-slug>.<target-view>.projection.json`，最后查看 `REVIEW.md` 决定是否可直接消费。
3. 当前 skill 实际消费的字段或 view 边界。
4. blocked route、`open_questions` 和 `dropped_items` 的处理原则。

默认直接复用 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`，并只在 consumer `SKILL.md` 中补当前技能自己的字段边界、target view 边界或本地绑定路径。

---

## 最终推荐

如果你现在要开始给 consumer skill 落 projection，直接用下面这套：

```text
skills/<consumer-skill>/
  contracts/
    contract-index.json
    README.md
    <domain-slug>/
      <domain-slug>.<target-view>.projection.json
      README.md
      REVIEW.md
```

这是当前最稳的默认方案，因为它同时兼顾了：

- 路径可发现性（只需找 `contracts/contract-index.json`）
- 视图类型可识别性（文件名中直接体现 `<target-view>`，文件夹内多视图一目了然）
- 主题边界可分组性（每个主题一个子文件夹，内含该主题的所有视图文件）
- 可评审性（每个主题有独立的 `README.md` 和 `REVIEW.md`，支持人工治理）
- 结构适度性（去掉了 `projections/ontology-extraction/` 中间层，但保留了主题文件夹用于分组）

如果没有特别强的例外需求，建议不要偏离这套默认结构。
