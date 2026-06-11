# STEP 3 — driveEmployeeOnScenario（driver + simulator 双角色循环）

**类型**：双角色（I/O 子进程 + 宿主 Agent simulator）
**依据**：工作流合同 `S3` + K8 + K14 + K15（运行时切面）
**输入**：已丰富化的测试用例、`evaluation_context.runtime_driver`、`evaluation_context.runtime_simulator`、`evaluation_context.global_turn_cap`
**输出**：`./runs/<eval_id>/traces/<tc_id>.trace.json`（根据 `execution_trace.schema.json` 验证）

## 非对称执行模型

| 角色 | 执行方式 | 位于 |
|---|---|---|
| `runtime_driver` | **子进程** — 通过 stdin/stdout 传输行分隔 JSON | `./runtime-drivers/<driver_id>/` |
| `runtime_simulator` | **非子进程** — 角色配置文件由宿主 Agent 自身的 LLM 消费 | `./simulators/<simulator_id>/` |

Driver 负责协议 I/O（WebSocket / JWT / TLS / 工具审批）。Simulator 决定客户说什么——与运行 STEP 1.5 / 4 / 8 / 9 的是同一个大脑。两者通过下文的行 JSON 协议通信。

## 每场景循环（每个 Agent 轮次执行一条 shell 命令）

对每个已丰富化的测试用例 `tc`：

### 1. 解析

从 `evaluation_context` 获取 `runtime_driver.driver_id` 和 `runtime_simulator.simulator_id`。如果任一缺失，快速失败。

### 2. 启动 driver 子进程

STEP 3 按以下固定模板直接构造 spawn 命令，路径取自 `run_plan.json` 对应 scenario 的字段（`eval_id`、`tc_id` 为具体值）：

```bash
PAD=/tmp/eval-driver/<eval_id>/<tc_id>
kill $(cat $PAD/pid 2>/dev/null) 2>/dev/null
rm -rf $PAD && mkdir -p $PAD && touch $PAD/in $PAD/out $PAD/err
nohup sh -c 'tail -f '"$PAD/in"' | python3 -u runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation_context.json \
  --enriched-test-case runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output runs/<eval_id>/traces/<tc_id>.trace.json \
  >> '"$PAD/out"' 2>> '"$PAD/err"'' & echo $! > $PAD/pid
```

**pad 布局**（`/tmp/eval-driver/<eval_id>/<tc_id>/`，K19）：
- `in` — 常规文件（非 FIFO）。Agent 向此文件追加 action JSON；`tail -f` 将新行管道传入 driver stdin
- `out` — 常规文件。driver stdout 追加到此处；Agent 用 `read_file` 工具或 `sed -n "${N}p"` 轮询
- `err` — driver stderr
- `pid` — spawn 包装器的 PID，用于后续清理

### 通信通道工作原理（改动前请务必阅读）

Driver 将 `{"event":"ready",...}` 及所有后续事件写入其 **stdout**。spawn 命令通过 `>>` 将 driver stdout 重定向到常规文件 `$PAD/out`。Agent 通过轮询 `$PAD/out` 的行来读取事件——**不是**直接附加到进程。

```
                   tail -f $PAD/in (regular file)
                          │
                          │ pipe (never EOF while tail -f is alive)
                          ▼
driver process stdin ◄────┤
                           │
driver process stdout ──(>> $PAD/out)──→  pad/out (regular file)
                                                   ▲
                              agent polls with sed -n "${N}p"

agent writes action JSON ──(printf >> $PAD/in)──→  pad/in (regular file)
                                                          ▲
                                         tail -f picks up via inotify
```

Agent 不得：
- Use `process_*` tools to attach to the subprocess
- Read driver stdout directly
- 在 pad/in 上使用 `cat`（`tail -f` 是 driver 的机制；Agent 使用 `>>` 追加）
- 以任何方式修改 spawn 命令

