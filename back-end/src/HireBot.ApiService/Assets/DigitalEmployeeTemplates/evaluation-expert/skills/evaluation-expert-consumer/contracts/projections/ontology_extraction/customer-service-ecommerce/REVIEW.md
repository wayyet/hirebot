# customer-service-ecommerce projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`ontology_extraction`
- 当前主题：`customer-service-ecommerce`
- 当前文件：
  - `customer-service-ecommerce.workflow-contract.projection.json`

评审备注：

- 当前主题面向电商客服员工评估，target view 选择 `workflow-contract`，因为评估流程的核心交付物是带有红线 gating 的执行步骤序列。
- 文件命名采用 `<domain-slug>.<projection-type-short>.projection.json` 规则。
- 文件路径采用 `contracts/projections/<producer-skill>/<domain-slug>/` 规则。
- 已通过父级 `contract-index.json` 的 topic 评分与 within-topic bonus 接入路由。
- 红线触发条件（工具调用遗漏 / 流程合规 ≤30 / 交互质量 ≤30 / 功能完整性 ≤40）以 `workflow_precondition` 形式落地为 blocking 条件。

评审核对顺序：

1. 当前主题是否已在 `ontology_extraction` 中收缩成最小 slice，且 slice 已通过校验。
2. 本次 projection 是否已经明确选定单一主视图 `workflow-contract`，而不是把多种 target view 要求混写在一起。
3. `concept_mappings`、`relation_mappings`、`constraint_mappings` 是否都有显式映射，未隐式补造红线触发条件。
4. projection 是否已通过 schema 校验，关键字段完整、结构合法、编辑器诊断为零。
5. projection 仅在通过校验后才落入 consumer skill 的 `contracts/projections` 目录，且 `contract-index.json` 与本 README/REVIEW 已同步更新。
