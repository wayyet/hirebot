---
name: skill-generation
description: 根据 skill 阶段的 gap TODO 和会话上下文，抽取统一 SkillSpec，生成可直接运行的业务技能包（SKILL.md），写入当前沙箱 skills/ 目录。
metadata: {"openclaw":{"emoji":"🧩"}}
---

# Skill Generation

## 核心立场

你是技能生成执行者。你的工作是把已经明确的技能需求落成可运行的 SKILL.md。

输入来源：
- skill 阶段的 gap TODO（`stage=skill` + `gap_type=missing_skill_definition` / `incomplete_skill_fields`）
- 会话上下文（用户对能力的描述）
- 用户上传的技能文件（如有）
- 模板包 `ontology/` 中已抽取的本体（技能不能超出本体覆盖范围）

## 输入处理

从 gap TODO 的 `expected_state` 中提取：
- `skill_name`：技能名称
- `skill_description`：完整描述（触发情境+核心逻辑+输入依赖+输出形式）
- `trigger`：可识别的触发条件
- `expected_output`：输出形态和后续动作

如果 TODO 字段不够明确 → 不强行生成，标记为需要雇佣教练继续引导。

## 输出契约

- 产出写入 `skills/{skill_slug}/SKILL.md`
- 严格按 `references/generated-skill-template.md` 模板
- 生成后通过 `references/quality-checklist.md` 质量校验

## 约束

- 生成的技能不超出 ontology 覆盖的知识范围
- 生成的技能不违反 AGENTS.md 的行为边界
- 不生成空壳技能文件
- 不写凭据值到技能文件中

## 产出后

更新对应 gap TODO：
- `todo.update` → `status = done`
- `acceptance_evidence` = 产出的 SKILL.md 路径
