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

**K20 覆盖——不要直接使用以下命令。** 概念性命令为：

```
python3 -u runtime-drivers/<driver_id>/run.py \
  --evaluation-context <eval_ctx_path> \
  --enriched-test-case <enriched_tc_path> \
  --output ./runs/<eval_id>/traces/<tc_id>.trace.json
```

但 STEP 3 不得组合此命令。而应原文执行 `run_plan.scenarios[i].commands.spawn`（来自预先落盘的 `run_plan.json`）。STEP 2.5 生成的 spawn 命令在上述基础上包装了：
- `nohup ... <> "$PAD/in" >> "$PAD/out" 2>> "$PAD/err" &` — 后台运行 driver 并将其 stdin/stdout/stderr 重定向到 pad 文件
- `echo $! > "$PAD/pid"` — 立即捕获后台 PID

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
                              agent polls with sed -n "${N}p" (commands.read_one_event)

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

原文执行 `run_plan.scenarios[i].commands.read_one_event`。这会轮询 `$PAD/out` 获取下一个未读行（由 `$PAD/cursor` 跟踪）。将返回行解析为 JSON；期望 `{"event":"ready",...}`。其他任何结果 → 中止此场景的 STEP 3。

### 4. 第 0 轮（确定性，无 LLM）

> ⚠️ **两层协议绝对不能混淆（最常见错误来源）**
>
> | 层 | 格式 | 谁写 | 谁消费 |
> |---|---|---|---|
> | **agent → driver stdin**（`pad/in`） | `{"action":"send","turn_index":N,"text":"...","decision":{...}}` | 宿主 Agent | `run.py` |
> | **driver → evaluatee WebSocket** | `{"type":"user_message","text":"..."}` | `ws_client.py` 内部自动 | Gateway / 目标沙箱 |
>
> **Agent 绝不直接写 WebSocket 层格式到 `pad/in`。**  
> ❌ `{"type":"user_message","content":"你好"}` → driver 报 `wrong protocol layer`  
> ❌ `{"action":"send","turn_index":0,"text":"你好"}` （无 `decision`）→ driver 报 `'send' action requires object decision`  
> ❌ `{"action":"send","role":"user","content":"你好"}` → driver 报 `unknown action None`（缺 `decision`，`role`/`content` 字段无效）

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

## Driver 子进程接线合同（K19 + K20——从 run_plan.json 读取字面命令）

**K20 (HARD)**: STEP 3 MUST NOT compose shell commands at runtime. Before STEP 3 begins, STEP 2.5 (`planRun`, see `playbooks/step-2.5-plan-run.md`) has materialised every `(pre_spawn_cleanup, spawn, read_one_event, write_action_template, post_scenario_cleanup)` as **literal shell strings** under `runs/<eval_id>/run_plan.json`. The agent reads `run_plan.scenarios[i].commands.*` and executes the strings verbatim. The ONLY runtime substitution permitted is replacing the marker `<<JSON_PAYLOAD>>` inside `commands.write_action_template` with the current single-line `send`/`end` action JSON.

**K19 (HARD)**: The canonical pad layout `/tmp/eval-driver/<eval_id>/<tc_id>/` contains: `in` (regular file — agent appends actions with `>>`; `tail -f` streams into driver stdin via pipe), `out` (regular file — driver stdout appended here; agent polls by line number), `cursor` (regular file — tracks next unread line number), `err` (regular file — driver stderr), `pid` (regular file — `sh -c` wrapper PID). All pad files are regular files — no FIFOs. This avoids the O_RDWR FIFO reference-count race that caused premature EOF on container kernels. Agents inspecting failures should verify the pad layout matches K19; agents executing the loop should NOT inspect or modify the layout — just run the commands.

Repeated `cat: /tmp/eval-stdout.txt: No such file or directory`-class failures are now K20 violations (STEP 3 improvised instead of reading the plan), not Python instability.

### Where the commands live (read-only)

```
runs/<eval_id>/run_plan.json
   .scenarios[i].tc_id
   .scenarios[i].pad.{dir,in_fifo,out_file,cursor,err_file,pid_file}
   .scenarios[i].commands.pre_spawn_cleanup       ← 原文执行
   .scenarios[i].commands.spawn                   ← 原文执行
   .scenarios[i].commands.read_one_event          ← 原文执行（每个事件）
   .scenarios[i].commands.write_action_template   ← 仅替换 <<JSON_PAYLOAD>>
   .scenarios[i].commands.post_scenario_cleanup   ← 原文执行（成功或失败均执行）
   .scenarios[i].opening_message                  ← 第 0 轮 send 的原文文本
   .scenarios[i].effective_max_turns              ← 已预先计算；运行时不需要 min()
```

