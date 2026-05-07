# Generated Consumer Projection Contract Template

Generated skills may be projection consumers when enough ontology projection information exists. Use this layout only for READY contracts or draft projection notes:

```text
skills/<skill_slug>/
  contracts/
    projections/
      ontology-extraction/
        contract-index.json
        README.md
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
```

Minimum READY `contract-index.json` requirements:

- `producer_skill`: `ontology-extraction`
- `consumer_skill`: generated skill `name`
- `default_selection_policy.prefer_ready_only`: `true`
- `default_selection_policy.block_on_open_questions`: `true`
- At least one `topics[]` entry
- Each READY view path points to an existing `*.projection.json`

Minimum READY projection document requirements:

- `$schema`: relative path to `docs/skill-projection-document.schema.json`
- `template_type`: `ontology_projection`
- `projection_version`: `1.0.0`
- `mapping_policy.unresolved_item_policy`: `block_or_escalate`
- `prompt_projection.allowed_terms`
- `prompt_projection.forbidden_assumptions`
- `prompt_projection.reasoning_paths`
- `delivery_artifacts`
- `dropped_items`
- `open_questions`

Use `workflow-contract` by default for generated business skills because it maps capability execution into steps, gates, and failure handling. Use `prompt-constraint` when the skill is mainly guidance language. Use `json-schema` when the user explicitly asks for structured payload validation.

If there is not enough information to generate a READY contract, write draft notes or a WARNING summary under the generated skill's references/contracts area, but do not mark the contract READY and do not block the base skill write for that reason alone.
