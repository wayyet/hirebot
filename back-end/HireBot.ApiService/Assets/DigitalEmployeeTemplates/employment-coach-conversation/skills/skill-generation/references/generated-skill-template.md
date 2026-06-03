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
- `{{projection_contracts_section}}` 仅在 `contracts/projections/ontology_extraction/contract-index.json` 已真实落盘时才可展开为 Projection Contracts 章节；否则必须替换为空字符串。
- Keep projection consumption in generated business skills, not in `skill-generation` itself.
- If a generated skill cannot produce a READY projection contract, write draft notes and do not block the base skill write for that reason alone.