上述 pad 文件名由 STEP 2.5 **固定**。禁止临时命名：`/tmp/eval-stdin-pipe`、`/tmp/eval-stdout.txt`、`/tmp/eval_driver_in`、`/tmp/eval_driver_out`、`/tmp/eval-stdin`、`/tmp/eval-stdout`，或任何 Agent 临时发明的名称。

### 每场景执行（六次字面字符串工具调用）

| 阶段 | 工具调用 | Agent 执行内容 |
|---|---|---|
| **1. 预启动清理** | shell | 原文执行 `run_plan.scenarios[i].commands.pre_spawn_cleanup`——不得修改、不得包裹、不得链接 |
| **2. 启动（后台）** | shell | 原文执行 `run_plan.scenarios[i].commands.spawn`；在对话日志中记录 PID 行 |
| **3. 读取首个事件** | shell | 原文执行 `run_plan.scenarios[i].commands.read_one_event`；将该行解析为 JSON；期望 `{"event":"ready",...}` |
| **4. 发送第 0 轮** | shell | 构建 `{"action":"send","turn_index":0,"text":<run_plan 中的 opening_message 原文>,"decision":<确定性第 0 轮决策>}`；序列化为单行 JSON；在 `<<JSON_PAYLOAD>>` 处替换到 `commands.write_action_template`；执行 |
| **5. 循环直到终止** | shell × N | 重复：使用 `commands.read_one_event` 读取 → simulator 决策（宿主 LLM）→ 替换 `<<JSON_PAYLOAD>>` 到 `commands.write_action_template` → 执行。当读取返回 `{"event":"trace_written",...}` 或 `{"event":"error",...}` 时停止 |
| **6. 场景后清理** | shell | 原文执行 `run_plan.scenarios[i].commands.post_scenario_cleanup`，无论结果如何 |

所有六条命令均为字面字符串。Agent **绝不**在运行时决定管道名、解释器路径、`--flag`、重定向或清理顺序。

### K20 自检（STEP 3 进入阶段 1 前强制执行）

对当前场景 `i`：

- `runs/<eval_id>/run_plan.json` 存在且通过 `runtime-schemas/run_plan.schema.json` 验证；
- `run_plan.scenarios[i]` 存在于 Agent 即将运行的 tc；
- 五个 `commands.*` 字符串各自非零长度；
- `commands.spawn` 以场景 `pad.dir` 赋值开始，并使用规范的 `$PAD/in`、`$PAD/out`、`$PAD/err` 和 `$PAD/pid` 叶；
- `commands.read_one_event` 以场景 `pad.dir` 赋值开始，并使用 `sed -n` 以及规范的 `$PAD/out` 和 `$PAD/cursor`；
- `commands.spawn` 包含 `--evaluation-context`、`--enriched-test-case` 和 `--output`，且不含任何遗留的 `--test-case-id` / `--endpoint` / `--pad-in` / `--pad-out` 标志；
- `commands.spawn` 不含 `&;` 标记；在 sh/bash 中，后台 `&` 本身就是命令分隔符；
- `commands.write_action_template` 恰好包含一个 `<<JSON_PAYLOAD>>`；
- 本次对话中没有使用不同命令字符串启动过相同 `tc_id` 的场景（在工具调用记录中搜索引用相同 `tc_id` 的先前 shell 调用）。

### K19 自检（STEP 3 返回场景前强制执行）

场景结束且 `commands.post_scenario_cleanup` 运行后：

- `ps -ef | grep "runtime-drivers/.*run.py" | grep "<tc_id>"` 返回零行；
- `pad.dir` 在磁盘上不再存在；
- 首次 `commands.read_one_event` 返回了可解析的 `{"event":"ready",...}`（非空，非 Python traceback）；
- 此场景的每次 shell 工具调用均引用 `run_plan.scenarios[i].pad.*` 中的精确 pad 路径（不出现其他 `/tmp/eval-*` 名称）。

### 反模式（每种均为 K19 或 K20 违规——用户一直看到的症状）

