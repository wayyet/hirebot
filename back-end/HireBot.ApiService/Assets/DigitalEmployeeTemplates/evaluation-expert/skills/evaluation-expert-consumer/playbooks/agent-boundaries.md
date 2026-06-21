# Agent 边界定义

**版本**：v1.0.0  
**配套文件**：`orchestrator.md`、`prep-agent-playbook.md`、`run-agent-playbook.md`  
**适用架构**：multi-agent（三段式）评估流水线

本文件是三个子 Agent 的**职责边界权威定义**。任何 Agent 越出自己的边界即触发 **K22（AgentBoundaryViolation）**——污染范围等同于 K9（整个运行停止）。

---

## 总览：职责分配

| 步骤 | 负责 Agent | 上下文规模（估算） |
|---|---|---|
| 评估前完备性检查 | Prep Agent | 小（只读文件路径） |
| PRE.A loadRoleCatalog | Prep Agent | 小 |
| STEP 0 resolveEmployee | Prep Agent | 中（员工模板材料） |
| PRE loadMetricRegistry | Prep Agent | 小 |
| STEP 1 候选指标过滤 | Prep Agent | 小 |
| STEP 1.2 curateMetrics | Prep Agent | 中（LLM，含证据） |
| STEP 1.5 parseTestCases | Prep Agent | 中（读员工模板推导场景） |
| STEP 1.6 pushSynthesizedTestCases | Prep Agent | 小 |
| STEP 2 enrichTestCases | Prep Agent | 小 |
| STEP 2.5 planRun | Prep Agent | 小 |
| STEP 3 driveEmployeeOnScenario | **Run Agent**（per TC） | 小（只读单个 TC） |
| STEP 4 scoreScenario | **Run Agent**（per TC） | 小（只读单个 TC 的 trace） |
| STEP 5 aggregateAcrossScenarios | Report Agent | 小（读摘要或 score 文件） |
| STEP 6 rollUpToDimensions | Report Agent | 小 |
| STEP 7 redLineCheck | Report Agent | 小 |
| STEP 8 buildScenarioReports | Report Agent | 中（LLM 散文，逐 TC） |
| STEP 9 buildOverallReport | Report Agent | 中（读汇总 JSON + HTML 模板） |
| STEP 10 uploadToHireBot | Report Agent | 小 |

---

## Prep Agent

### 职责

执行评估的**初始化阶段**：从零开始，读取所有员工模板材料，完成员工身份解析、指标精选、测试用例准备和执行计划生成。

### 输入（只允许读取这些来源）

| 来源 | 用途 |
|---|---|
| `/workspace/runtime/evaluation-context.json` | 完整评估上下文 |
| `/workspace/uploads/artifact/<template_dir>/` | 员工模板材料（IDENTITY/SOUL/AGENTS/SKILL/ontology） |
| `./role-catalog/*.role.json` | 角色目录 |
| `./metrics/*.metric.json` | 指标库 |
| `./test-cases/*.tc.json` | 预置测试用例（若存在） |
| `./runtime-drivers/<driver_id>/driver.json` | Driver manifest（评估前完备性检查） |
| `./simulators/<simulator_id>/simulator.json` | Simulator manifest（评估前完备性检查） |
| `./contracts/projections/` | 投影合同（metric-selection / scoring-judgement） |

### 输出（写入文件系统）

| 产物 | 路径 | 消费方 |
|---|---|---|
| 丰富测试用例 | `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json` | Run Agent |
| 合成测试用例（若走 1.5） | `runs/<eval_id>/synthesized-cases/<tc_id>.tc.json` | STEP 1.6 / Report Agent |
| 执行计划 | `runs/<eval_id>/run_plan.json` | Orchestrator（完成信号）、人工审阅 |
| 评估上下文快照（含 selected_metrics） | `runs/<eval_id>/evaluation_context.json` | Run Agent、Report Agent |

> **注意**：`runs/<eval_id>/evaluation_context.json` 是 Prep Agent 在 STEP 0 末尾写入的**脱敏快照**，不含 `hirebot_api.auth.client_secret`。Run Agent 和 Report Agent 需要凭据时，必须读取 `/workspace/runtime/evaluation-context.json` 原始文件（见 STEP 2.5 playbook）。

### 上下文加载顺序（必须严格遵守）

