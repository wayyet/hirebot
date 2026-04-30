# workflow-contract-projection 评审说明

这份文档是 [workflow-contract-projection.json](workflow-contract-projection.json) 的人工评审配套说明。它展示了 `workflow_contract_projection` 应该怎样把 ontology slice 中的概念、关系和约束，转成工作流层真正会消费的输入、边和前置条件。

---

## 样例定位

- 角色：`READY` projection 参考样例。
- 目的：展示工作流契约不是把 ontology 语义压平后再拼 step 名，而是显式保留 workflow inputs、workflow edges 和 workflow preconditions。
- 最佳用法：当团队要把一份 slice 接到工作流编排、tool contract 生成或流程节点治理时，以它作为起点。

## 使用方式

```powershell
..\..\scripts\validate-projection.py .\workflow-contract-projection.json --review-mode
```

上面的命令适用于当前目录位于 `ontology_extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology_extraction\examples\ready\workflow-contract-projection.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

评审时，建议先对照 [sample.json](sample.json) 理解源 slice，再看 [workflow-contract-projection.json](workflow-contract-projection.json) 如何把配置对象、技能定义集合、来源层级、关系边和 gating 约束变成工作流契约。对任何改过的 projection 文件，都应继续用 [../../templates/PROJECTION_TEMPLATE.schema.json](../../templates/PROJECTION_TEMPLATE.schema.json) 做结构校验。

## 结构层状态

- 结构结果：`PASS`
- 评审状态：`READY`
- 当前解释：这是一个面向 workflow contract 目标类型的正向基线，适合拿来改造成真实编排契约。

## 为什么这个 projection 是 READY

- 它没有把 workflow contract 简化成“几个 step 名称”，而是把核心概念映射成可消费的 workflow inputs 和 shared enum。
- 它把 `R3` 明确投影成 `workflow_edge`，让 provenance 和依赖方向保留在编排层。
- 它把 `K1` 落成 `workflow_precondition`，避免工作流在未解决 source precedence 的情况下继续执行后续步骤。
- 它保持了保守的 mapping policy，没有依赖宽松假设或未决问题兜底。

## 重点看什么

1. `concept_mappings` 是否准确表达了工作流输入和步骤输出边界。
2. `relation_mappings` 是否把 ontology relation 投成了真实的 workflow edge，而不是注释。
3. `constraint_mappings` 是否把 gating 规则变成了可执行前置条件。
4. `delivery_artifacts` 是否明确指向了最终交付的 workflow contract 文件。

## 最适合怎么用

- 为工作流编排、step contract 和流程治理提供 ontology 驱动的契约基线。
- 给 workflow orchestrator 或 tool contract generator 提供一个结构清晰、可追溯的 projection 输入。
- 演示 `workflow_contract_projection` 在本规范里应该怎样处理 step 依赖和执行前置条件。
