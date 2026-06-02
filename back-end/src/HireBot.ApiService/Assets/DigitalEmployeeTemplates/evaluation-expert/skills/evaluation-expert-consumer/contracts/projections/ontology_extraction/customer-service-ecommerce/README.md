# customer-service-ecommerce projection contract

本目录为 `evaluation-expert-consumer` 提供 `customer-service-ecommerce` 主题，把电商客服员工评估场景落成可绑定的 projection 主题。

主文件：

- `customer-service-ecommerce.workflow-contract.projection.json`

target view 说明：

- `workflow-contract`：面向电商客服评估的执行流，覆盖红线检查、维度评分、闭环确认、工单创建等关键步骤与前置条件。

主要触发信号：`客服`、`售后`、`退货`、`投诉`、`电商`、`工单`、`customer service`、`ecommerce`。

推荐读取顺序：

1. 按请求产物形态选读对应 projection 文件
2. 再读 `REVIEW.md` 看当前治理结论
3. 如需扩展或重排 view 优先级，再同步修改父级 `contract-index.json` 的评分和路由定义
