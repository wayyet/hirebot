# testcase-library projection review

- 当前状态：`READY`
- consumer skill：`evaluation-expert-consumer`
- producer skill：`testcase-ontology`
- 当前主题：`testcase-library`
- 当前文件：
  - `testcase-library.test-case-catalog.projection.json`
  - `schemas/test-case.schema.json`

## 评审备注

- 本主题与 `metric-ontology/metric-library` 形成两条独立 producer 链，分别承载"指标本体"与"用例本体"。
- **数据层**位于 `evaluation-expert-consumer/test-cases/`，每个 `*.tc.json` 是一个用例实例，必须通过本目录 schema 校验。
- 用例 `expected_output` 字段对评估结果有强约束：
  - `expected_tool_calls.criticality = must` 是 `tool_call_correctness` 红线判定的输入
  - `forbidden_behaviors` 命中即作为扣分证据
- `provenance.source` 是流程审计与回溯的核心字段，必须保留。
- 自动合成（STEP 1.5）的用例**不入本目录**，落到运行时 `./runs/<eval-id>/synthesized-cases/`，避免污染 golden set。

## 评审核对顺序

1. schema 是否覆盖所有热加载字段且 `additionalProperties: false`。
2. projection 是否声明了 `mapping_policy.unresolved_item_policy = block_or_escalate`。
3. 数据层至少有 1 个用例文件能通过 schema 校验。
4. `expected_tool_calls.criticality` 枚举与 metric `tool_call_correctness` 的红线规则对齐。
5. 与 `contract-index.json`、本目录 `README.md` 已同步更新。
