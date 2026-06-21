# Run Agent — 操作手册（单 TC 执行）

**角色**：单测试用例执行代理  
**职责范围**：STEP 3（driveEmployeeOnScenario）+ STEP 4（scoreScenario）+ TC 摘要写入  
**上下文边界**：见 `agent-boundaries.md` §Run Agent  
**实例化方式**：每个 TC 一个独立 Agent 实例，由 Orchestrator 触发  
**退出条件**：`scores/<tc_id>__summary.json` 写入完成（或所有 score 文件写完且摘要写失败时以降级模式退出）

---

## 启动参数

Orchestrator 启动 Run Agent 时传入以下参数：

```jsonc
{
  "eval_id": "<eval_id>",
  "tc_id": "<tc_id>",
  "evaluation_context_path": "/workspace/runtime/evaluation-context.json"
}
```

Run Agent 从 `eval_id` + `tc_id` 推导所有文件路径，无需 Orchestrator 传递更多信息。

---

## 启动自检（Run Agent 开始前）

1. `/workspace/runtime/evaluation-context.json` 可读，且 `hirebot_api.auth` 完整（K6a）
2. `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json` 存在且通过 `enriched_test_case.schema.json` 验证
3. `runs/<eval_id>/evaluation_context.json`（Prep Agent 快照）存在，可读取 `selected_metrics`
4. `./runtime-drivers/<driver_id>/run.py` 存在（K8 白名单文件）
5. `./simulators/<simulator_id>/system_prompt.md` 存在
6. `runs/<eval_id>/traces/<tc_id>.trace.json` **不存在**（防止覆盖已完成的 trace）
   - 若存在且完整（含 `termination` 块）→ 跳过 STEP 3，直接执行 STEP 4（断点续跑）
   - 若存在但为 partial（含 `_partial` 标记）→ 继续 STEP 3（从断点恢复）

---

## 上下文加载（最小化）

Run Agent 只加载以下内容，**不加载任何员工模板材料**：

```
1. /workspace/runtime/evaluation-context.json        （driver_config、auth、global_turn_cap）
2. runs/<eval_id>/enriched-cases/<tc_id>.enriched.json  （当前 TC 完整定义）
3. runs/<eval_id>/evaluation_context.json             （selected_metrics 及其 metric_code 列表）
4. ./metrics/<metric_code>.metric.json                （仅 tc.applicable_metrics 中的指标，逐一按需加载）
5. ./simulators/<simulator_id>/system_prompt.md       （Simulator 模板，加载一次后复用）
6. ./contracts/projections/ontology_extraction/scoring-judgement/
   scoring-judgement.prompt-constraint.projection.json  （STEP 4 评分约束，K3）
```

> **为什么不读 IDENTITY/SOUL？**  
> Prep Agent 在 STEP 1.5 生成 enriched 文件时，已将员工的行为边界提炼到 `customer_persona`、`goal`、`stop_conditions` 字段中。Run Agent 无需原始模板材料即可正确模拟客户并评分。

---

## 执行流程

### STEP 3 — driveEmployeeOnScenario

详细协议见 [`step-03-driver-and-simulator-loop.md`](./step-03-driver-and-simulator-loop.md)。

Run Agent 视角的关键约束：

**session 管理**：
- `sessionId` 由首轮 run.py 调用后自动缓存在 partial trace 的 `_ws_session_id` 字段
- 后续轮次调用 run.py 时自动从 partial trace 读取并复用，**不新建会话**

**simulator 系统提示词**：
- 仅在首轮前加载 `system_prompt.md` 一次，后续轮次直接展开 Mustache 占位符（不重新读文件）
- 占位符来源：`enriched_test_case.input.customer_persona`、`.goal`、`.stop_conditions`、partial trace 的 `simulator_trail[-1].internal_emotion`

**轮次循环**（`--utterance` 单轮模式，唯一允许模式）：

