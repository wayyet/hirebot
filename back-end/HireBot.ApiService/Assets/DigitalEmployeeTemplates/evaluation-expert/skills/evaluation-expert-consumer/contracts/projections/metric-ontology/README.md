# metric-ontology 投影契约（consumer 侧镜像）

本目录镜像 `metric-ontology` skill 生成的投影契约，由 `evaluation-expert-consumer` 消费。

## 目录结构

```
metric-ontology/
├── README.md
├── contract-index.json           # 路由选择索引
└── metric-library/
    ├── README.md
    ├── REVIEW.md
    ├── metric-library.metric-catalog.projection.json    # 指标注册表契约
    └── schemas/
        └── metric.schema.json    # 单个可热加载 .metric.json 文件的 JSON Schema
```

## 边界划分

- **契约层（本目录）**：声明评估指标定义的 schema、治理规则和路由。
- **数据层（`evaluation-expert-consumer/metrics/`）**：存放实际的 `*.metric.json` 实例（每个指标一个文件），可热加载。

数据层必须通过 `metric-library/schemas/metric.schema.json` 校验。契约层保持稳定；数据层才是业务方扩展的目标。

## 生产者 skill

名义上的生产者 skill 为 `metric-ontology`。若该 skill 后续作为独立 skill 创建，本目录将成为其 `contracts/projections/exports/` 输出的同步镜像。

## 主题列表

- **`metric-library`**：评估子指标的枚举目录，默认目标视图：`metric-catalog`。

## 触发信号

- `指标` / `指标库` / `指标清单` / `metric` / `catalog` / `挑选指标` / `evaluation dimension`
