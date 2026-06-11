# Projection Consumption Guide

本文档说明：其他 skill 拿到 `ontology-projection` 产出的 projection 后，应该如何消费，而不是如何重新推导 ontology。

## Core Rule

projection 是下游 skill 的语义契约，不是可随意改写的参考文档。

下游 skill 应该：

- 读 `contract-index.json` 做 topic / view 选择
- 只消费自己支持的 projection 字段
- 保留 `mapping_policy`、`dropped_items`、`open_questions` 这些治理信息
- 遇到 blocked route 或未映射项时显式停止，而不是补造

## Discovery And Reading Order

本地绑定 consumer contract 时，默认从这里发现：

```text
contracts/projections/ontology_extraction/contract-index.json
```

人工审查顺序：

1. `contract-index.json`
2. 如需 topic 总览，再读 namespace `README.md`
3. selected `*.projection.json`

## Fields A Consumer Skill Should Care About

- `projection.projection_type`
- `projection.target_format`
- `projection.target_runtime`
- `projection.source_slice`
- `mapping_policy`
- `concept_mappings`
- `relation_mappings`
- `constraint_mappings`
- `prompt_projection`
- `delivery_artifacts`
- `dropped_items`
- `open_questions`

## How To Treat Uncertainty

- `open_questions` 非空：默认不能直接当成 READY 结果继续消费
- `unresolved_item_policy = block_or_escalate`：遇到未映射项时停止或升级
- 不确定字段名、枚举或外部绑定信息：如果 projection 已定义 fallback，就走 fallback；不要自己改写 contract 语义

## Thin Multi-View Guidance

同一 topic 下的多个 view 不应互相复制：

- `domain-model`：概念对象和关系
- `json-schema`：输入输出结构
- `prompt-constraint`：术语、禁区、澄清规则
- `workflow-contract`：步骤、流转、前置条件

如果 consumer skill 同时保留 4 个 view，应让每个 view 只承担这一层的最小职责。
