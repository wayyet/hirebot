# json-schema-projection 评审说明

这份文档是 [json-schema-projection.json](json-schema-projection.json) 的人工评审配套说明。它展示了同一份 `READY` slice 如何被稳定投影为面向 JSON Schema 的交付契约，而不是只保留一个偏 codegen 的通用 projection。

---

## 样例定位

- 角色：`READY` projection 参考样例。
- 目的：展示 `json_schema_projection` 在本规范下应该如何保留概念、关系、约束和 traceability。
- 最佳用法：当团队需要把 ontology slice 落成配置校验 schema、数据交换 schema 或机器可读契约时，以它作为起点。

## 使用方式

```powershell
..\..\scripts\validate-projection.py .\json-schema-projection.json --review-mode
```

上面的命令适用于当前目录位于 `ontology_extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology_extraction\examples\ready\json-schema-projection.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

评审时，建议先对照 [sample.json](sample.json) 理解源 slice，再看 [json-schema-projection.json](json-schema-projection.json) 如何把核心概念收敛成 schema 定义、把关系转成依赖规则、把约束转成 schema rule。对任何改过的 projection 文件，都应继续用 [../../templates/PROJECTION_TEMPLATE.schema.json](../../templates/PROJECTION_TEMPLATE.schema.json) 做结构校验。

## 结构层状态

- 结构结果：`PASS`
- 评审状态：`READY`
- 当前解释：这是一个面向 JSON Schema 目标类型的正向基线，而不是泛化的占位 projection。

## 为什么这个 projection 是 READY

- 它绑定到同一份 `READY` 源 slice，没有绕开 source slice 直接发明结构化契约。
- 它把 `SkillsConfig`、`SkillDefinition` 和 `SkillSource` 都落到了明确的 schema 定义路径，而不是只写一句“可生成 schema”。
- 它保留了 relation 和 constraint 的显式映射，避免把 precedence 语义静默吞进普通字段说明里。
- 它没有依赖 `open_questions` 或 `dropped_items` 才成立，因此适合作为一个保守且可复用的 READY 基线。

## 重点看什么

1. `concept_mappings` 是否把核心概念映射成了可定位的 schema 定义。
2. `relation_mappings` 是否仍保留了 `SkillDefinition -> SkillSource` 的依赖关系。
3. `constraint_mappings` 是否把来源优先级落成了可执行或可校验的 schema 规则。
4. `delivery_artifacts` 是否准确描述了最终想交付的 JSON Schema 文件。

## 最适合怎么用

- 为配置文件和导入导出格式提供 ontology 驱动的 schema 基线。
- 给 schema generator 或 runtime validator 提供一个忠实、可追溯的 projection 输入。
- 演示 `json_schema_projection` 不只是改 `projection_type`，而是连目标路径、规则映射和交付物都要一起特化。
