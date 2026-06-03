# scoring-judgement projection contract

本目录为 `evaluation-expert-consumer` 提供 `scoring-judgement` 主题，把"严苛打分判定"的 prompt 端规则落成可绑定的 prompt 约束。

主文件：

- `scoring-judgement.prompt-constraint.projection.json`

target view 说明：

- `prompt-constraint`：面向严苛判分的 prompt 端约束（红线触发、起评分 50、证据驱动加扣分、80 分以上极其罕见）。

主要触发信号：`打分`、`评分`、`严格评估`、`红线`、`扣分`、`起评分`、`judgement`、`strict scoring`、`red line`。

推荐读取顺序：

1. 按请求产物形态选读对应 projection 文件
2. 再读 `REVIEW.md` 看当前治理结论
3. 如需扩展或重排 view 优先级，再同步修改父级 `contract-index.json` 的评分和路由定义
