# Generated Consumer Projection Contract Template

Generated skills are projection consumers in bound mode. Use this layout for READY or WARNING consumer contracts materialized from producer ontology projections:

This template describes the local consumer contract under a generated business skill. It intentionally uses the consumer flat shape so runtime and human reviewers can read the same top-level fields directly. Source projection files may be either consumer flat shape or canonical ontology shape, but generated consumer contracts must be flat.

```text
skills/<skill_slug>/
  contracts/
    projections/
      ontology_extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.domain-model.projection.json
          <domain-slug>.json-schema.projection.json
          <domain-slug>.prompt-constraint.projection.json
          <domain-slug>.workflow-contract.projection.json
```

Minimum `contract-index.json` requirements:

- `producer_skill`: `"ontology_extraction"`
- `consumer_skill`: generated skill `name`
- `default_selection_policy`: at least `prefer_ready_only` and `block_on_open_questions`
- `topics[]`: at least one topic object with:
  - `domain_slug`: domain slug
  - `intent_keywords`: array of intent keywords
  - `default_target_view`: `workflow-contract`
  - `views[]`: all 4 standard views with `target_view`, `projection_type`, `status`, and `path`
- Each topic must include these 4 files under `contracts/projections/ontology_extraction/<domain-slug>/`:
  - `<domain-slug>.domain-model.projection.json`
  - `<domain-slug>.json-schema.projection.json`
  - `<domain-slug>.prompt-constraint.projection.json`
  - `<domain-slug>.workflow-contract.projection.json`

Minimum projection document requirements:

- `projection_id`
- `projection_type`
- `target_view`
- `target_name`
- `target_format`
- `target_runtime`
- `source_slice`
- `intended_consumers`
- `status`
- `mapping_policy`
- `concept_mappings`
- `relation_mappings`
- `constraint_mappings`
- `prompt_projection`
- `delivery_artifacts`
- `dropped_items`
- `open_questions` (empty for READY; non-empty for WARNING)

`source_slice.path` must be package-root-relative (for example, `ontology/<topic>.slice.json`) or otherwise resolvable from the projection file. Do not write skill-directory-relative paths such as `../../ontology/...`.

Always generate the 4 standard views for generated business skills. Keep `workflow-contract` as `default_target_view`, and keep the other 3 views thin rather than omitting them. Do not write stub references such as `{ "note": "...", "source_projection_path": "..." }`.

Preferred materialization command:

```bash
python "scripts/materialize-consumer-projection-contract.py" --workspace-root "<workspace_root>" --skill-slug "<skill-slug>" --skill-name "<skill-name>"
```

When running from outside the `skill-generation` skill directory, use the absolute path to this script instead.

If the source projection contains `open_questions`, generate a WARNING contract and carry those questions through every relevant view. If source projection files are missing, invalid, unwritable, or slug-mismatched, block the run; do not treat a base skill-only result as successful.