关键设计属性：
- **pad/in 是常规文件**（非 FIFO）。Agent 使用 `printf ... >>` 追加动作 JSON 行。追加到常规文件**永远不会阻塞**。
- **tail -f** 监视 pad/in 并将新行通过管道传入 driver 的 stdin。由于它跟踪的是常规文件，永远不会返回 EOF（不像在容器内核上使用 O_RDWR 读取 FIFO）。
- **pad/out 是常规文件**，由 `sed -n` 轮询。无 FIFO 语义——轮询打开时永远不会阻塞。
- 整个管道（`tail -f | python3`）由单个 `sh -c` 包装器持有。杀死包装器 PID 会破坏管道并终止两个进程。

### 3. 读取首行 stdout（通过 pad/out 轮询）

使用 `read_file` 工具读取 `$PAD/out`，或执行 `sed -n "${N}p" $PAD/out`（N 从 1 开始，每次读取后加 1，跟踪已读行数）。将返回行解析为 JSON；期望 `{"event":"ready",...}`。其他任何结果 → 中止此场景的 STEP 3。

### 4. 第 0 轮（确定性，无 LLM）

第 0 轮精确格式（字段名是字面量，写成**单行 JSON**，通过 `printf '%s\n' '...' >> pad/in` 写入）：

```json
{"action":"send","turn_index":0,"text":"<tc.input.opening_message 原文>","decision":{"turn_index":0,"should_continue":true,"next_utterance":"<同 text 字段>","internal_emotion":"neutral","perceived_progress":"none","stop_reason":null}}
```

`decision` 完整字段约束（所有 `send` 和 `end` 动作通用）：

| 字段 | 类型 | `should_continue=true` | `should_continue=false` |
|---|---|---|---|
| `turn_index` | int，等于外层 `turn_index` | 必填 | 必填 |
| `should_continue` | bool（`true`/`false`，非字符串） | `true` | `false` |
| `internal_emotion` | `angry`/`anxious`/`neutral`/`curious`/`satisfied`/`skeptical`/`frustrated` | 必填 | 必填 |
| `perceived_progress` | `none`/`partial`/`resolved`/`regressed` | 必填 | 必填 |
| `next_utterance` | string | 必填 | 可选 |
| `stop_reason` | null | 必须为 `null` | 必须为非 null enum |

`stop_reason` 合法值：`goal_achieved`、`bottom_line_violated`、`deadlock_detected`、`customer_gave_up`。

**单引号安全**：`printf '%s\n' '...'` 将负载包裹在单引号中。文本含 `'` 时转义为 `'\''`。示例：`I can't` → `I can'\''t`。

第 0 轮不得调用 LLM。

### 5. 循环直到终止

每次迭代：

1. 读取下一行 stdout。期望 `{"event":"evaluatee_turn", ...}`。其他任何结果 → 作为错误事件处理。
2. 用占位符渲染 `simulators/<simulator_id>/system_prompt.md`：
   - `customer_persona` / `goal` / `stop_conditions` / `context` / `current_emotion` / `dialog_so_far` / `effective_max_turns`
3. Agent 自身的 LLM 消费渲染后的提示词并返回 `SimulatorDecision` JSON。在向 driver 写入任何内容**之前**根据 `runtime-schemas/simulator_decision.schema.json` 验证。验证失败会阻止该场景；不要写入格式错误的决策，也不要在事后修补 trace。
   如果 driver 在格式错误的动作后返回 `{"event":"error","recoverable":true,...}`，更正动作 JSON 并继续使用同一 driver 进程。不要清理/重新启动，也不要将此计入场景重试。
4. 计算：
   ```
   effective_max_turns = min(tc.turn_budget.hard_max_turns, evaluation_context.global_turn_cap or 30)
   ```
