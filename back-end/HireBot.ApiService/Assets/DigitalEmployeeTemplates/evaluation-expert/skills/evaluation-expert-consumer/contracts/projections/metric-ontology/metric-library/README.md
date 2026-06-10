# metric-library 主题

`metric-library` 主题提供**评估子指标的枚举目录**及其治理规则。

## 文件说明

- `metric-library.metric-catalog.projection.json` — 声明指标文件结构与发现方式的契约
- `schemas/metric.schema.json` — 单个可热加载指标文件的 JSON Schema
- `REVIEW.md` — 评审备注与当前状态

## 注册表的填充方式

1. 评估开始时（PRE 步骤 `loadMetricRegistry`），runtime 扫描 `evaluation-expert-consumer/metrics/*.metric.json`
2. 每个文件通过 `schemas/metric.schema.json` 校验
3. 通过校验的文件以 `metric_code` 为键加载到 `metric_registry`
4. 新增指标只需**向数据层投入一个新的 `*.metric.json` 文件**——无需修改代码或契约

## 触发信号（满足以下条件时选择本主题）

- 指标库 / 指标清单 / 可选指标 / catalog / metric registry
- 结合显式产物：`metric_code`、`.metric.json`、`applicable_roles`、`scoring_rubric`

## 推荐读取顺序

1. 读本 README
2. 读 `REVIEW.md` 了解治理状态
3. 读 projection JSON 获取正式契约
4. 读 `schemas/metric.schema.json` 了解单文件结构
