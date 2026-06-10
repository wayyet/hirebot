# playbooks/

evaluation-expert-consumer 技能的分步操作流程。`SKILL.md` 是路由器；本目录包含详细内容。

| 文件 | 步骤 | 类型 |
|---|---|---|
| [`step-00-resolve-employee.md`](./step-00-resolve-employee.md) | PRE.A + STEP 0 | 确定性角色目录加载 + LLM+确认员工解析与规范化 |
| [`step-01-resolve-and-filter.md`](./step-01-resolve-and-filter.md) | STEP 1 | 确定性——按角色过滤指标到 `candidate_metrics` |
| [`step-1.2-curate-metrics.md`](./step-1.2-curate-metrics.md) | STEP 1.2 | LLM，有界+可审计——`selected_metrics = (candidate − removed) ∪ added` |
| [`step-1.5-consult-then-synthesize.md`](./step-1.5-consult-then-synthesize.md) | STEP 1.5 | LLM，条件触发——用户优先回退链；**负责 K21**（正:负 ≈ 80:20） |
| [`step-2.5-plan-run.md`](./step-2.5-plan-run.md) | STEP 2.5 | 确定性——将 `runs/<eval_id>/run_plan.json` 落盘，包含 STEP 3 使用的字面 shell 命令（K20） |
| [`step-03-driver-and-simulator-loop.md`](./step-03-driver-and-simulator-loop.md) | STEP 3 | 双角色——driver 子进程 + 宿主 LLM simulator（从 `run_plan.json` 原文读取命令，K19+K20） |
| [`step-04-fanout-scoring.md`](./step-04-fanout-scoring.md) | STEP 4 | LLM 并行扇出——每 (用例, 指标) 一次调用 |
| [`step-05-07-deterministic-rollup.md`](./step-05-07-deterministic-rollup.md) | STEP 5/6/7 | 确定性——聚合 + 汇总 + 红线检查 |
| [`step-09-overall-report.md`](./step-09-overall-report.md) | STEP 9 | LLM 综合——JSON + HTML 双格式输出 |
| [`k-rules.md`](./k-rules.md) | 所有步骤 | K1–K21 参考表，含一句话说明、所属步骤、严重级别、污染策略 |
| [`pre-flight-invariants.md`](./pre-flight-invariants.md) | PRE.A 前 | 宿主 Agent 在启动任何运行前必须验证的不变式 |
| [`tainted-run-lifecycle.md`](./tainted-run-lifecycle.md) | 任意步骤 | 运行何时变为污染状态、哪些内容继续、如何恢复 |

PRE（loadMetricRegistry）/ STEP 2 / STEP 8 足够简短，可内联在 `SKILL.md` 中，无需单独的操作手册。STEP 2.5 有自己的操作手册，因为它负责 K20 合同。

## 使用方法

1. 宿主 Agent 从 `SKILL.md`（路由器）开始。
2. 进入某一步骤时，读取该步骤对应的操作手册以获取完整操作流程。
3. K 规则在全文中以编号引用；权威查找请见 [`k-rules.md`](./k-rules.md)。

## 编写规则

- 每个操作手册是其步骤的**唯一信息源**。SKILL.md 链接到此，从不重复。
- 工作示例引用 `../runs/eval-soul-001/` / `eval-xiaofu-001/` / `eval-xiaofu-002/` 下的参考 fixtures。
- K 规则编号引用工作流合同 `metric-selection.workflow-contract.projection.json`（K1–K16）。反模式必须引用其违反的 K 规则。
