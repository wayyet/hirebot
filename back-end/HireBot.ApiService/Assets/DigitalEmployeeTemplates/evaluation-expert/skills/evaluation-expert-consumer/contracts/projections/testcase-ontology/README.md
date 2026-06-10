# testcase-ontology 投影契约（consumer 侧镜像）

本目录镜像 `testcase-ontology` skill 生成的投影契约，由 `evaluation-expert-consumer` 消费。

## 目录结构

```
testcase-ontology/
├── README.md
├── contract-index.json
└── testcase-library/
    ├── README.md
    ├── REVIEW.md
    ├── testcase-library.test-case-catalog.projection.json
    └── schemas/
        └── test-case.schema.json
```

## 边界划分

- **契约层（本目录）**：声明评估测试用例的 schema、治理规则、发现规则和溯源策略。
- **数据层（`evaluation-expert-consumer/test-cases/`）**：存放实际的 `*.tc.json` 实例（每个用例一个文件），可热加载。

数据层必须通过 `testcase-library/schemas/test-case.schema.json` 校验。

## 生产者 skill

名义上的生产者 skill 为 `testcase-ontology`。若该 skill 后续作为独立 skill 创建，本目录将成为其导出结果的同步镜像。

## 主题列表

- **`testcase-library`**：评估测试用例的枚举目录（input + expected_output [+ 可选 applicable_metrics]）。

## 与 `STEP 1.5 parseTestCases` 的关系

自动合成的测试用例（当存在 SOP 或用户提供场景时）写入 **`./runs/<eval-id>/synthesized-cases/`**，**不**进入本目录。本目录只存放**人工精选 / 回归基线**用例。

## 触发信号

- `测试用例` / `test case` / `场景` / `scenario` / `SOP` / `期望行为` / `expected output`