```
1. /workspace/runtime/evaluation-context.json
2. /workspace/uploads/artifact/<template_dir>/config/IDENTITY.md
3. /workspace/uploads/artifact/<template_dir>/config/SOUL.md
4. /workspace/uploads/artifact/<template_dir>/config/AGENTS.md
5. /workspace/uploads/artifact/<template_dir>/skills/*/SKILL.md
6. /workspace/uploads/artifact/<template_dir>/ontology/*.slice.md
7. 评估专用本体（若存在）
8. 预置测试用例目录
```

加载完成后，Prep Agent **不再需要**这些材料的原文——后续步骤通过持久化产物传递信息。

### 退出条件

`runs/<eval_id>/run_plan.json` 写入完成后，Prep Agent 退出，通知 Orchestrator。

### 边界禁令（K22）

- **不执行 STEP 3 或 STEP 4**。
- **不生成报告**。
- **不上传结果**。
- **不直接与目标沙箱通信**（driver/WebSocket 仅 Run Agent 使用）。

---

## Run Agent

### 职责

执行单个测试用例的**对话驱动 + 评分**（STEP 3 + STEP 4）。每个实例只处理一个 TC，处理完即退出。

### 输入（只允许读取这些来源）

| 来源 | 用途 |
|---|---|
| `/workspace/runtime/evaluation-context.json` | 获取 driver_config、auth、global_turn_cap |
| `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json` | 当前 TC（含 applicable_metrics） |
| `runs/<eval_id>/evaluation_context.json`（Prep Agent 写入的快照） | 获取 selected_metrics 定义（用于 STEP 4 评分规则）|
| `./metrics/<metric_code>.metric.json`（仅 applicable_metrics 中的） | 评分 rubric |
| `./simulators/<simulator_id>/system_prompt.md` | Simulator 系统提示词模板 |
| `./contracts/projections/ontology_extraction/scoring-judgement/` | 评分约束投影 |

### 输出（写入文件系统）

| 产物 | 路径 | 必须/可选 |
|---|---|---|
| 执行轨迹 | `runs/<eval_id>/traces/<tc_id>.trace.json` | **必须** |
| 每指标分数 | `runs/<eval_id>/scores/<tc_id>__<metric_code>.json` | **必须**（每个 applicable_metric 一个） |
| TC 级摘要 | `runs/<eval_id>/scores/<tc_id>__summary.json` | **可选**（存在时 Report Agent 优先读，提升性能） |

### TC 级摘要写入规则

Run Agent 在所有 `scores/<tc_id>__<metric_code>.json` 写入完成后，再写 `scores/<tc_id>__summary.json`。摘要须符合 `runtime-schemas/tc_score_summary.schema.json`。

**摘要内容来源**：
- `turns_used`、`termination_reason` → `traces/<tc_id>.trace.json`
- `actual_tool_calls`、`missing_required_tools` → trace + enriched_test_case 计算
- `observed_signals` → 所有 `scores/<tc_id>__<metric_code>.json` 的 `observed_signals` 合并
- `metric_scores.<metric_code>.score`、`scored_at` → 各 score 文件字节拷贝
- `metric_scores.<metric_code>.reasoning_snippet` → 各 score 文件 `reasoning` 字段前 200 字

摘要文件写入**不得早于**所有 score 文件写入完成（顺序约束）。

### 边界禁令（K22）

- **不读取员工模板原文**（IDENTITY/SOUL/AGENTS/SKILL/ontology）。Prep Agent 已将必要信息提炼到 `enriched-cases/*.enriched.json` 中。
- **不生成报告**（STEP 8/9/10 属于 Report Agent）。
- **不执行 STEP 5/6/7**。
- **不修改 `evaluation_context.json`**（Prep Agent 写入后为只读）。
- **不写入 `selected_metrics`**（K17 单一写入规则）。
- 只读取**本实例 tc_id** 对应的文件；不扫描其他 TC 的产物。

### simulator 上下文最小化

Run Agent 启动时只加载 `system_prompt.md` 模板文件。**不**加载 IDENTITY/SOUL/AGENTS 等模板材料。Simulator 决策所需的员工行为边界信息，已经通过 `enriched_test_case.customer_persona`、`goal`、`stop_conditions` 等字段内嵌到 enriched 文件中（Prep Agent 在 STEP 1.5 生成时填入）。

---

## Report Agent

### 职责

在所有 Run Agent 完成后，执行**汇总 + 报告 + 上传**（STEP 5~10）。上下文极简，不加载任何 trace 原文。

### 输入（只允许读取这些来源）

