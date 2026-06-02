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
1. 识别输入意图与关键字段。
2. 产出该技能负责的结果。
3. 命中 fallback 时输出映射建议、待确认项或阻断原因。

## 边界与不做
{{boundaries_markdown}}

## Projection Contracts

This skill may be augmented by bound projection contracts from `ontology_extraction`, discovered under `contracts/projections/**/contract-index.json`.

- Projection discovery, route selection, and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contracts/projections/ontology_extraction/contract-index.json`, optionally read the namespace `README.md`, and then the chosen `*.projection.json`.

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

- Keep frontmatter parser-friendly.
- Do not leave placeholder text in the final file.
- Keep projection consumption in generated business skills, not in `skill-generation` itself.
- If a generated skill cannot produce a READY projection contract, write draft notes and do not block the base skill write for that reason alone.
