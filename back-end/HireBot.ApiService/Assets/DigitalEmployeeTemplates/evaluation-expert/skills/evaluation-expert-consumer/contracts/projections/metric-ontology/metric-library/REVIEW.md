# metric-library projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`metric-ontology`
- 当前主题：`metric-library`
- 当前文件：
  - `metric-library.metric-catalog.projection.json`
  - `schemas/metric.schema.json`

## 评审备注

- 本主题用 `metric-catalog` 视图承载"指标本体"，与 `ontology_extraction/metric-selection`（prompt-constraint：选取政策规则）形成职责分离：
  - `metric-ontology/metric-library`（本目录）：枚举的指标定义本体
  - `ontology_extraction/metric-selection`：选取规则（必须从 catalog 选、权重 sum=1 等）
- **数据层（`evaluation-expert-consumer/metrics/`）的实例必须通过本目录 schema 校验**，否则在 PRE 阶段 `loadMetricRegistry` 中会被剔除。
- `aggregation_strategy` 字段是新流程的核心：跨场景聚合在 STEP 5 deterministic 执行，**LLM 不参与汇总**。
- `red_line` 字段为可选；若一个指标声明了 red_line，会被 STEP 7 deterministic redLineCheck 收集为红线判定来源，**红线判定永远是 deterministic 的**。

## 评审核对顺序

1. schema 是否覆盖所有热加载字段且 `additionalProperties: false`。
2. projection 是否声明了 `mapping_policy.unresolved_item_policy = block_or_escalate`（保持与上层 ontology_extraction 治理一致）。
3. 数据层 `evaluation-expert-consumer/metrics/` 中至少有 1 个指标文件能通过 schema 校验。
4. `parent_dimension` 枚举与 `scoring-judgement` 中红线判定使用的 5 维度对齐。
5. 与 `contract-index.json`、本目录 `README.md` 同步更新。
