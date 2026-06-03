# metric-selection projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`ontology_extraction`
- 当前主题：`metric-selection`
- 当前文件：
  - `metric-selection.prompt-constraint.projection.json`

评审备注：

- 当前主题面向"评估前选取合适的评分维度并说明权重"，target view 选择 `prompt-constraint`。
- 与 `metric-catalog` 视图的差异：本视图给出 prompt 端的选取边界与禁忌，不替代显式的指标目录交付物。
- 文件命名采用 `<domain-slug>.<projection-type-short>.projection.json` 规则。
- `prompt_assumption_policy = disallow_unmapped_terms` 强制：consumer 不得自创未列出的维度名。

评审核对顺序：

1. 当前主题是否已在 `ontology_extraction` 中收缩成最小 slice。
2. 本次 projection 是否明确选定单一主视图 `prompt-constraint`。
3. `prompt_projection.allowed_terms` 是否覆盖默认 5 维度命名。
4. projection 是否已通过 schema 校验。
5. projection 与 `contract-index.json`、本目录 `README.md` 已同步更新。
