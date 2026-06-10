# runtime-schemas

评估运行期间产生/消费的运行时数据结构。**这些不是投影合同（projection contracts）。** 它们与技能放在一起，目的是让每个工作流步骤都能根据稳定的结构验证其输入/输出。

## 文件说明

| 模式 | 生产步骤 | 消费步骤 | 落盘路径 |
|---|---|---|---|
| `evaluation_context.schema.json` | STEP 6 `materializeEvaluationContext`（确定性） | STEP 4 扇出、STEP 5–8 | `./runs/<eval_id>/evaluation_context.json` |
| `enriched_test_case.schema.json` | STEP 2 `enrichTestCases`（确定性，始终运行） | STEP 3、STEP 4 | `./runs/<eval_id>/enriched-cases/<test_case_id>.json` |
| `run_plan.schema.json` | STEP 2.5 `planRun`（确定性；将每个场景的字面 shell 字符串冻结） | STEP 3（原文执行） | `./runs/<eval_id>/run_plan.json` |
| `execution_trace.schema.json` | STEP 3 `driveEmployeeOnScenario`（评估者-driver） | STEP 4 扇出 | `./runs/<eval_id>/traces/<test_case_id>.trace.json` |
| `metric_score.schema.json` | STEP 4 扇出（每对一次 LLM 调用） | STEP 5、STEP 7 | `./runs/<eval_id>/scores/<test_case_id>__<metric_code>.json` |
| `scenario_score.schema.json` | STEP 4（扇出后聚合器，确定性） | STEP 5、STEP 7 | `./runs/<eval_id>/scenarios/<test_case_id>.json` |
| `scenario_report.schema.json` | STEP 8 `buildScenarioReports`（LLM 合成，仅文字） | STEP 9 | `./runs/<eval_id>/reports/scenarios/<test_case_id>.report.json` |
| `evaluation_report.schema.json` | STEP 9 `buildOverallReport`（LLM 合成，仅文字） | 运行结束消费方 | `./runs/<eval_id>/reports/evaluation_report.json` |
| `runtime_driver.schema.json` | `runtime-drivers/<driver_id>/driver.json` 清单的编写者 | STEP 3 driver 加载器 | `./runtime-drivers/<driver_id>/driver.json`（不在 `./runs/` 下） |
| `simulator.schema.json` | `simulators/<simulator_id>/simulator.json` 清单的编写者 | STEP 3 simulator 配置加载器（宿主 Agent） | `./simulators/<simulator_id>/simulator.json`（不在 `./runs/` 下） |
| `simulator_decision.schema.json` | 宿主评估专家 Agent 的自有 LLM，每个客户轮次一次 | STEP 3（内存中消费；持久化到 `execution_trace.simulator_trail`） | 不单独持久化——嵌入在 trace 中 |

## 硬性规则

- 这些文件的内容**绝不能**写回 `contracts/projections/**`。合同层在运行时是只读的。
- `metric_score.schema.json` 故意不包含 `red_line_passed` 或 `pass_fail` 字段。红线判断是确定性的，只存在于 STEP 7 `redLineCheck` 中。LLM 只可以为 STEP 7 提交 `observed_signals`。
- `enriched_test_case.schema.json` 要求 `applicable_metrics` 非空：即使对于已随附指标绑定的完全策展测试用例，STEP 2 也会强制执行此规则。
- 由 STEP 1.5 `parseTestCases` 合成的测试用例**必须**持久化到 `./runs/<eval_id>/synthesized-cases/`，**不得**污染 `./test-cases/`（权威目录）。
- **报告分两层**：STEP 8 为每个测试用例生成一个 `ScenarioReport`；STEP 9 在所有 ScenarioReport 存在后生成恰好一个 `EvaluationReport`。STEP 9 **必须**通过路径链接场景报告，**不得**内联它们。
- **报告数字字段是拷贝，不是重新计算**：`ScenarioReport.metric_results[].score` 以及 `EvaluationReport.per_metric_final_scores` / `.dimension_scores` / `.overall_score` / `.red_line` / `.passed` 中的每个数字字段必须与上游 `MetricScore` / STEP 5 / STEP 6 / STEP 7 输出字节完全一致。STEP 8 / STEP 9 中的 LLM 只可以编写文字说明。
- **运行时 Driver 是纯协议适配器**：`./runtime-drivers/<driver_id>/` 下的每个 driver 必须发布根据 `runtime_driver.schema.json` 验证的 `driver.json`，并必须输出根据 `execution_trace.schema.json` 验证的 `ExecutionTrace`。Driver 不得包含评估逻辑，不得从任何 `*.projection.json` 中引用，且当 `runtime_driver.driver_id` 缺失时不得作为隐式回退——STEP 3 在此情况下快速失败。
- **用户模拟器是纯人格角色配置**：`./simulators/<simulator_id>/` 下的每个模拟器必须发布根据 `simulator.schema.json` 验证的 `simulator.json` 以及 `system_prompt.md` 模板。模拟器**不是子进程**——宿主评估专家 Agent 的自有 LLM（运行 STEP 1.5 / STEP 4 / STEP 8 / STEP 9 的同一大脑）每轮消费系统提示词，并生成根据 `simulator_decision.schema.json` 验证的 `SimulatorDecision`，然后才转发给 driver 并追加到 `simulator_trail`。模拟器目录**不得**包含可执行入口、不得对员工评分、不得提及指标、不得判断红线，也不得从任何 `*.projection.json` 中引用。当 `runtime_simulator.simulator_id` 无法解析时，STEP 3 快速失败——无隐式默认值。
- **STEP 3 是非对称执行的双角色**：`runtime_driver` 是长连接子进程（stdin/stdout 行分隔 JSON——Agent 到 driver 为 `{"action":"send",...}` / `{"action":"end",...}`，driver 到 Agent 为 `{"event":"ready"}` / `{"event":"evaluatee_turn",...}` / `{"event":"trace_written",...}`）。`runtime_simulator` 在宿主 Agent 自身的 LLM 内消费，**无子进程边界**。Driver **不得**生成客户文本；宿主 Agent（以模拟器身份行动）**不得**触碰协议连接。`turn_budget.hard_max_turns`（或 `evaluation_context.global_turn_cap`，取较小值）是**硬性上限**——`should_continue=true` 无法绕过它；一旦达到上限，宿主 Agent **必须**发出 `reason=max_turns_reached` 的 `end` 动作。

## 为什么此目录与 `contracts/projections/` 分开

- `contracts/projections/` 定义**永久为真的内容**（词汇表、约束、工作流形态）。
- `runtime-schemas/` 定义**单次运行期间流动的内容**（瞬态证据、分数、计划）。

混用会导致运行时数据漂移回合同，破坏可重现性。
