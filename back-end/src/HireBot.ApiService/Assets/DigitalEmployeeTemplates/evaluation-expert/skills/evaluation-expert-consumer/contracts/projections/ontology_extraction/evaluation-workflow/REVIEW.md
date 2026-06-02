# evaluation-workflow projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`ontology_extraction`
- 当前主题：`evaluation-workflow`
- 当前文件：
  - `evaluation-workflow.workflow-contract.projection.json`

评审备注：

- 当前主题为 template-agnostic 的通用评估流，target view 选择 `workflow-contract`，因为通用流的核心交付物是步骤序列与红线 gating。
- 与 `customer-service-ecommerce` 的差异：本主题不绑定具体员工模板，dimension set 与 weights 来自 runtime 注入，而不是写死在 projection 内。
- 文件命名采用 `<domain-slug>.<projection-type-short>.projection.json` 规则。
- 红线 gating 仍以 `workflow_precondition` 形式落地为 blocking 条件。

评审核对顺序：

1. 当前主题是否已在 `ontology_extraction` 中收缩成最小 slice，且 slice 已通过校验。
2. 本次 projection 是否已经明确选定单一主视图 `workflow-contract`。
3. `concept_mappings`、`relation_mappings`、`constraint_mappings` 是否都有显式映射。
4. projection 是否已通过 schema 校验，关键字段完整、结构合法。
5. projection 与 `contract-index.json`、本目录 `README.md` 已同步更新。