```
轮次 0：
  → run.py --utterance "<tc.input.opening_message>"
  → 读 partial trace，渲染 system_prompt.md，生成 SimulatorDecision
  → 追加 SimulatorDecision 到 partial trace.simulator_trail
  → 若 decision.should_continue == false → 跳到收尾

轮次 N（N ≥ 1）：
  → run.py --utterance "<decision.next_utterance>"
  → 读 partial trace，生成 SimulatorDecision
  → 追加到 simulator_trail
  → 重复直到 should_continue == false 或达到 effective_max_turns

收尾：
  → run.py --finalize-trace --termination-reason <decision.stop_reason>
  → 验证 trace 完整性（_partial 字段不存在 + termination 块存在）
```

**Trace 拒绝规则**（K14，四条款）：满足任意一条 → trace 被污染，Run Agent 写 TAINTED.md 并退出

### STEP 4 — scoreScenario（LLM 并行扇出）

详细规则见 [`step-04-fanout-scoring.md`](./step-04-fanout-scoring.md)。

Run Agent 视角的关键操作：

**评分循环**（K3：每 (tc, metric) 独立推理，K16：scored_at 各不同）：

```python
# 伪代码：对当前 TC 的所有 applicable_metrics 逐一评分
trace = load_json(f"runs/{eval_id}/traces/{tc_id}.trace.json")
enriched_tc = load_json(f"runs/{eval_id}/enriched-cases/{tc_id}.enriched.json")
scoring_constraint = load_json("contracts/projections/ontology_extraction/scoring-judgement/scoring-judgement.prompt-constraint.projection.json")

for metric_code in enriched_tc.applicable_metrics:
    metric_def = load_json(f"metrics/{metric_code}.metric.json")

    # 独立推理：每次都重新读 trace，不复用上一次推理的中间状态
    score_result = llm_invoke(
        system  = build_scoring_prompt(metric_def, scoring_constraint),
        user    = build_scoring_input(trace, enriched_tc, metric_def)
    )

    # 立即落盘（scored_at 在写入时取当前时间，K16）
    score_result["scored_at"] = now_iso()
    write_json(f"runs/{eval_id}/scores/{tc_id}__{metric_code}.json", score_result)
    # 不等所有指标都评分完再批量写——每评完一个立即写一个
```

**评分前必读**（K16 evidence 要求）：
- 完整读取 `traces/<tc_id>.trace.json`（引用 `dialog_turns` 或 `actual_tool_calls` 中的具体片段）
- 完整读取 `enriched-cases/<tc_id>.enriched.json`（`expected_tool_calls`、`expected_output` 等）

**K16 自检**（所有 score 文件写完后）：

```python
scored_at_values = [
    load_json(f"runs/{eval_id}/scores/{tc_id}__{m}.json")["scored_at"]
    for m in enriched_tc.applicable_metrics
]
assert len(set(scored_at_values)) == len(scored_at_values), \
    "K16 violation: duplicate scored_at — batch fabrication detected"
```

### TC 级摘要写入（STEP 4 完成后）

所有 score 文件写入完成且 K16 自检通过后，Run Agent 写入摘要文件：

