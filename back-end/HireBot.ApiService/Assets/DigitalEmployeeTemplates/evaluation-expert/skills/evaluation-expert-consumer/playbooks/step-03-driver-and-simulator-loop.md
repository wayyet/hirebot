# STEP 3 — driveEmployeeOnScenario

**依据**：工作流合同 `S3` + K8  
**输入**：`enriched-cases/<tc_id>.enriched.json`、`/workspace/runtime/evaluation_context.json`  
**输出**：`runs/<eval_id>/traces/<tc_id>.trace.json`（根据 `execution_trace.schema.json` 验证）

## 执行模式：`--utterance` 单轮 LLM 追问（唯一模式）

每次 `run.py` 调用只发一轮：连接 → 发送（带 `sessionId`）→ 收集回复 → 追加到 partial trace → 退出。  
Host Agent LLM 在两次调用之间读 partial trace，渲染 `system_prompt.md`，生成 `SimulatorDecision`，再将 `decision.next_utterance` 作为下一轮的 `--utterance`。

**关键特性：**
- 无长驻子进程，无 pad 文件，无后台轮询
- 追问完全基于被评估者的实际回复内容（不是固定话术）
- `sessionId` 由首轮后的 partial trace 缓存（`_ws_session_id`），后续轮次自动复用，不会新建会话

### 调用序列

```
# ── 轮次 0：首轮（固定使用 opening_message）──────────────────────────────
python3 runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation-context.json \
  --enriched-test-case  runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output              runs/<eval_id>/traces/<tc_id>.trace.json \
  --utterance "<tc.input.opening_message>"
# → 写 partial trace，_ws_session_id 自动缓存
# → stdout 输出 {"event":"evaluatee_turn","turn_index":0,"content":"...",...}
# → stdout 输出 {"event":"turn_appended","turn_index":0,...}

# ── LLM 决策：宿主 Agent 读 partial trace，渲染 system_prompt.md ──────────
#   展开占位符：
#     {{customer_persona.*}}     ← enriched_test_case.customer_persona
#     {{goal.*}}                 ← enriched_test_case.goal
#     {{stop_conditions.*}}      ← enriched_test_case.stop_conditions
#     {{current_emotion}}        ← partial_trace.simulator_trail[-1].internal_emotion（首轮取 tc 默认值）
#     {{dialog_so_far}}          ← partial_trace.dialog_turns 格式化为对话记录
#     {{effective_max_turns}}    ← min(tc.turn_budget.hard_max_turns, eval_ctx.global_turn_cap)
#
#   LLM 输出：SimulatorDecision JSON
#   若 decision.should_continue == false → 跳到收尾步骤

# ── 轮次 1..N：动态追问 ────────────────────────────────────────────────────
python3 runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation-context.json \
  --enriched-test-case  runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output              runs/<eval_id>/traces/<tc_id>.trace.json \
  --utterance "<decision.next_utterance>"
# → 自动从 partial trace 读取 _ws_session_id，续上已有会话

# （重复 LLM 决策 → 追问 → 直到 decision.should_continue == false）

# ── 收尾：生成完整 trace ───────────────────────────────────────────────────
python3 runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation-context.json \
  --enriched-test-case  runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output              runs/<eval_id>/traces/<tc_id>.trace.json \
  --finalize-trace \
  --termination-reason  <decision.stop_reason>
# → 清除 _partial/_ws_session_id/_last_turn_index，写入 termination 块，输出完整 trace
```

### `{{dialog_so_far}}` 展开规则（防止上下文随轮次线性膨胀）

**背景**：每轮 Simulator 决策都要把完整对话历史展开进 system prompt。当被评估员工输出长文时（如招标书、法律意见、代码），历史轮次的原始内容会使上下文随轮次线性增长，最终导致 Agent 超窗口中断。

**展开规则**（Agent 在渲染 `{{dialog_so_far}}` 时必须遵守）：

```
turn 0 到 turn (N-3)（历史轮次，距当前超过 2 轮）：
  格式：[T{turn_index}|{actor}] {content 前80字}{若被截断则追加"…（共约{估算字数}字）"}
  示例：[T2|evaluatee] 您好，关于您反映的退款问题，我已查询到订单 2024061200…（共约 320 字）

turn (N-2) 和 turn (N-1)（最近两轮，完整展开）：
  格式：展开完整 content，不截断
  原因：Simulator 决策主要依赖最近一轮被评估者的完整回复
```

**效果**：无论对话进行多少轮，每次 Simulator 调用看到的 `dialog_so_far` 总量约为：
- 历史轮次：(N-2) 条 × ~50 token/条（固定）
- 最近两轮：完整 content（可能较大，但只有 2 轮）

上下文增长从 **O(N × L)**（L 为单轮最大长度）变为 **O(N × 50 + 2 × L)**，避免历史积累导致的超窗口。

**actor 标记规范**：
- `evaluator` → 客户（Simulator）发出的话语
- `evaluatee` → 被评估员工的回复

---

### LLM 决策——simulator_trail 追加（在调用下一次 `--utterance` 前）

每次 LLM 生成 `SimulatorDecision` 后，宿主 Agent 必须将决策追加到 partial trace 的 `simulator_trail` 字段，使 trace 记录完整的模拟器推理链：

```python
# 伪代码：读取 partial trace，追加 SimulatorDecision，写回
trace = json.load(open(output_path))
decision["decided_at"] = now_iso()
trace["simulator_trail"].append(decision)
json.dump(trace, open(output_path, "w"), ensure_ascii=False, indent=2)
```

### 停止条件

`SimulatorDecision.should_continue == false` 时终止循环，`stop_reason` 作为 `--termination-reason` 传入收尾调用。常见取值：

| `stop_reason` | 含义 |
|---|---|
| `goal_achieved` | 被评估者已完成实际操作，问题已解决 |
| `bottom_line_violated` | 被评估者触犯底线（如泄露禁止信息） |
| `deadlock_detected` | 对话陷入死循环，无实质进展 |
| `customer_gave_up` | 轮次达到上限或客户放弃 |

### 运行后验证

```bash
python3 -c "
import json, sys
t = json.load(open('runs/<eval_id>/traces/<tc_id>.trace.json'))
assert '_partial' not in t, 'trace is still partial!'
assert 'termination' in t, 'missing termination block'
print('turns_used:', t['termination']['turns_used'])
print('reason:', t['termination']['reason'])
"
```

---

## 硬性规则（K8）

Agent **不得**创建任何 `.py` / `.sh` / `.ts` / `.js` / `Makefile` / `*.ps1` 文件来驱动循环。允许的可执行文件仅限 `./runtime-drivers/<driver_id>/` 下已提交的文件。

---

## Trace 拒绝规则

以下任意条件成立，trace 将在 STEP 4 输入门被拒绝：

1. `termination.reason == "evaluatee_error"` AND `detail` 含 `"stdin closed before 'end' action received"`
2. `termination.reason == "evaluatee_error"` AND `turns_used == 1` AND `actual_tool_calls == []`
3. `termination.reason == "max_turns_reached"` AND `turns_used < effective_max_turns` AND `simulator_trail[-1].should_continue == true`
4. `simulator_trail[-1].next_utterance` 非空 AND 该文本未出现在最后一条 `actor == "evaluator"` 的 `dialog_turns` 中

被拒绝的 trace 会污染运行；受影响的 `tc_id` **必须**出现在 `EvaluationReport.open_questions` 中。