5. 决定写入哪种动作（精确 JSON 格式——使用 `action` 字段，不要使用 `type`）：

   **`send` 动作**（字段名强制且精确）：
   ```json
   {"action":"send","turn_index":<N>,"text":"<utterance text>","decision":{"turn_index":<N>,"should_continue":true,"next_utterance":"<utterance text>","internal_emotion":"<emotion>","perceived_progress":"<none|partial|resolved|regressed>","stop_reason":null}}
   ```

   **`end` action** (field names are mandatory and exact):
   ```json
   {"action":"end","decision":{"turn_index":<N>,"should_continue":false,"internal_emotion":"<final emotion>","perceived_progress":"<none|partial|resolved|regressed>","stop_reason":"<reason>"},"termination":{"reason":"<termination_reason>","detail":"<optional detail>","final_emotion":"<emotion>","turns_used":<N>}}
   ```

   | 条件 | 动作类型 |
   |---|---|
   | `turn_index + 1 >= effective_max_turns` | `end`，`termination.reason = "max_turns_reached"`（无论 `decision.should_continue` 为何值） |
   | `decision.should_continue == false` 且 `decision.next_utterance` 非空 | 先写 `send`（携带 `next_utterance`），**再**写 `end` |
   | `decision.should_continue == false` 且 `next_utterance` 为空 | `end`，`termination.reason` 从 `stop_reason` 映射 |
   | 其他情况 | `send`（携带 `decision.next_utterance`） |

   `stop_reason` 映射：`goal_achieved` → `completed_normally`；`bottom_line_violated` → `bottom_line_violated`；`deadlock_detected` / `customer_gave_up` → `deadlock_detected`。

### 6. 等待 `{"event":"trace_written", ...}`

Driver 写入最终 trace 并退出。`./runs/<eval_id>/traces/<tc_id>.trace.json` 现在是权威的 `ExecutionTrace`。

### 7. 收到 `{"event":"error", ...}` 时的处理

如果 `recoverable == true`，在内部运行日志中显示详情，修正格式错误的动作，并将更正后的下一个动作发送到同一 driver 进程。不要关闭 stdin，不要重启场景。

如果 `recoverable` 不存在或为 false，显示详情并中止场景。Driver 在退出前写入部分 trace。

## 硬性规则：禁止编排脚本（K8）

Agent 在**对话中以交互方式**执行整个循环，而不是通过生成脚本来执行。对话本身**就是**编排器。

技能下允许的可执行文件**仅限**在技能创建时提交的文件：

- `./runtime-drivers/<driver_id>/run.py` 及同目录中的同级文件
- 随技能发布的任何未来 `runtime-*/<id>/` 适配器目录

