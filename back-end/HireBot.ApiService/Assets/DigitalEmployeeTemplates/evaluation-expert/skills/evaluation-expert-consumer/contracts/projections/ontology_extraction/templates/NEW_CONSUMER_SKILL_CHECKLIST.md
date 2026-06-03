# New Consumer Skill Checklist

复制 `CONSUMER_SKILL_PROJECTION_SECTION.md` 进入新的 consumer skill 后，使用本清单清理未解决的占位符、裁剪不支持的 projection 字段，并保持新 skill 与共享 consumer-skill 契约模型一致。

## 1. 替换必须的占位符

- 用真实的 skill 文件夹名替换 `name: <consumer-skill-name>`。
- 用具体且可被检索的描述替换 `description: <...>`，包括用户可能触发的语句。
- 用最终 emoji 替换 `"<emoji>"`，或在 skill 不需要 emoji 时移除整条 metadata 项。
- 替换标题 `# <consumer-skill-name>`。
- 用真实的触发短语替换 `When asked to <primary trigger phrases or user intents>:`。
- 替换 `Skill-Specific Constraints` 中的所有占位符。

## 2. 保留或删除 Projection 段

- 仅在新 skill 直接消费 `ontology_extraction` projection contract 时保留 `## Projection Contracts`。
- 如果 skill 不直接消费 projection，整段删除 `Projection Contracts`。
- 如果 skill 绑定到更窄的本地路径，用本地路径替换发现入口语句。

## 3. 裁剪不支持的 projection 类型

- 在 `Supported projection types` 中移除该 skill 不会消费的类型。
- 如果只支持单一类型，将该行重写为单一明确值，而不是菜单。

## 4. 裁剪不支持的 projection 字段

- 在 `Projection Consumption` 中移除该 skill 不会读取的字段。
- 在 `Supported projection fields beyond the shared minimum` 中显式列出额外字段，或删除该行。
- 如果 skill 不消费 prompt 端约束，不要保留 `prompt_projection`。
- 不要添加未在所选 projection 类型或本地契约中定义的字段。

## 5. 添加 skill 本地边界

- 显式列出支持的产物，例如 `evaluation_report`、`scoring_criteria`、`workflow_contract`、`metric_set`。
- 列出本地排除项，描述 skill 不应生成或决定的内容。
- 如果 skill 完全依赖 runtime 选定的 projection，明确说明。
- 如果 skill 可以回退到非 projection 输入，描述何时允许此回退。

## 6. 检查 description 可发现性

- 确保 `description` 包含会激活 skill 的用户语言。
- 让开头工作流匹配 `description`，不要漂移成通用模板腔调。
- 删除属于其他 skill 家族的触发短语。

## 7. 检查 References 与路径

- 如果新 skill 消费 projection contract，保留对 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`、`references/PROJECTION_CONSUMPTION_GUIDE.md`、`references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md` 的共享引用。
- 如果 skill 不是 projection consumer，删除未使用的引用。
- 如果新 skill 要存放本地绑定 contract，创建匹配的 `contracts/projections/ontology_extraction/<domain-slug>/` 目录。

## 8. 提交前最终评审

- 在新 `SKILL.md` 中搜索剩余的 `<...>` 占位符。
- 搜索由 `|` 分隔的菜单式占位值，并替换为最终支持的子集。
- 通读整文件，假设 runtime 已为它选好 projection；删除任何过度承诺的语句。
- 校验 markdown 格式，修复格式错误。

## 常见删除项

- 对非 prompt 类 skill 删除字段清单中的 `prompt_projection`。
- 删除约束清单中不支持的 projection 类型。
- 对非 consumer skill 整段删除 `Projection Contracts`。
- 如果 skill 在没有可用 projection 时必须阻断，删除通用的回退语言。
