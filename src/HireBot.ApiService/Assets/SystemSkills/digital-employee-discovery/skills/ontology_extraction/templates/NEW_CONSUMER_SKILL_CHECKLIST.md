# New Consumer Skill Checklist

Use this checklist after copying `CONSUMER_SKILL_SCAFFOLD.md` into a new consumer skill directory.

The goal is to remove unresolved placeholders, trim unsupported projection fields, and keep the new skill aligned with the shared consumer-skill contract model.

## 1. Replace Required Placeholders

- Replace `name: <consumer-skill-name>` with the real skill folder name.
- Replace `description: <...>` with a concrete, discoverable sentence that includes likely user trigger phrases.
- Replace `"<emoji>"` with the final emoji or remove the metadata entry if the skill should not define one.
- Replace the heading `# <consumer-skill-name>`.
- Replace `When asked to <primary trigger phrases or user intents>:` with the actual trigger line.
- Replace every placeholder in `Skill-Specific Constraints`.

## 2. Keep or Delete the Projection Section

- Keep `## Projection Contracts` only if the new skill directly consumes `ontology-extraction` projection contracts.
- If the skill does not consume projection contracts directly, delete the entire `Projection Contracts` section.
- If the skill consumes a narrower local binding path, replace the generic discovery sentence with the local path.

## 3. Trim Unsupported Projection Types

- In `Supported projection types`, remove every type the skill will not consume.
- Do not leave all four defaults unless the skill truly supports all of them.
- If only one type is supported, rewrite the line as a single explicit value instead of a menu.

## 4. Trim Unsupported Projection Fields

- In `Projection Consumption`, remove fields the skill does not read.
- In `Supported projection fields beyond the shared minimum`, either list the extra fields explicitly or delete the line.
- Do not keep `prompt_projection` in the shared field list unless the skill actually consumes prompt-facing constraints.
- Do not add fields that are not defined by the selected projection type or local contract.

## 5. Add Skill-Local Boundaries

- Add explicit supported deliverables such as `report`, `prompt`, `workflow`, `schema`, or `code`.
- Add local exclusions describing what the skill must not generate or decide.
- If the skill depends on runtime-selected projection only, say so clearly.
- If the skill can fall back to non-projection inputs, describe when that fallback is allowed.

## 6. Check Description Discoverability

- Ensure the `description` contains the user language that should activate the skill.
- Make the opening workflow match the description instead of drifting into a generic template tone.
- Remove any trigger phrases that belong to a different skill family.

## 7. Check References and Paths

- Keep the shared references to `../ontology-extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md`, `../ontology-extraction/references/PROJECTION_CONSUMPTION_GUIDE.md`, and `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md` if the new skill consumes projection contracts.
- Delete unused references if the skill is not a projection consumer.
- If the new skill will store local bound contracts, create the matching `contracts/projections/ontology-extraction/<domain-slug>/` layout.

## 8. Final Review Before Commit

- Search the new `SKILL.md` for any remaining `<...>` placeholders.
- Search for menu-style placeholder values separated by `|` and replace them with the final supported subset.
- Read the whole file once as if runtime had selected a projection for it; remove any sentence that overclaims support.
- Compare the result with `EXAMPLE_CONSUMER_SKILL.md` if you want a fully resolved end state to match against.
- Validate the markdown file and fix any formatting errors.

## Common Deletions

- Delete `prompt_projection` from the field list for non-prompt skills.
- Delete unsupported projection types from the constraint list.
- Delete the entire `Projection Contracts` section for non-consumer skills.
- Delete generic fallback language if the skill must block when no projection is available.
