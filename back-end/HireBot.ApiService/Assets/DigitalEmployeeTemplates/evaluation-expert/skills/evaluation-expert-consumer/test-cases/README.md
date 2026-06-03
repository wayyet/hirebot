# test-cases/

热加载的**已策展（curated）**测试用例库。

> 这个目录是**评估前已经存在**的、可被多次复用的用例集合。
> 由 STEP 1.5 在线合成的临时用例**不**写入这里，而是落到 `./runs/<eval-id>/synthesized-cases/`（见下方"两类用例"）。

## 约定

- **一用例一文件**：`<test_case_id>.tc.json`，文件名（不含 `.tc.json`）必须等于 `test_case_id`
- **schema**：每个文件遵循 [`test-case.schema.json`](../contracts/projections/testcase-ontology/testcase-library/schemas/test-case.schema.json)（v2.0：simulator-driven 用例）
- **新增方式**：往本目录放新文件即可生效，不需要改契约或代码；STEP 1 在 `resolveEmployeeAndCheckTestCases` 里扫描本目录决定 `test_case_status`
- **覆盖路径**：默认本目录；运行时可通过环境变量 `EVALUATION_TEST_CASES_DIR` 指向其他路径
- **当前状态**：本目录是空的——首次集成 skill 时按团队真实业务往里放 `*.tc.json`。
  评估器在本目录找不到匹配用例时会自动进入 STEP 1.5（见下文）。

## 两类用例

| 类型 | 来源 | 落盘位置 | provenance.source | reliability |
|---|---|---|---|---|
| 策展用例 | 团队手工编排 / 回归基线 | `./test-cases/`（本目录） | `manual_curation` 或 `regression_baseline` | 通常 high |
| 合成用例 | STEP 1.5 在缺用例时按用户输入或 SOP 在线合成 | `./runs/<eval-id>/synthesized-cases/` | `user_provided_scenarios` / `synthesized_from_sop` / `mixed` | high / low / medium |

合成用例**永远不会**回写到本目录（K5 约束：`SynthesizedCasesIsolatedFromCatalog`）。这条边界保证策展集随时可被审阅、可被 git diff 追踪，不会被自动化污染。

## 字段速览（v2.0 simulator-driven）

| 字段 | 必填 | 说明 |
|---|---|---|
| `test_case_id` | ✓ | 机器标识，与文件名一致 |
| `version` | ✓ | semver |
| `applicable_roles` / `applicable_scenarios` | ✓ | 决定哪些岗位/场景下被选中执行；`'*'` 是 wildcard，不是字面量 |
| `input.opening_message` | ✓（推荐） | 顾客对员工说的第一句话；替代旧的 `user_message` |
| `input.customer_persona` | ★ | 客户人设（personality / patience_level / communication_style）。决定 simulator 的语气 |
| `input.initial_emotion` | ★ | 顾客 turn 0 的情绪（`angry` / `anxious` / `neutral` / ...） |
| `input.goal` | ★ | `primary` / `secondary` / `bottom_line` —— `bottom_line` 触发 `stop_reason=bottom_line_violated` |
| `input.context` | ★ | 员工可见的场景上下文（订单号、状态、时间戳等） |
| `input.stop_conditions` | ★ | `success` / `failure` / `deadlock` —— simulator 每轮判断是否达成 |
| `turn_budget.hard_max_turns` | – | 单场景对话硬上限（1–50，默认回退到 `evaluation_context.global_turn_cap=30`） |
| `expected_output.expected_tool_calls` | – | 期望工具调用，含 `must` / `should` / `may` 三档 criticality |
| `expected_output.expected_response_traits` | – | 期望话术应满足的特征清单 |
| `expected_output.forbidden_behaviors` | – | 禁止行为；命中即扣分证据 |
| `applicable_metrics` | – | 可预绑指标；缺失则 STEP 2 自动绑定 |
| `provenance` | – | 来源标记，详见下节 |
| `polarity` | – | 决策边界对偶覆盖标记（`positive` / `negative` / `boundary`），best practice |
| `paired_case_id` | – | 指向同一决策边界另一侧的用例 ID，与 `polarity` 配套使用 |

★ 在 v2.0 simulator-driven 流程中强烈建议提供；缺失会让 STEP 3 模拟器无依据。

## `provenance` 字段（K11 闭环）

```jsonc
{
  "source": "user_provided_scenarios",          // 必填
  "reliability": "high",                          // 合成用例必填
  "reliability_caveat": "...",                    // reliability=low 时必填
  "source_ref": "user_consultation_log[0]",       // 可选
  "generated_by": "evaluation-expert-consumer",   // 可选
  "generated_at": "2026-05-31T10:00:00Z"          // 可选
}
```

| `source` | 含义 | 典型 reliability |
|---|---|---|
| `user_provided_scenarios` | STEP 1.5 Tier 1：用户提供的真实业务场景 | `high` |
| `synthesized_from_sop` | STEP 1.5 Tier 2：用户拒绝提供，从 SOP 派生 | `low`（必带 `reliability_caveat`） |
| `mixed` | STEP 1.5 Tier 1+2：用户提供部分种子，SOP 扩展剩余 | `medium` |
| `manual_curation` | 团队手工编排的策展用例 | 通常 `high` |
| `regression_baseline` | 回归基线集 | 通常 `high` |
| `employee_sop` | 已废弃，仅为兼容旧文件保留；等价 `synthesized_from_sop` | `low` |

`reliability=low` 的用例会让 STEP 9 EvaluationReport 在 `open_questions` 里挂红字 caveat，并把语气从 "definitive" 降到 "indicative" / "preliminary"（K11）。

## `polarity` / `paired_case_id`（决策边界覆盖，best practice）

不是 blocking 约束，但强烈建议在涉及决策阈值（金额上限、时效窗口、品类限制、客户等级）的用例上成对出现：

```jsonc
// tc-refund-300-eligible.tc.json
{ "test_case_id": "tc-refund-300-eligible", "polarity": "positive", "paired_case_id": "tc-refund-899-handoff", ... }

// tc-refund-899-handoff.tc.json
{ "test_case_id": "tc-refund-899-handoff",  "polarity": "negative", "paired_case_id": "tc-refund-300-eligible", ... }
```

只有信息查询类、无决策边界的用例才允许单条不成对。

## 与契约的关系

本目录是**数据层**。**契约层**位于 [`contracts/projections/testcase-ontology/`](../contracts/projections/testcase-ontology/)。
schema 校验由 STEP 2 `enrichTestCases` 在加载时执行；不通过校验的文件会在 STEP 1 探测阶段就被 drop 并警告。
