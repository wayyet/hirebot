# Generated Consumer Projection Contract Template

Generated skills may be projection consumers when enough ontology projection information exists. Use this layout for READY contracts or draft projection notes:

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

Minimum READY `contract-index.json` requirements:

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

Minimum READY projection document requirements:

- `projection`: contains `projection_id`, `projection_type`, `target_name`, `target_format`, `target_runtime`, and `source_slice`
- `mapping_policy`
- `concept_mappings`
- `relation_mappings`
- `constraint_mappings`
- `prompt_projection`
- `delivery_artifacts`
- `dropped_items`
- `open_questions` (empty for READY)

Always generate the 4 standard views for generated business skills. Keep `workflow-contract` as `default_target_view`, and keep the other 3 views thin rather than omitting them.

If there is not enough information to generate a READY contract, write draft notes or a WARNING summary under the generated skill's `references/` area, but do not mark the contract READY and do not block the base skill write for that reason alone.