| 反模式 | 症状 | 解决方式 |
|---|---|---|
| 在 STEP 3 中从头组合 `mkfifo /tmp/eval-stdin-pipe; ... > /tmp/eval-stdout.txt` | `cat: /tmp/eval-stdout.txt: No such file or directory`；PID 泄漏 | 原文执行 `run_plan.json` 中的 `commands.*`；如果计划中没有，重新运行 STEP 2.5 |
| Modify `commands.spawn` to add `2>&1` / change redirection / swap python binary | One scenario behaves differently than the rest; flaky runs | Re-run STEP 2.5 with the desired change wired into the plan generator |
| Use `cat "$PAD/out"` instead of the plan's cursor-based `sed -n "${N}p" ...` poller | Tool-call can block or re-read stale events | Use `commands.read_one_event` verbatim |
| 跳过 `commands.pre_spawn_cleanup` 或 `commands.post_scenario_cleanup` | `ps aux` 条目陈旧；pad 文件在 `/tmp/eval-driver/` 下泄漏 | 两次清理均为强制工具调用；它们在计划中是有原因的 |
| 替换 `<<JSON_PAYLOAD>>` 以外的任何内容（例如替换不同的 `pad.in_fifo`） | Driver 未收到任何内容，因为写入了不存在的文件 | 只有标记是可变的；其他所有内容都是只读字面量 |
| 在 `run_plan.json` 不存在时运行 STEP 3 | 临时发挥循环重现；用户再次看到"Exit code 1" | STEP 2.5 输入门：STEP 3 拒绝启动；先重新运行 STEP 2.5 |

### 为什么这是合同而非建议

Driver 协议（`ready` → `send`/`evaluatee_turn` × N → `end`/`trace_written`）是正确且稳定的。用户一直看到的每类错误（"Exit code 1"、"No such file or directory"、"PID still alive after cleanup"、144 噪声）都是由**运行时字符串组合**产生的，而非 `run.py` 的问题。在 STEP 2.5 中落盘命令并在 STEP 3 中原文读取，消除了整个失败面，并将每场景 STEP-3 的开销缩短为：每轮 `1 次读取 + 1 次替换 + 1 次执行`，无需逐轮编写 shell。

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
| 创建 `read_one_event.py` 轮询 driver stdout | K8 / K20 | 原文使用 `run_plan.scenarios[i].commands.read_one_event`；它已经是内联游标式 shell 轮询器 |
| 写一个 `send` 后关闭 stdin | K14 | 关闭前始终写 `end` |
| 以"演示"为由将轮次自我截断至低于 `effective_max_turns` | K14 | 让预算自然耗尽 |
| `should_continue=false` 且 `next_utterance` 非空时跳过最终 `send` | K14（条款 4） | 先 send 后 end 模式 |
| Simulator 在客户必要信息到达被评估者前宣告 `goal_achieved` | K15 运行时 | 客户必须先说出必要信息 |
| 每轮临时发明管道文件名（`/tmp/eval-stdin-pipe` + `/tmp/eval-stdout.txt` 等） | K19 / K20 | 从 `run_plan.json#scenarios[i].commands.*` 读取字面命令；不要在运行时编写 shell |
| 将 driver stdout 重定向到临时 `*.txt` 文件而非计划中的 `pad.out_file` | K19 / K20 | 计划中的 `commands.spawn` 追加到 `pad.out_file`；原文执行 |
| `cat "$PAD/out"`（可能阻塞或重读陈旧事件） | K19 / K20 | 原文使用 `commands.read_one_event`（游标式 `sed -n "${N}p"` 轮询） |
| 在 spawn 命令中使用 `<> "$PAD/in"`（O_RDWR FIFO 打开） | K19 / K20 / 已废弃 | 使用 run_plan 中的 `tail -f "$PAD/in" \| exec python3 ...` 模式；O_RDWR FIFO 在容器内核上导致提前 stdin EOF |
| 在 pre_spawn_cleanup 中使用 `mkfifo "$PAD/in"` | K19 / K20 / 已废弃 | 使用 `touch "$PAD/in"`——pad/in 对于 tail -f 必须是常规文件 |
| 跳过预启动或场景后清理 | K19 / K20 | `commands.pre_spawn_cleanup` 和 `commands.post_scenario_cleanup` 均为强制工具调用 |
| 在 `runs/<eval_id>/run_plan.json` 不存在时开始 STEP 3 | K20 | 先运行 STEP 2.5；计划缺失时 STEP 3 快速失败 |
| 修改 `commands.*` 中除替换 `<<JSON_PAYLOAD>>` 外的任何字符串 | K20 | 将变更纳入计划生成器后重新运行 STEP 2.5 |
