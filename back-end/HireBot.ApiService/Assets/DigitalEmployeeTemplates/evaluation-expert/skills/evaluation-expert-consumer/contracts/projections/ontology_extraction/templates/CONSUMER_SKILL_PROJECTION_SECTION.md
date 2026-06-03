# Consumer Skill Projection Section Template

把本文件作为消费 `ontology_extraction` projection contract 的 consumer `SKILL.md` 中 `Projection Contracts` 段的最小共享模板。

仅复制稳定的共享规则。每个 consumer skill 自己的 target view 边界、字段限制、本地绑定路径，应在复制后再补在 consumer skill 中。

```md
## Projection Contracts

This skill may be augmented by bound `ontology_extraction` projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Projection discovery, route selection, and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contract-index.json` first, then the selected topic's `README.md` and `REVIEW.md`, and then the chosen `*.projection.json` file.

### Projection Consumption

- Read the selected projection before planning implementation details.
- Only consume the projection fields and target views this skill actually supports, especially `concept_mappings`, `relation_mappings`, `constraint_mappings`, `prompt_projection`, `delivery_artifacts`, `mapping_policy`, `open_questions`, and `dropped_items`.
- Treat the selected projection as authoritative for terminology, clarifications, dropped scope, and blocking conditions.

### Blocking Rules

- If route selection is blocked, ambiguous, or does not safely cover the request, surface that limitation instead of guessing.
- If `mapping_policy` requires `block_or_escalate`, or `open_questions` is non-empty, do not finalize the output before surfacing the issue.
- Do not recreate items listed in `dropped_items`.
```

## 适配规则

- 保留发现入口语句，除非 consumer skill 绑定到更窄的本地 `contracts/projections/...` 路径。
- 如果 consumer skill 只消费部分字段，缩窄字段清单。
- blocking 行为应与 `mapping_policy`、`open_questions`、`dropped_items` 保持一致。
- 不要把 topic 评分、target view 评分、请求映射示例或 topic-local 路由提示拷贝进 consumer `SKILL.md`。
