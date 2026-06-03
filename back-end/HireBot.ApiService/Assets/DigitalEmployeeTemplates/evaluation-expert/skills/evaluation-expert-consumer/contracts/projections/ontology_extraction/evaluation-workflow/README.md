# evaluation-workflow projection contract

本目录为 `evaluation-expert-consumer` 提供 `evaluation-workflow` 主题，作为不依赖具体员工模板的通用评估工作流契约。

主文件：

- `evaluation-workflow.workflow-contract.projection.json`

target view 说明：

- `workflow-contract`：面向通用评估管线的执行流（红线检查 → 维度评分 → 综合聚合 → 报告生成），可在未指定具体员工模板时作为骨架使用。

主要触发信号：`评估流程`、`评估工作流`、`评估编排`、`evaluation workflow`、`evaluation pipeline`、`orchestration`。

推荐读取顺序：

1. 按请求产物形态选读对应 projection 文件
2. 再读 `REVIEW.md` 看当前治理结论
3. 如需扩展或重排 view 优先级，再同步修改父级 `contract-index.json` 的评分和路由定义
