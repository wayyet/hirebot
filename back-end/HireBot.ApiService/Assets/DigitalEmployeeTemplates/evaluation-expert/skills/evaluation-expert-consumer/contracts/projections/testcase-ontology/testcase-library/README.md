# testcase-library 主题

`testcase-library` 主题提供**评估测试用例的枚举目录**及其治理规则。

## 文件说明

- `testcase-library.test-case-catalog.projection.json` — 声明测试用例文件结构与发现方式的契约
- `schemas/test-case.schema.json` — 单个可热加载测试用例文件的 JSON Schema
- `REVIEW.md` — 评审备注与当前状态

## 用例集的填充方式

1. STEP 1 首先在精选目录（`evaluation-expert-consumer/test-cases/`）中查找与员工角色和场景匹配的文件
2. 若存在匹配，则直接使用
3. 若无匹配且存在 SOP / user_scenarios，**STEP 1.5 parseTestCases** 将自动合成新用例（遵循 SOP 优先回退链），写入 `./runs/<eval-id>/synthesized-cases/`
4. STEP 2（enrichTestCases）**始终执行**，确保每个选定用例都已绑定 `applicable_metrics`

## 溯源字段

每个测试用例携带一个 `provenance.source` 字段：

| 取值 | 含义 |
|---|---|
| `manual_curation` | 人工精选，用于回归 / golden set |
| `regression_baseline` | 固定基线，用于检测回归 |
| `employee_sop` | 从员工 SOP 文档自动合成 |
| `synthesized_from_user_scenarios` | SOP 缺失但用户提供场景时自动合成 |

只有 `manual_curation` 和 `regression_baseline` 存入目录；合成用例存放在运行时隔离目录中。

## 触发信号

- 测试用例 / 用例库 / test case / scenario / SOP-derived cases / regression set

## 推荐读取顺序

1. 读本 README
2. 读 `REVIEW.md`
3. 读 projection JSON
4. 读 `schemas/test-case.schema.json`