| 来源 | 用途 |
|---|---|
| `/workspace/runtime/evaluation-context.json` | 获取 auth（STEP 10 上传用）|
| `runs/<eval_id>/evaluation_context.json` | 获取 eval_id、employee 信息、selected_metrics |
| `runs/<eval_id>/scores/<tc_id>__summary.json`（若存在） | STEP 5/6/7 优先读 |
| `runs/<eval_id>/scores/<tc_id>__<metric_code>.json`（摘要缺失时） | STEP 5/6/7 降级读 |
| `runs/<eval_id>/aggregated_metric_scores.json` | STEP 6/7/9 字节拷贝（K7） |
| `runs/<eval_id>/dimension_scores.json` | STEP 7/9 字节拷贝（K7） |
| `runs/<eval_id>/red_line_check.json` | STEP 9 字节拷贝（K7） |
| `runs/<eval_id>/reports/scenarios/<tc_id>.report.json` | STEP 9 链接（K6，不内联） |
| `./runtime-schemas/evaluation_report.schema.json` | STEP 9 JSON 结构约束 |
| `./runtime-schemas/report-template.html` | STEP 9 HTML 模板（K17） |
| `./metrics/<metric_code>.metric.json` | STEP 9 中文标签来源（K18） |
| `./role-catalog/<role_id>.role.json` | STEP 9 工具中文标签来源（K18） |

### 严格禁止读取

- 任何 `runs/<eval_id>/traces/<tc_id>.trace.json`（**完全不读**）。
- 任何员工模板材料（`/workspace/uploads/artifact/`）。
- Prep Agent 写入之外的任何上下文材料。

> **为什么不读 trace？**  
> Report Agent 需要的轨迹信息已经全部内嵌于：
> - `tc_score_summary.json`（actual_tool_calls、missing_required_tools、observed_signals）
> - `scores/<tc_id>__<metric_code>.json`（evidence 字段中的 trace 引用片段）
> - `reports/scenarios/<tc_id>.report.json`（STEP 8 产物，含轨迹摘要）  
> 读取完整 trace 原文会无谓膨胀 Report Agent 的上下文（每个 trace 可达数千 token × N 个 TC）。

### 输出

| 产物 | 路径 |
|---|---|
| STEP 5 产物 | `runs/<eval_id>/aggregated_metric_scores.json` |
| STEP 6 产物 | `runs/<eval_id>/dimension_scores.json` |
| STEP 7 产物 | `runs/<eval_id>/red_line_check.json` |
| STEP 8 产物 | `runs/<eval_id>/reports/scenarios/<tc_id>.report.json`（每 TC 一个） |
| STEP 9 JSON 报告 | `runs/<eval_id>/reports/evaluation_report.json` |
| STEP 9 HTML 报告 | `runs/<eval_id>/reports/evaluation_report.html` |
| STEP 10 上传回执 | `runs/<eval_id>/upload_verdict_result.json` + `upload_trace_result.json` |

### 边界禁令（K22）

- **不执行 STEP 0~4**。
- **不修改任何 trace 文件或 score 文件**。
- **不读取 trace 原文**（`runs/<eval_id>/traces/` 目录对 Report Agent 不可见）。

---

## K22 — AgentBoundaryViolation（新规则）

**描述**：任何 Agent 执行了超出其职责边界的步骤，或读取了禁止读取的文件。

**严重级别**：critical（与 K9 同级，整个运行停止）

**检测方式**：Agent 自检。每个 Agent 在写入任何产物前，必须确认当前操作在自己的"允许执行的步骤"列表内。

**污染范围**：等同于整个运行（完全重启，新 `eval_id`）。

**例外**：Orchestrator 读取 `run_plan.json` 检查完成状态不视为越权。

---

## 文件系统是唯一的 Agent 间通信通道

```
Prep Agent  ──写──►  enriched-cases/  ──读──►  Run Agent
                     run_plan.json    ──读──►  Orchestrator
                     evaluation_context.json ──读──► Run Agent + Report Agent

Run Agent   ──写──►  traces/<tc_id>.trace.json
                     scores/<tc_id>__*.json   ──读──►  Report Agent
                     scores/<tc_id>__summary.json

Report Agent ──写──► aggregated_metric_scores.json
                     dimension_scores.json
                     red_line_check.json
                     reports/
                     upload_*.json
```

**禁止**通过 Orchestrator 的内存传递评估数据（trace 内容、score 值、员工信息等）。Orchestrator 只传递 `eval_id`、`tc_id`、`phase` 等控制信令。
