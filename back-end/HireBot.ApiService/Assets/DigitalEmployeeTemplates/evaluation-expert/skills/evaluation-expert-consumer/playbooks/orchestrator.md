# Orchestrator — 三段式评估调度状态机

**角色**：轻量调度器，不持有任何业务上下文，只负责触发和监控三个子 Agent 的生命周期。  
**版本**：v1.0.0（与 multi-agent 架构同步引入）  
**权威依据**：`agent-boundaries.md`；K 规则 K1–K21 不变，由各子 Agent 自行遵守。

---

## 概览

```
Orchestrator（状态机）
    │
    ├─► Prep Agent   — PRE.A + STEP 0~2.5（一次性，做完即退出）
    │
    ├─► Run Agent × N — STEP 3+4 per TC（每 TC 一个实例，可选并行）
    │
    └─► Report Agent  — STEP 5~10（汇总 + 报告 + 上传）
```

Orchestrator 持有的唯一状态：

```jsonc
{
  "eval_id": "<eval_id>",
  "phase": "INIT" | "PREP" | "RUN" | "REPORT" | "DONE" | "ERROR",
  "tc_list": ["tc-001", "tc-002", ...],   // 从 run_plan.json 读取，PREP 完成后填充
  "completed_tcs": [],
  "failed_tcs": [],
  "parallelism_enabled": false            // 从 evaluation_context.parallelism.enabled 读取
}
```

---

## 状态机定义

### INIT → PREP

触发条件：用户发起评估请求（或平台触发 `evaluate employee`）。

动作：
1. 验证 `/workspace/runtime/evaluation-context.json` 存在且可解析。
2. 读取 `evaluation_context.parallelism.enabled`（缺失默认 `false`）。
3. 启动 **Prep Agent**（`agent-boundaries.md` §Prep Agent 定义）。
4. `phase = "PREP"`。

### PREP → RUN

触发条件：`runs/<eval_id>/run_plan.json` 存在（Prep Agent 写入此文件即视为完成）。

动作：
1. 读取 `run_plan.json`，提取 `tc_list = [scenario.tc_id for scenario in run_plan.scenarios]`。
2. 若 `tc_list` 为空 → `phase = "ERROR"`，原因 `prep_produced_no_scenarios`。
3. 若 `parallelism_enabled == true`：
   - **并发**启动 N 个 Run Agent 实例，每实例携带各自的 `tc_id`。
4. 若 `parallelism_enabled == false`（默认）：
   - **串行**按 `tc_list` 顺序依次启动 Run Agent，每个完成后再启动下一个。
5. `phase = "RUN"`。

> **并行开关说明**：`evaluation_context.parallelism.enabled` 默认 `false`。开启并行需要平台支持并发子 Agent 实例，且每个 Run Agent 共享同一文件系统（`/workspace`）。并行时各 Run Agent 写入不同路径（`traces/<tc_id>/`、`scores/<tc_id>__*.json`），不存在写冲突。

### RUN（部分完成）

触发条件：某个 Run Agent 完成（`traces/<tc_id>.trace.json` 且所有 `scores/<tc_id>__*.json` 已存在）。

动作：
1. 将该 `tc_id` 加入 `completed_tcs`。
2. 若对应 `scores/<tc_id>__summary.json` 存在 → 记录 `summary_available = true`；否则 `summary_available = false`（降级兼容）。
3. 若 `tc_id` 对应 Run Agent 以错误码退出 → 加入 `failed_tcs`，不加入 `completed_tcs`。写入 `TAINTED.md` 片段（受影响 tc_id）。
4. 检查是否所有 TC 已处理：`len(completed_tcs) + len(failed_tcs) == len(tc_list)`。
   - 若是 → 进入 RUN → REPORT 转换。
   - 若否 → 继续等待（串行模式下继续启动下一个 Run Agent）。

### RUN → REPORT

触发条件：所有 TC 均已处理（completed 或 failed）。

