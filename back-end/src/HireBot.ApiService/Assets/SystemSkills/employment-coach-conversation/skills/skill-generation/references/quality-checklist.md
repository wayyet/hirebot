# Skill Generation Quality Checklist

Run these checks before writing a generated skill package.

## Input And Extraction

- [ ] Input type is classified as todo, conversation, upload, or mixed.
- [ ] In todo mode, every dispatched todo id appears in `todo_results` with `success`, `skipped`, or `failed`.
- [ ] In todo mode, `skill_name`, `skill_description`, `trigger`, `expected_output`, `source`, and `acceptance` are preserved in source or extraction notes.
- [ ] Source summary is recorded in `references/source-digest.md`.
- [ ] Every capability has a source or extraction note.
- [ ] Ambiguous capabilities are listed as pending instead of silently finalized.

## SkillSpec

- [ ] `name` is normalized and slug-safe.
- [ ] `description` is non-empty and specific.
- [ ] At least one trigger exists.
- [ ] At least one capability exists.
- [ ] Every capability has inputs, outputs, and fallback.
- [ ] Boundaries include what the skill will not do.

## Projection Consumer Contract

- [ ] If a READY projection contract is generated, generated `SKILL.md` includes the Projection Contracts section.
- [ ] If a READY projection contract is generated, `contracts/projections/ontology-extraction/contract-index.json` exists.
- [ ] If a READY projection contract is generated, the index `consumer_skill` matches generated skill `name`.
- [ ] If a READY projection contract is generated, every READY view points to an existing projection file.
- [ ] If a READY projection contract is generated, projection document contains `prompt_projection`, `delivery_artifacts`, `dropped_items`, and `open_questions`.
- [ ] If ontology projection information is insufficient, draft notes are written instead of a READY contract, and the base skill write is not blocked for that reason alone.
- [ ] `open_questions` is empty before marking a projection READY.

## Safety

- [ ] No plaintext token, secret, password, API key, connection string, or credential is written.
- [ ] No files outside `skills/<skill_slug>/` are written.
- [ ] Main agent behavior constraints are excluded from generated business skill content.

## Final Report

- [ ] `references/quality-report.md` records passed checks and any skipped checks.
- [ ] `technical_artifact` lists all generated files.
- [ ] `todo_results` maps each todo id to artifacts, acceptance result, or readable failure reason.
- [ ] `user_summary` groups新增、更新、跳过、失败。