```python
# 构建摘要（tc_score_summary.schema.json）
trace = load_json(f"runs/{eval_id}/traces/{tc_id}.trace.json")
enriched_tc = load_json(f"runs/{eval_id}/enriched-cases/{tc_id}.enriched.json")

# 计算 missing_required_tools
must_tools = [t.tool_name for t in enriched_tc.expected_tool_calls if t.criticality == "must"]
actual_tools = trace.actual_tool_calls  # 已是 flat list
missing_tools = [t for t in must_tools if t not in actual_tools]

# 汇总 observed_signals（来自所有 score 文件）
all_signals = []
metric_scores_summary = {}
for metric_code in enriched_tc.applicable_metrics:
    sf = load_json(f"runs/{eval_id}/scores/{tc_id}__{metric_code}.json")
    all_signals += [{"signal": s.signal, "metric_code": metric_code, "detail": s.get("detail")}
                    for s in sf.observed_signals]
    metric_scores_summary[metric_code] = {
        "score":              sf.score,
        "scored_at":          sf.scored_at,           # 字节拷贝，保留用于 STEP 5 验证
        "reasoning_snippet":  sf.reasoning[:200],      # 前 200 字
        "rubric_adjustments_count": len(sf.get("rubric_adjustments", []))
    }

summary = {
    "evaluation_id":      eval_id,
    "tc_id":              tc_id,
    "completed_at":       now_iso(),
    "turns_used":         trace.termination.turns_used,
    "termination_reason": trace.termination.reason,
    "actual_tool_calls":  actual_tools,
    "missing_required_tools": missing_tools,
    "observed_signals":   all_signals,
    "metric_scores":      metric_scores_summary
}

validate_json(summary, "runtime-schemas/tc_score_summary.schema.json")
write_json(f"runs/{eval_id}/scores/{tc_id}__summary.json", summary)
```

**摘要写入失败的处理**：
- schema 验证失败或写入 IO 错误 → 记录警告，但**不污染运行**（摘要是可选的）
- 原始 score 文件和 trace 文件已完整 → Run Agent 仍视为成功退出
- Report Agent 检测到摘要缺失时自动降级读原始文件（向后兼容）

---

## STEP 3 被污染时的处理

若 trace 触发 K14 四条款拒绝规则之一：

1. 写入 `runs/<eval_id>/TAINTED.md`（追加，不覆盖）：
   ```markdown
   ## TC Tainted: <tc_id>
   **Rule**: K14 (DriverProtocolLoopComplete)
   **Reason**: <具体拒绝原因>
   **Affected**: runs/<eval_id>/traces/<tc_id>.trace.json
   **Recovery**: 跳过该 TC 的 STEP 4；Report Agent 在 open_questions 中注明
   ```
2. **不执行 STEP 4**（该 TC 的所有 score 文件不写入）。
3. **不写摘要文件**。
4. Run Agent 以错误码退出，通知 Orchestrator。

Orchestrator 将该 `tc_id` 加入 `failed_tcs`，Report Agent 在 `open_questions` 中以 `critical` 级别记录。

---

## K 规则自检速查（Run Agent 必须遵守）

| K 规则 | 检查点 | 失败时 |
|---|---|---|
| K3 每 (tc,metric) 独立推理 | STEP 4 评分循环内 | 污染（该 TC 所有 score 文件重新生成） |
| K8 禁止编排脚本 | 启动自检 | 污染（整个运行） |
| K10 enriched 文件与内联一致 | STEP 3 前读文件 | 污染 |
| K14 driver 协议完整性 | STEP 3 收尾验证 | TC 级污染 |
| K15 stop_conditions 对齐 | STEP 3 前检查 | TC 设计缺陷，记录 open_question |
| K16 scored_at 唯一性 | STEP 4 自检 | 污染（score 文件重新生成） |
| K16 evidence 必须引用 trace | STEP 4 每次推理后 | score 文件重新生成 |
| K19 driver 子进程接线 | STEP 3 spawn 前 | 污染（TC 级） |
| K20 run_plan 已落盘 | 启动自检 | block（等待 Prep Agent 完成） |
| K22 Agent 边界 | 每步前自检 | 污染（整个运行） |

---

## 参考

- STEP 3 详细协议：[`step-03-driver-and-simulator-loop.md`](./step-03-driver-and-simulator-loop.md)
- STEP 4 扇出评分：[`step-04-fanout-scoring.md`](./step-04-fanout-scoring.md)
- TC 摘要 Schema：[`../runtime-schemas/tc_score_summary.schema.json`](../runtime-schemas/tc_score_summary.schema.json)
- Agent 边界：[`agent-boundaries.md`](./agent-boundaries.md)
- 污染处理：[`tainted-run-lifecycle.md`](./tainted-run-lifecycle.md)
