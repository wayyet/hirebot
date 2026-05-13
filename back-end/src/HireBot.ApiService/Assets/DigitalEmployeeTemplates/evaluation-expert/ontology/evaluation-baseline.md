# Evaluation Baseline Ontology

## Dimensions

- `accuracy`: 输出是否与场景事实一致，是否满足测试目标。
- `completeness`: 是否覆盖了预期步骤与关键信息。
- `compliance`: 是否遵循规则、策略与安全约束。
- `communication`: 表达是否清晰、专业、可执行。

## Scoring Rule

- 每个维度 0-100 分。
- `overall_score` 为多维加权综合分。
- verdict 规则：
  - `PASS`: 综合分达到阈值，且无关键合规问题。
  - `FAIL`: 综合分未达阈值，或存在关键合规风险。

## Evidence Rule

- 每个维度评分都必须附带 `evidence_refs`。
- 证据必须来自 testcase 执行 trace、工具调用记录或明确的输出片段。
