# scoring-judgement projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`ontology_extraction`
- 当前主题：`scoring-judgement`
- 当前文件：
  - `scoring-judgement.prompt-constraint.projection.json`

评审备注：

- 当前主题面向"严苛判分"，target view 选择 `prompt-constraint`，因为核心交付物是 prompt 端的判分政策与红线触发规则。
- 与 `metric-selection` 的差异：本主题不决定"用哪些维度"，只决定"在已选维度内如何严苛地打分"。
- 与 `customer-service-ecommerce` 的差异：本主题与员工模板解耦，只表达通用的判分规则。
- `prompt_assumption_policy = disallow_unmapped_terms` 强制：consumer 不得绕开声明的红线触发条件自创新红线。

评审核对顺序：

1. 当前主题是否已在 `ontology_extraction` 中收缩成最小 slice。
2. 本次 projection 是否明确选定单一主视图 `prompt-constraint`。
3. `prompt_projection.forbidden_assumptions` 是否覆盖"放水/老好人"反模式。
4. projection 是否已通过 schema 校验。
5. projection 与 `contract-index.json`、本目录 `README.md` 已同步更新。
