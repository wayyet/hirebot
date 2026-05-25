# Generated Consumer Projection Contract Template

Generated skills may be projection consumers when enough ontology projection information exists. Use this layout only for READY contracts or draft projection notes:

```text
skills/<skill_slug>/
  contracts/
    contract-index.json                                    # 投影选择与路由索引入口
    README.md                                              # 人工总览
    <domain-slug>/                                         # 每个主题一个子文件夹
      <domain-slug>.<target-view>.projection.json          # 投影内容
      README.md                                            # 主题说明
      REVIEW.md                                            # 评审记录
```

Minimum READY `contract-index.json` requirements:

- `$schema`: relative path to `docs/skill-projection-contract-index.schema.json`
- `producer_skill`: `"ontology-extraction"`
- `consumer_skill`: generated skill `name`
- `default_selection_policy`: select-by-status and block-on-questions policies
- `topic_scoring` and `target_view_scoring`: selection signal definitions
- `selection_algorithm`: deterministic two-phase selection
- `topics[]`: at least one topic with intent keywords, default target view, and views
- Each READY view's `path` points to an existing `<domain-slug>/<domain-slug>.<target-view>.projection.json`

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
