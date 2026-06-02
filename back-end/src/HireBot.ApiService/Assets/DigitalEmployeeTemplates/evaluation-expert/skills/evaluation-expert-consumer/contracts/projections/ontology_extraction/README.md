# evaluation-expert-consumer projection contracts

本目录是 `evaluation-expert-consumer` 作为 consumer skill 时，消费 `ontology_extraction` projection contract 的总入口。

选择入口：

- 机器选择：`contract-index.json`
- 人类总览：当前 README

当前结构：

```text
contracts/projections/ontology_extraction/<domain-slug>/
```

每个主题目录下至少包含：

- 1 个主 projection 文件
- `README.md`
- `REVIEW.md`

当前主题清单：

- `customer-service-ecommerce/`：电商客服员工评估的工作流契约
- `evaluation-workflow/`：通用评估工作流（红线检查 → 维度评分 → 报告生成）的工作流契约
- `metric-selection/`：评估维度选取的 prompt 约束（指标选取、维度权重、评分起点）
- `scoring-judgement/`：严苛打分判定的 prompt 约束（红线、起评分、证据驱动评分）

## Runtime 权威规则

当前目录下真正影响 runtime 行为的权威入口是 `contract-index.json`，而不是当前 README 的自然语言摘要。

可从 runtime 已消费字段中概括出的规则：

- topic / target view 选择由 runtime 按请求做评分解析。
- `prefer_ready_only = true` 时，只在 `READY` candidates 中继续选择；没有可用候选时阻断。
- target view 会结合显式输出信号、view 信号和 topic 默认 view bonus 做评分，而不是由 README 手工指定固定回退顺序。
- `block_on_open_questions = true` 时，命中的 projection 若仍有 blocking open questions，会被阻断而不是继续消费。
- 如果 route 结果歧义、文件缺失、结构无效或不存在安全匹配，runtime 会阻断，而不是伪造 contract。

## 人类阅读提示

推荐读取顺序：

1. 先读 `contract-index.json` 决定主题和 target view
2. 再按主题进入对应目录
3. 再读取 `<domain-slug>.<projection-type-short>.projection.json`
4. 最后查看本目录的 `REVIEW.md` 决定是否可直接消费

## 共享文档

- `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`：consumer skill 中 `Projection Contracts` 段的最小共享模板
- `templates/NEW_CONSUMER_SKILL_CHECKLIST.md`：复制模板后的清理清单
- `references/PROJECTION_CONSUMPTION_GUIDE.md`：consumer skill 如何消费 projection 的指南
- `references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：本地绑定 projection 的目录与命名规范