Agent **不得**在技能根目录下的**任何地方**创建任何新的 `.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / `Makefile` / `*.cmd` / `*.ps1` 文件。包括：

- 编排器 / 运行器 / 协调器脚本（`run_scenario.py`、`run_step3.py`、`run_evaluation.py`、`runner.py`、`orchestrator.py`、`coordinator.py`、`main.py`、`eval.py`、`test_driver.py`、`driver_client.py` 等）
- 渲染提示词、解析 JSON、驱动循环或调用 LLM 端点的辅助脚本
- 链接多个 Agent 职责的内联 shell 脚本

如果 Agent 刚刚将 `subprocess.Popen(... runtime-drivers/...)` 或 `proc.stdin.write(json.dumps(...))` 写入自己创建的文件，那就是 **K8 违规**。同样的逻辑**必须**作为对话中的 **Agent 工具调用轮次**来执行。

可执行文件路径白名单：`./runtime-drivers/<driver_id>/**`、`./runtime-*/<id>/**`。其他任何位置都会污染运行。

## Driver 子进程接线合同（K19）

**K19 (HARD)**：pad 布局为 `/tmp/eval-driver/<eval_id>/<tc_id>/`，包含：`in`（常规文件——Agent 用 `>>` 追加 action；`tail -f` 通过管道流入 driver stdin）、`out`（常规文件——driver stdout 追加到此；Agent 按行号轮询）、`err`（driver stderr）、`pid`（`sh -c` 包装器 PID）。所有 pad 文件均为常规文件——无 FIFO。这避免了容器内核上 O_RDWR FIFO 引用计数竞争导致过早 EOF 的问题。

### 路径约定（STEP 3 运行时构造）

STEP 3 直接按以下规则构造各路径，不从 run_plan.json 读取命令：

| 路径 | 规则 |
|---|---|
| `--evaluation-context` | **固定** `/workspace/runtime/evaluation_context.json`（含完整凭据的原始文件） |
| `--enriched-test-case` | `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json` |
| `--output` | `runs/<eval_id>/traces/<tc_id>.trace.json` |
| pad dir | `/tmp/eval-driver/<eval_id>/<tc_id>` |

> `run_plan.json` 提供 `tc_id`、`opening_message`、`effective_max_turns`；路径按上表规则构造，不从 run_plan 读取。

### 每场景执行顺序

| 阶段 | 执行方式 | 内容 |
|---|---|---|
| **1. 预清理 + spawn** | shell | 按 section 2 的固定模板清理旧 pad、创建新 pad、后台启动 driver |
| **2. 读取首个事件** | `read_file` 或 shell sed | 轮询 `$PAD/out`，期望 `{"event":"ready",...}` |
| **3. 发送第 0 轮** | `write_file`（追加）或 shell `printf >>` | 将 turn-0 `send` action JSON 追加到 `$PAD/in` |
| **4. 循环直到终止** | read/write 交替 | 读取 evaluatee_turn → simulator 决策 → 追加 action → 重复 |
| **5. 场景后清理** | shell | `kill $(cat $PAD/pid 2>/dev/null); rm -rf $PAD`，无论结果如何 |

### K19 自检（场景结束后强制执行）

- `ps` 确认 `runtime-drivers/.*run.py.*<tc_id>` 进程已退出
- `$PAD/out` 末行为可解析的 `{"event":"trace_written",...}` 或 `{"event":"error",...}`
- `runs/<eval_id>/traces/<tc_id>.trace.json` 存在且为合法 JSON

### 反模式

| 反模式 | 问题 |
|---|---|
| 用 `<> $PAD/in`（O_RDWR FIFO）代替 `tail -f` | 容器内核上过早 EOF；driver 立即退出 |
| 用 `cat $PAD/out` 代替按行轮询 | 可能重读历史事件或阻塞 |
| `--evaluation-context runs/<eval_id>/evaluation_context.json` | 该副本可能已脱敏；凭据缺失导致 token 解析失败 |
| 跳过场景后清理 | pid / pad 文件泄漏；下次运行同一 tc 时 kill 错进程 |

## 循环完整性（K14）

Driver 期望严格交替：`send → 读取 evaluatee_turn → send | end`。在写入 `end` 之前关闭 stdin 是**协议违规**，而非优雅关闭。

### Trace 拒绝规则

如果满足以下任意条件，trace 将在 STEP 4 输入门被拒绝：

```
1. termination.reason == "evaluatee_error"
   AND termination.detail 包含 "stdin closed before 'end' action received"

2. termination.reason == "evaluatee_error"
   AND turns_used == 1
   AND actual_tool_calls == []

3. termination.reason == "max_turns_reached"
   AND turns_used < effective_max_turns
   AND simulator_trail[-1].should_continue == true

4. simulator_trail 非空
   AND simulator_trail[-1].next_utterance 是非空字符串
   AND 该字符串不是 actor == "evaluator" 的最后一条 dialog_turns 条目的内容
```

- **条款 3** 捕获"演示快捷方式"缺陷：Agent 在 simulator 仍想继续时自我截断轮次，低于 `effective_max_turns`。
- **条款 4** 捕获 **`runs/eval-soul-001/` "simulator 已决策但 Agent 从未传递"**缺陷：simulator_trail 记录 `next_utterance = "订单号是 ORD…"`，`should_continue=false`，`stop_reason=goal_achieved`，但 `dialog_turns` 显示客户从未实际说出该话，因为 Agent 在发出最终 `send` 前就关闭了 stdin。

### 修复方式：先 send 后 end

只要 `decision.next_utterance` 非空，Agent 就必须先写 `send`（携带该精确文本），再写 `end`——即使 `should_continue==false`。客户的最后一句话（提供订单号、说"谢谢再见"等）是对话的一部分，**必须**出现在 `dialog_turns` 中。

### LLM 渲染在循环中途出错时的恢复

写入 `{"action":"end","decision":{"turn_index":<N>,"should_continue":false,"internal_emotion":"<current emotion>","perceived_progress":"regressed","stop_reason":"deadlock_detected"},"termination":{"reason":"deadlock_detected","detail":"<reason>"}}` **然后**关闭 stdin。**绝不**先关闭 stdin。

### 禁止的快捷方式（K14）

Agent **不得**以"演示"、"预览"、"样例"、"测试"、"简短"或任何其他自创理由提前终止循环。在循环内写入 `end` 的唯一有效原因为：

1. `decision.should_continue == false`（simulator 决定停止）
2. `turn_index + 1 >= effective_max_turns`（硬性预算耗尽）
3. Driver 发出 `{"event":"error"}`（不可恢复的 driver 故障）

### 对称的 simulator 侧规则（K15 运行时切面）

A simulator decision MUST NOT set `goal_progress = "goal_achieved"` or `stop_reason = "goal_achieved"` on the **first** decision after the evaluatee asked the customer for required information (e.g. `order_number`, `refund_id`) UNLESS the customer's reply containing that information has already been delivered to the evaluatee in a prior turn. Self-declaring `goal_achieved` while the required info is still locked inside `next_utterance` trips the trace-rejection rule above (clause 4).

被拒绝的 trace 会污染运行；受影响的 `tc_id` **必须**出现在 `EvaluationReport.open_questions` 中。

## 反模式（每种均为停止并污染）

| 反模式 | K规则 | 解决方式 |
|---|---|---|
| 编写任何 `run_*.py` / `runner.py` / `orchestrator.py` 来驱动循环 | K8 | 在对话中逐轮驱动循环 |
| 在 Agent 编写的代码中使用 `subprocess.Popen([..., 'runtime-drivers/...'])` | K8 | 从单次 shell 工具调用启动 |
| `while True:` 循环将多轮合并为一次执行 | K8 | 每个 Agent 轮次一次往返 |
| 从编写的脚本中向"LLM"发出 HTTP 调用 | K8 | Simulator 就是宿主 LLM |
| `.sh` / `Makefile` 将 spawn 与其他内容链接 | K8 | 每轮单条 shell 命令 |
| 创建 `read_one_event.py` 轮询 driver stdout | K8 | 用 `read_file` 工具或 `sed -n "${N}p" $PAD/out` 内联轮询 |
| 写一个 `send` 后关闭 stdin | K14 | 关闭前始终写 `end` |
| 以"演示"为由将轮次自我截断至低于 `effective_max_turns` | K14 | 让预算自然耗尽 |
| `should_continue=false` 且 `next_utterance` 非空时跳过最终 `send` | K14（条款 4） | 先 send 后 end 模式 |
| Simulator 在客户必要信息到达被评估者前宣告 `goal_achieved` | K15 运行时 | 客户必须先说出必要信息 |
| 每轮临时发明管道文件名（`/tmp/eval-stdin-pipe`、`/tmp/eval-stdout.txt` 等） | K19 | 按 section 2 固定模板使用 `/tmp/eval-driver/<eval_id>/<tc_id>/` 布局 |
| `cat "$PAD/out"`（可能重读陈旧事件） | K19 | 用 `sed -n "${N}p" $PAD/out` 游标式轮询，自行跟踪已读行号 |
| 在 spawn 中用 `<> "$PAD/in"`（O_RDWR FIFO） | K19 | 用 `tail -f "$PAD/in" \| python3 ...`；O_RDWR FIFO 在容器内核上导致提前 stdin EOF |
| 对 `$PAD/in` 用 `mkfifo` 而非 `touch` | K19 | `pad/in` 必须是常规文件，`tail -f` 才不会返回 EOF |
| 跳过预清理或场景后清理 | K19 | 两次清理均为强制步骤；pid / pad 文件泄漏会影响下次同 tc 运行 |
