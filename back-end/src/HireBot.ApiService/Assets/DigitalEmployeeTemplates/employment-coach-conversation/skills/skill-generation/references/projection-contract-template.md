# Generated Consumer Projection Contract Template

Generated skills may be projection consumers when enough ontology projection information exists. Use this layout for READY contracts or draft projection notes:

```text
skills/<skill_slug>/
  contracts/
    contract-index.json              # 投影选择与路由索引入口
    ontology-extraction/             # producer skill 命名空间
      <domain-slug>.projection.json  # 每个主题一个投影文件
```

Minimum READY `contract-index.json` requirements:

- `producer_skill`: `"ontology-extraction"`
- `consumer_skill`: generated skill `name`
- `default_selection_policy`: at least `prefer_ready_only` and `block_on_open_questions`
- `topics[]`: at least one topic object with:
  - `domain_slug`: domain slug
  - `intent_keywords`: array of intent keywords
  - `default_target_view`: target view name
  - `views[]`: at least one view with `target_view`, `projection_type`, `status`, and `path`
- Each view's `path` is relative to `contracts/`, e.g. `ontology-extraction/<domain-slug>.projection.json`
- Optionally include `topic_scoring`, `target_view_scoring`, `selection_algorithm` for full routing logic

Minimum READY projection document requirements:

- `projection_type`: e.g. `workflow_contract_projection`
- `source_slice`: object with `path` and `topic`
- `intended_consumers`: array containing consumer skill slug
- `concept_mappings`: non-empty array
- `relation_mappings`: array
- `constraint_mappings`: array
- `prompt_projection`: object with `allowed_terms`, `forbidden_assumptions`, `reasoning_paths`
- `delivery_artifacts`: array
- `dropped_items`: array
- `open_questions`: array (empty for READY)

Use `workflow-contract` by default for generated business skills because it maps capability execution into steps, gates, and failure handling. Use `prompt-constraint` when the skill is mainly guidance language. Use `json-schema` when the user explicitly asks for structured payload validation.

If there is not enough information to generate a READY contract, write draft notes or a WARNING summary under the generated skill's references/ area, but do not mark the contract READY and do not block the base skill write for that reason alone.
