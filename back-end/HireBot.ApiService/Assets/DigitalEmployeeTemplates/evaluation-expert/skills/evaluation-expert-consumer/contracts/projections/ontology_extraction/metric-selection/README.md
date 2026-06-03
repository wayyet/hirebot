# metric-selection projection contract

本目录为 `evaluation-expert-consumer` 提供 `metric-selection` 主题，把"为某个员工模板选取合适的评分维度并说明权重"落成可绑定的 prompt 约束。

主文件：

- `metric-selection.prompt-constraint.projection.json`

target view 说明：

- `prompt-constraint`：面向维度选取与权重说明的 prompt 端约束（允许的维度名、禁止补造的扣分理由、必须澄清的事项、推理路径）。

主要触发信号：`指标`、`评分维度`、`评估标准`、`维度权重`、`metric selection`、`scoring dimensions`、`weights`。

推荐读取顺序：

1. 按请求产物形态选读对应 projection 文件
2. 再读 `REVIEW.md` 看当前治理结论
3. 如需扩展或重排 view 优先级，再同步修改父级 `contract-index.json` 的评分和路由定义
