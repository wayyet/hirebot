# Consumer Skill Projection Layout Guide

本文档定义 consumer skill 绑定本地 projection contract 时的目录、命名和阅读顺序。

## Recommended Layout

```text
skills/<consumer-skill>/
  SKILL.md
  contracts/
    projections/
      ontology_extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.domain-model.projection.json
          <domain-slug>.json-schema.projection.json
          <domain-slug>.prompt-constraint.projection.json
          <domain-slug>.workflow-contract.projection.json
```

## Directory Rules

- `contracts/`：这些 projection 会被当前 skill 当作正式输入消费。
- `projections/`：显式标识这里存放的是可路由的 projection contract。
- `ontology_extraction/`：当前 producer namespace。它是 contract 布局命名空间，不要求与 producer skill 目录名完全一致。
- `<domain-slug>/`：一个 topic 一个子目录，默认固定存放 4 个标准 view 的真实 projection 文件。

## File Rules

- `contract-index.json`：runtime discovery 入口，也是 topic/view 选择入口。
- `README.md`（namespace 级）：人工总览，用于说明当前有哪些 topic。
- `*.projection.json`：真实可消费的 projection 文件，不允许只写 stub 引用。

## Naming Rules

- 每个 topic 默认固定生成 4 个文件：
  - `<domain-slug>.domain-model.projection.json`
  - `<domain-slug>.json-schema.projection.json`
  - `<domain-slug>.prompt-constraint.projection.json`
  - `<domain-slug>.workflow-contract.projection.json`
- `workflow-contract` 作为 `default_target_view`，其余三个视图作为同 topic 的薄补充视图存在。

## Reading Order

1. 先读 `contracts/projections/ontology_extraction/contract-index.json`
2. 如需总览 topic 列表，再读 namespace 级 `README.md`
3. 默认先读 `<domain-slug>.workflow-contract.projection.json`
4. 再按需读取 `prompt-constraint`、`json-schema`、`domain-model`

## Why This Layout

- 与单 topic / 多 view 的运行时选择模型一致
- view 路径天然去重，不再混用旧平面命名
- topic 说明与 runtime 索引分层，避免 README 承载路由算法
- 删除旧式平面 contracts 语义
