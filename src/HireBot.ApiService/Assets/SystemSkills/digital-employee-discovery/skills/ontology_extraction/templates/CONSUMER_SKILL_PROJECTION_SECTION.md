# Consumer Skill Projection Section Template

Use this file as the canonical minimal `Projection Contracts` section for consumer `SKILL.md` files that consume `ontology_extraction` projection contracts.

Keep only stable shared rules in the copied section. Put any skill-specific target-view limits, field restrictions, or local binding paths in the consumer skill after copying this template.

If you need a full starter `SKILL.md` instead of only this section, use `CONSUMER_SKILL_SCAFFOLD.md` in the same directory.

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

## Adaptation Rules

- Keep the discovery sentence unless the consumer skill binds to a narrower local `contracts/projections/...` path.
- Narrow the supported fields list if the consumer skill only consumes a subset.
- Keep blocking behavior aligned with `mapping_policy`, `open_questions`, and `dropped_items`.
- Do not copy topic scoring, target-view scoring, request mapping examples, or topic-local routing hints into consumer `SKILL.md` files.
