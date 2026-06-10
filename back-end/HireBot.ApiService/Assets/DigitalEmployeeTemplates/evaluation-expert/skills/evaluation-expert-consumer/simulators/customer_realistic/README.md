# customer_realistic

评估专家 Agent 在 STEP 3 中扮演真实客户时使用的默认**角色配置文件**。Agent 的**自有** LLM 大脑（运行 STEP 1.5 / STEP 4 / STEP 8 / STEP 9 的同一个大脑）读取此处的 `system_prompt.md`，将已丰富化的测试用例和进行中的执行 trace 中的占位符填入，并每轮生成一个符合 `runtime-schemas/simulator_decision.schema.json` 的 `SimulatorDecision`。

Agent 随后将 `decision.next_utterance` 转发给运行时 driver 子进程（通过 WebSocket / JWT 与被评估者通信），并将完整决策追加到 `execution_trace.simulator_trail` 用于审计。

> ⚠️ 本目录**不包含**可执行文件。**没有**子进程，**没有**外部 LLM 密钥，**没有** `decide.py`。模拟器是提示词模板 + 清单；消费它的 LLM 是宿主评估专家 Agent 自身的大脑。

## 人格概要

- 根据 `customer_persona`（性格、沟通风格、耐心程度）行事。
- 追求 `goal.primary`（可选 `secondary`）；如果 Agent 的回应低于 `goal.bottom_line`，则放弃对话。
- 每轮根据 Agent 的最新回复更新 `internal_emotion` 和 `perceived_progress`。
- 当 `stop_conditions` 满足时自行停止，无需耗尽 `turn_budget.hard_max_turns`。

## 文件说明

| 文件 | 用途 | 状态 |
|---|---|---|
| `simulator.json` | 清单文件，根据 `runtime-schemas/simulator.schema.json` 验证 | ✅ 已提交 |
| `system_prompt.md` | 宿主 Agent 每轮填入 `{{占位符}}` 的 LLM 系统提示词模板 | ✅ 已提交 |

## 每轮流程（在宿主 Agent 内部执行）

对每个客户轮次 `n`（从 0 开始索引）：

1. **构建提示词上下文**，从已丰富化的测试用例和当前执行 trace 读取：
   - `customer_persona.*`、`context`、`goal.*`、`stop_conditions.*` — 来自 `enriched_test_case.input`。
   - `dialog_so_far` — 将 `execution_trace.dialog_turns` 渲染为 `customer: …` / `agent: …` 行。
   - `current_emotion` — 从 `simulator_trail` 最后一条推导（或若为空则取 `initial_emotion`），如有 `emotion_shift` 则沿情绪阶梯偏移。
   - `effective_max_turns` = `min(turn_budget.hard_max_turns, evaluation_context.global_turn_cap)`。
2. **第 0 轮短路**：原文输出 `next_utterance = enriched_test_case.input.opening_message`，`should_continue=true`，`internal_emotion=initial_emotion`，`perceived_progress="none"`。第 0 轮**不调用** LLM——客户尚无任何内容可反应。
3. **第 ≥ 1 轮**：将 `system_prompt.md` 与上下文一起渲染，请宿主 LLM 返回符合 `runtime-schemas/simulator_decision.schema.json` 的 JSON 对象，解析并验证。
4. **接受决策前检验不变量**：
   - `should_continue=false` ⇒ `stop_reason` 必须为 `goal_achieved` / `bottom_line_violated` / `customer_gave_up` / `deadlock_detected` 之一；`next_utterance` 可以是最后一句话或空字符串。
   - `should_continue=true` ⇒ `stop_reason` 必须缺失（或为 null）；`next_utterance` 必须非空。
   - `violated_bottom_line=true` ⇒ `stop_reason=bottom_line_violated` 且 `should_continue=false`。
5. **发送给 driver**：向 driver 的 stdin 写入 `{"action":"send","turn_index":n,"text":decision.next_utterance,"decision":decision}`，并从 driver 的 stdout 读取下一个 `evaluatee_turn` 事件。
6. **停止**：当 `should_continue=false`（或循环触及 `effective_max_turns`）时，向 driver 写入 `{"action":"end","decision":finalDecision,"termination":{...}}`，让 driver 写入 trace 文件。

## 编写 `system_prompt.md`

提示词模板有意设计为非工程师可编辑（产品经理 / 领域专家）。只使用 Mustache 风格的 `{{占位符}}`；宿主 Agent 填入：

- `{{customer_persona.*}}`
- `{{context}}`（渲染为简短段落）
- `{{goal.*}}`
- `{{stop_conditions.*}}`
- `{{current_emotion}}`（运行时的绝对状态）
- `{{dialog_so_far}}`（渲染为 `agent: …` / `customer: …` 行）
- `{{effective_max_turns}}`

不要在模板中嵌入 Python 或可执行逻辑；如果需要分支，将其推入宿主 Agent 的 STEP 3 LLM 工作流，而不是本文件。

## 添加其他人格

要添加 `customer_calm`、`customer_aggressive` 等：

1. 放入同级目录 `simulators/<new_id>/`。
2. 复制 `simulator.json` 并调整 `simulator_id` + `version`。
3. 为新人格的声音重写 `system_prompt.md`。

无需编辑任何合同或投影文件，也无需添加代码——消费新提示词的是宿主 Agent 的 LLM。

## 情绪阶梯参考

宿主 Agent 将 `current_emotion` 维护为 7 级阶梯上的绝对状态：

```
angry → frustrated → anxious → skeptical → neutral → curious → satisfied
（愤怒 → 沮丧 → 焦虑 → 怀疑 → 中性 → 好奇 → 满意）
```

上一次决策中的 `emotion_shift` 作为增量应用：

| 偏移 | 移动方向 |
|---|---|
| `more_upset` | 向左一步（趋向 `angry`） |
| `calmer` | 向右一步（趋向 `satisfied`） |
| `unchanged`（或缺失） | 保持当前位置 |

测试用例中的 `initial_emotion` 设置第 1 轮前的起始位置。
