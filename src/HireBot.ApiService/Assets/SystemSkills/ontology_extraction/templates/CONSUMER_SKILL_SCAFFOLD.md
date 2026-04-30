# Consumer Skill Scaffold Template

Use this file when creating a new consumer skill that is expected to consume `ontology_extraction` projection contracts.

This scaffold is intentionally minimal. Copy it into the new skill directory as `SKILL.md`, then replace placeholders and trim sections that do not apply.

## Recommended Companion Layout

If the new skill binds to local projection contracts, keep this layout next to the copied `SKILL.md`:

```text
<consumer-skill>/
  SKILL.md
  contracts/
    projections/
      ontology_extraction/
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
```

See `../references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md` for naming and placement rules.

## Copyable Minimal Skeleton

```md
---
name: <consumer-skill-name>
description: <one-sentence description of what this skill does and when to use it>
metadata: {"openclaw":{"emoji":"<emoji>"}}
---

# <consumer-skill-name>

When asked to <primary trigger phrases or user intents>:

1) Identify the task boundary:
   - Clarify the user goal, target output, and any missing scope constraints.
   - If the request is ambiguous, narrow the expected deliverable before proceeding.

2) Load the right inputs:
   - Prefer the user-provided files, workspace facts, or runtime-selected projection contract.
   - If runtime selected a projection, treat it as the semantic boundary for downstream work.

3) Produce the deliverable:
   - Generate only the output types this skill is responsible for.
   - Preserve important terminology, constraints, and traceability that affect correctness.

4) Validate before finalizing:
   - Check that the output still matches the selected scope and contract boundaries.
   - Surface uncertainty, blocked routes, or missing evidence instead of guessing.

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

## Skill-Specific Constraints

- Supported deliverables: <report | prompt | code | workflow | schema | other>
- Supported projection types: <prompt-constraint | workflow-contract | domain-model | json-schema>
- Supported projection fields beyond the shared minimum: <optional field list>
- Local exclusions: <anything this skill explicitly must not do>

## References

- `../ontology_extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md`: shared minimal `Projection Contracts` section
- `../ontology_extraction/references/PROJECTION_CONSUMPTION_GUIDE.md`: how consumer skills should consume projection contracts
- `../ontology_extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`: where to place local bound projection files
```

## Adaptation Checklist

- Replace every placeholder wrapped in `<...>`.
- Rewrite the trigger sentence so the skill is discoverable from its `description` and opening instructions.
- Remove unsupported projection types and unsupported field names.
- Add any skill-local constraints after the shared `Projection Contracts` section instead of editing routing rules into it.
- If the skill never consumes projection contracts directly, do not use this scaffold.

For a step-by-step post-copy review, use `NEW_CONSUMER_SKILL_CHECKLIST.md` in the same directory.
