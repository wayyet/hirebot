# STEP 3 — driveEmployeeOnScenario

**依据**：工作流合同 `S3` + K8  
**输入**：`enriched-cases/<tc_id>.enriched.json`、`/workspace/runtime/evaluation_context.json`  
**输出**：`runs/<eval_id>/traces/<tc_id>.trace.json`（根据 `execution_trace.schema.json` 验证）

## 执行模式

STEP 3 有两种执行模式；**优先使用 Mode A**。

| 模式 | 触发方式 | 适用场景 |
|---|---|---|
| **A — `--auto-simulate`（推荐）** | 命令行加 `--auto-simulate` | 绝大多数场景；`tc.input` 已含 `opening_message` 和 `stop_conditions` |
| **B — 交互式 stdin/stdout** | 不加 `--auto-simulate` | 需要宿主 LLM 扮演复杂多变客户时（保留为高级模式） |

---

## Mode A — `--auto-simulate`（推荐）

run.py 自行驱动所有对话轮次：读取 `tc.input.opening_message` → 发到 WebSocket → 检测 `stop_conditions.failure` → 必要时发追问 → 写 trace 并退出。

**无后台进程，无 pad 文件，无轮询，单次 shell 调用同步等待完成。**

### 命令模板

对每个 `tc_id`，执行一次：

```bash
mkdir -p runs/<eval_id>/traces
python3 runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation_context.json \
  --enriched-test-case runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output runs/<eval_id>/traces/<tc_id>.trace.json \
  --auto-simulate
echo "exit=$?"
```

- 命令完成 → trace 已写入，直接进入 STEP 4。
- 退出码 0 = 正常；1 = 配置/输入错误；2 = 运行时错误（timeout / WebSocket 失败）。

### 内置决策规则（无需 Agent 介入）

| 优先级 | 条件 | 结果 |
|---|---|---|
| 1 | 被评估者回复含合规标志词且命中 `stop_conditions.failure` 关键词 | `bottom_line_violated` 终止 |
| 2 | `turns_used >= effective_max_turns` | `max_turns_reached` 终止 |
| 3 | `turn_index >= 1` 且回复 > 300 字 | `goal_achieved` 终止 |
| 4 | 其他 | 发 `tc.input.follow_up_messages[N]` 或通用追问，继续 |

### 运行后验证

```bash
# trace 文件存在且为合法 JSON
python3 -c "import json,sys; json.load(open('runs/<eval_id>/traces/<tc_id>.trace.json'))" && echo "OK"
```

---

## Mode B — 交互式 stdin/stdout（高级）

仅在 Mode A 无法满足场景需求时使用（如需要宿主 LLM 动态扮演客户）。

### spawn 命令

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

pad 布局（`/tmp/eval-driver/<eval_id>/<tc_id>/`）：

- `in` — 常规文件（非 FIFO）。Agent 用 `printf '%s\n' '...' >>` 追加 action JSON
- `out` — driver stdout；Agent 用 `sed -n "${N}p"` 游标轮询（自行跟踪已读行号）
- `err` — driver stderr
- `pid` — `sh -c` 包装器 PID

### 循环协议

1. 轮询 `$PAD/out` → 期望 `{"event":"ready",...}`
2. 第 0 轮写入（不调用 LLM）：
   ```json
   {"action":"send","turn_index":0,"text":"<opening_message>","decision":{"turn_index":0,"should_continue":true,"next_utterance":"<opening_message>","internal_emotion":"neutral","perceived_progress":"none","stop_reason":null}}
   ```
3. 读取 `{"event":"evaluatee_turn",...}` → 渲染 `simulators/<simulator_id>/system_prompt.md` → LLM 返回 `SimulatorDecision` → 校验 → 写 `send` 或 `end`
4. 收到 `{"event":"trace_written",...}` → 场景结束
5. 场景后清理：`kill $(cat $PAD/pid 2>/dev/null); rm -rf $PAD`

**关键约束**：写 `end` 之前必须已写 `send`（若 `decision.next_utterance` 非空）；不得在写 `end` 之前关闭 stdin。

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