动作：
1. 若 `len(completed_tcs) == 0` → `phase = "ERROR"`，原因 `all_run_agents_failed`，终止评估。
2. 若 `len(failed_tcs) > 0`：
   - 记录 `open_taint_tc_ids = failed_tcs`，传递给 Report Agent 写入 `open_questions`。
3. 启动 **Report Agent**，传递：
   - `eval_id`
   - `completed_tcs`（Report Agent 只处理这些 TC 的产物）
   - `open_taint_tc_ids`（Report Agent 在 `open_questions` 中注明这些 TC 失败）
4. `phase = "REPORT"`。

### REPORT → DONE

触发条件：`runs/<eval_id>/reports/evaluation_report.json` 且 `runs/<eval_id>/upload_verdict_result.json` 均存在（STEP 10 完成标志）。

动作：`phase = "DONE"`，通知用户评估完成，提供报告路径。

### 任意阶段 → ERROR

触发条件：子 Agent 返回不可恢复错误，或超时（各阶段超时见下文），或检测到 TAINTED.md 且污染范围为"整个运行"。

动作：
1. 记录错误阶段、原因、受影响产物。
2. 写入或追加 `runs/<eval_id>/TAINTED.md`（若尚不存在则创建）。
3. 通知用户，提示恢复路径（参见 `tainted-run-lifecycle.md`）。

---

## 超时配置

| 阶段 | 默认超时 | 覆盖字段 |
|---|---|---|
| PREP | 10 分钟 | `evaluation_context.orchestrator.prep_timeout_seconds` |
| 单个 Run Agent | 15 分钟 | `evaluation_context.orchestrator.run_agent_timeout_seconds` |
| REPORT | 10 分钟 | `evaluation_context.orchestrator.report_timeout_seconds` |

超时视为该阶段的 ERROR，处理同上。

---

## Orchestrator 不做的事（硬性边界）

1. **不执行任何评估业务逻辑**。Orchestrator 不读取 metric 文件、不打分、不生成报告。
2. **不持有员工模板上下文**。员工模板材料只在 Prep Agent 的上下文中加载。
3. **不持有 trace 内容**。trace 文件由 Run Agent 写入，Orchestrator 只检查文件是否存在。
4. **不修改任何产物文件**。Orchestrator 只读 `run_plan.json` 和检查产物路径；所有写入由子 Agent 完成。
5. **不跨 Agent 传递上下文内容**。子 Agent 之间的数据传递**完全通过文件系统**，不通过 Orchestrator 的内存。

---

## 与现有 K 规则的关系

Orchestrator 本身不触发任何 K 规则检查。K 规则由各子 Agent 在其职责范围内自行遵守：

| K 规则 | 负责 Agent |
|---|---|
| K1 MetricRegistryNonEmpty | Prep Agent（PRE 步骤） |
| K2–K15 | Prep Agent（STEP 0~2.5）或 Run Agent（STEP 3~4） |
| K16 scored_at 唯一性 | Run Agent（STEP 4），Report Agent（STEP 5 输入门验证） |
| K17 employee provenance | Prep Agent（STEP 0） |
| K18 curate 证据 | Prep Agent（STEP 1.2） |
| K19–K21 | Prep Agent（STEP 1.5 / 2.5 / 3）或 Run Agent |
| K22 Agent 边界不可越权 | 所有 Agent 自检 |

若某个子 Agent 触发污染，该 Agent 负责写 `TAINTED.md`；Orchestrator 检测到文件后进行 ERROR 状态转换。

---

## 参考

- [`agent-boundaries.md`](./agent-boundaries.md) — 三个 Agent 的完整职责定义、输入/输出契约
- [`prep-agent-playbook.md`](./prep-agent-playbook.md) — Prep Agent 启动与执行指引
- [`run-agent-playbook.md`](./run-agent-playbook.md) — Run Agent 单 TC 执行指引
- [`tainted-run-lifecycle.md`](./tainted-run-lifecycle.md) — 污染恢复路径
