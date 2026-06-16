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

{{projection_contracts_section}}

## 对话示例
{{examples_markdown}}
```

Template rules:

- Keep frontmatter parser-friendly.
- Do not leave placeholder text in the final file.
- `{{projection_contracts_section}}` 仅在 `contracts/projections/ontology_extraction/contract-index.json` 已真实落盘时才可展开为 Projection Contracts 章节；若 contract 无法落盘，本轮 skill-generation 必须阻断，不得把基础 skill 视为成功结果。
- Keep projection consumption in generated business skills, not in `skill-generation` itself.
- If a generated skill consumes a projection with `open_questions`, generate a WARNING projection contract and surface those questions; do not downgrade to a base skill-only result.
