# Generated Consumer Projection Contract Template

Generated skills may be projection consumers when enough ontology projection information exists. Use this layout only for READY contracts or draft projection notes:

```text
skills/<skill_slug>/
  contracts/
    projection-index.json            # 投影元数据索引
    <domain-slug>.projection.json    # 完整投影内容
```

Minimum READY `projection-index.json` requirements:

- `schema_version`: `"2.0"`
- `producer_skill`: `"ontology-extraction"`
- `consumer_skill`: generated skill `name`
- `status`: `"READY"`
- `generated_by`: `"projection-pass"`
- At least one `topics[]` entry
- Each READY topic's `file` points to an existing `<domain-slug>.projection.json`

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
