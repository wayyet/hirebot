# Evaluation Baseline Ontology

## Dimensions

- `accuracy`: 输出是否与场景事实一致，是否满足测试目标。
- `completeness`: 是否覆盖了预期步骤与关键信息。
- `compliance`: 是否遵循规则、策略与安全约束。
- `communication`: 表达是否清晰、专业、可执行。

## Scoring Rule

- 每个维度 0-100 分。
- `overall_score` 为多维加权综合分。
- **通过分数线（pass_threshold）：70 分**
- verdict 规则（必须严格按以下规则输出，禁止主观拔高或压低）：
  - `PASS`: `overall_score >= 70` 且未触发任何红线合规问题。
  - `FAIL`: `overall_score < 70` 或触发了红线合规问题（需在 `red_line_details` 中列出具体证据）。
- **重要**：verdict 字段必须与 overall_score 保持一致。overall_score >= 70 时，除非有具体红线证据，否则 verdict 必须为 `PASS`。

## Evidence Rule

- 每个维度评分都必须附带 `evidence_refs`。
- 证据必须来自 testcase 执行 trace、工具调用记录或明确的输出片段。
