# Generated Business Skill Template

Use this template when rendering the `SKILL.md` for a generated business skill. Replace every `{{...}}` placeholder before writing.

```markdown
---
name: {{name}}
description: {{description}} 当用户提到：{{triggers_joined}} 时触发。
metadata: {"openclaw":{"emoji":"{{emoji}}"}}
---

# {{display_name}}

## 适用场景
{{scenarios_markdown}}

## 能力清单
{{capabilities_markdown}}

## 处理流程
1. 意图识别与槽位补全
2. 根据匹配能力执行动作或给出指引
3. 返回结果、失败原因或下一步建议
4. 如命中 projection blocking condition，先说明阻断原因，不继续编造结果

## 边界与不做
{{boundaries_markdown}}

## Projection Contracts

This skill may be augmented by bound `ontology-extraction` projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Projection discovery, route selection, and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contracts/projections/ontology-extraction/contract-index.json` first, then the selected topic's `README.md` and `REVIEW.md`, and then the chosen `*.projection.json` file.

### Projection Consumption

- Read the selected projection before planning implementation details.
- Only consume the projection fields and target views this skill actually supports, especially `concept_mappings`, `relation_mappings`, `constraint_mappings`, `prompt_projection`, `delivery_artifacts`, `mapping_policy`, `open_questions`, and `dropped_items`.
- Treat the selected projection as authoritative for terminology, clarifications, dropped scope, and blocking conditions.

### Blocking Rules

- If route selection is blocked, ambiguous, or does not safely cover the request, surface that limitation instead of guessing.
- If `mapping_policy` requires `block_or_escalate`, or `open_questions` is non-empty, do not finalize the output before surfacing the issue.
- Do not recreate items listed in `dropped_items`.

## 对话示例
{{examples_markdown}}
```

Template rules:

- Keep frontmatter parser-friendly. Prefer single-line `description` in this repository unless a target runtime is known to support multiline YAML.
- Do not leave placeholder text in the final file.
- Keep projection consumption in generated skills, not in `skill-generation` itself.
- If a generated skill cannot produce a READY projection contract, write draft notes and do not block the base skill write for that reason alone.
