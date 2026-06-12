# Skill Generation Quality Checklist

Run these checks before writing a generated skill package.

## Input And Extraction

- [ ] Input type is classified as workorder, conversation, upload, or mixed.
- [ ] In workorder mode, every incoming skill item appears in the final result with `success`, `skipped`, or `failed`.
- [ ] In workorder mode, `skill_name`, `skill_description`, `trigger`, `expected_output`, `source`, and `generation_action` are preserved in source or extraction notes.
- [ ] Source summary is recorded in `references/source-digest.md`.
- [ ] Every capability has a source or extraction note.
- [ ] Ambiguous capabilities are listed as pending instead of silently finalized.

## SkillSpec

- [ ] `name` matches the incoming workorder `items[].name` verbatim (no hyphen/underscore conversion, no case folding, no reordering).
- [ ] `description` is non-empty and specific.
- [ ] At least one trigger exists.
- [ ] At least one capability exists.
- [ ] Every capability has inputs, outputs, and fallback.
- [ ] Boundaries include what the skill will not do.

## Projection Consumer Contract

- [ ] If valid source projections exist under `ontology/projections/<skill-slug>/`, `scripts/materialize-consumer-projection-contract.py` was run or the same algorithm was applied manually.
- [ ] If `projection_binding_confirmed: true` or `projection_contract_mode: "required"` is present in the payload, producer projection materialization is treated as mandatory rather than optional.
- [ ] If a READY projection contract is generated, generated `SKILL.md` includes the Projection Contracts section.
- [ ] If `SKILL.md` includes the Projection Contracts section, `contracts/projections/ontology_extraction/contract-index.json` exists and is non-empty.
- [ ] If a READY projection contract is generated, `contracts/projections/ontology_extraction/contract-index.json` exists with `producer_skill`, `consumer_skill`, and `topics[]`.
- [ ] If a READY projection contract is generated, the index `consumer_skill` matches generated skill `name`.
- [ ] If a READY projection contract is generated, every topic view's `path` points to an existing file under `contracts/projections/ontology_extraction/<domain-slug>/`.
- [ ] If a READY projection contract is generated, every topic includes exactly the 4 standard views: `domain-model`, `json-schema`, `prompt-constraint`, and `workflow-contract`.
- [ ] If a READY projection contract is generated, each projection document uses the consumer flat shape and contains top-level `projection_type`, `source_slice`, `intended_consumers`, `mapping_policy`, `prompt_projection`, `delivery_artifacts`, `dropped_items`, and `open_questions`.
- [ ] If `projection_binding_confirmed: true` or `projection_contract_mode: "required"` is present, `contracts/projections/ontology_extraction/contract-index.json` and the 4 standard view files must all exist; otherwise the run fails.
- [ ] If projection sources are recorded in `metadata.json` but contract files are absent, the run must be treated as blocked, and `references/quality-report.md` must explain the blocking reason.
- [ ] If ontology projection information is insufficient, draft notes are allowed only when projection binding was not confirmed; confirmed binding must fail instead of downgrading to a base skill-only result.
- [ ] `open_questions` is empty before marking a projection READY.

## Safety

- [ ] No plaintext token, secret, password, API key, connection string, or credential is written.
- [ ] No files outside `skills/<skill_slug>/` are written.
- [ ] Main agent behavior constraints are excluded from generated business skill content.

## Final Report

- [ ] `references/quality-report.md` records passed checks and any skipped checks.
- [ ] `technical_artifact` lists all generated files.
- [ ] Final result maps each skill item to artifacts, acceptance result, or readable failure reason.
- [ ] `user_summary` groups新增、更新、跳过、失败。
