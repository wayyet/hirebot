# Example Consumer Skill

Use this file as a fully resolved example of a consumer `SKILL.md` after copying `CONSUMER_SKILL_SCAFFOLD.md` and applying `NEW_CONSUMER_SKILL_CHECKLIST.md`.

This example models a software-engineering consumer skill that consumes projection contracts to constrain code-generation and implementation work.

## Example: Software Developer Consumer Skill

```md
---
name: software-developer
description: Operates as an autonomous software engineer, capable of writing code, running tests, and managing git repositories.
metadata: {"openclaw":{"emoji":"💻"}}
---

# software-developer

When asked to "write code", "fix a bug", "implement a feature", or act as a "developer":

1) Identify the task boundary:
   - Identify the target files, languages, expected behavior, and validation path.
   - If the request is ambiguous, narrow the required implementation outcome before editing.

2) Load the right inputs:
   - Prefer user-provided files, nearby implementation context, and runtime-selected projection contracts.
   - If runtime selected a projection, treat it as the semantic boundary for concepts, relations, constraints, and permitted delivery artifacts.

3) Produce the deliverable:
   - Implement only the code, tests, schema changes, or workflow changes this skill is responsible for.
   - Preserve important terminology, constraint mappings, and downstream traceability that affect correctness.

4) Validate before finalizing:
   - Check that the final change still respects the selected scope, mapped constraints, and blocked items.
   - Surface uncertainty, blocked routes, or unmapped requirements instead of guessing.

## Projection Contracts

This skill may be augmented by bound `ontology-extraction` projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Use `../ontology-extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md` as the shared minimal template for consumer skills.
- Projection discovery, route selection, and prompt patching are handled by runtime rather than by manual rules in this file.
- For human review, read `contract-index.json` first, then the selected topic's `README.md` and `REVIEW.md`, and then the chosen `*.projection.json` file.

### Projection Consumption

- Read the selected projection before planning implementation details.
- Only consume the projection fields this skill actually supports: `concept_mappings`, `relation_mappings`, `constraint_mappings`, `delivery_artifacts`, `mapping_policy`, `open_questions`, and `dropped_items`.
- Treat the selected projection as authoritative for terminology, clarifications, dropped scope, and blocking conditions.

### Blocking Rules

- If route selection is blocked, ambiguous, or does not safely cover the request, surface that limitation instead of guessing.
- If `mapping_policy` requires `block_or_escalate`, or `open_questions` is non-empty, do not finalize the output before surfacing the issue.
- Do not recreate items listed in `dropped_items`.

## Skill-Specific Constraints

- Supported deliverables: code, tests, schema, workflow
- Supported projection types: domain-model, json-schema, workflow-contract
- Supported projection fields beyond the shared minimum: `concept_mappings.target_path`, `concept_mappings.target_kind`, `constraint_mappings.severity_mapping`, `delivery_artifacts.path`
- Local exclusions: do not invent unsupported APIs, do not bypass mapped constraints, and do not modify files outside the intended implementation scope

## References

- `../ontology-extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md`: shared minimal `Projection Contracts` section
- `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`: post-copy checklist for trimming unsupported fields and placeholders
- `../ontology-extraction/references/PROJECTION_CONSUMPTION_GUIDE.md`: how consumer skills should consume projection contracts
- `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`: where to place local bound projection files
```

## Why This Example Matters

- It replaces all scaffold placeholders.
- It trims the supported projection types to the subset that fits a code-generation consumer.
- It narrows the consumed projection fields instead of keeping the full generic list.
- It shows where to keep shared rules versus skill-local constraints.
