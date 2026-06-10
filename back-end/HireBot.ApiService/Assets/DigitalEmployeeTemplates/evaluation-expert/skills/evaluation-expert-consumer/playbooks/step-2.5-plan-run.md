# STEP 2.5 — planRun（将执行计划记录落盘）

**类型**：确定性（无 LLM）
**依据**：工作流合同 `S2.5`（新增）+ K20
**输入**：`evaluation_context.json`（STEP-6 落盘后或 STEP-2 enrichment 后，见排序说明），以及所有 `runs/<eval_id>/enriched-cases/<tc_id>.json`
**输出**：`runs/<eval_id>/run_plan.json`（根据 `runtime-schemas/run_plan.schema.json` 验证）

## 本步骤存在的原因

STEP 3 过去要求 Agent **在每一轮临时拼写 shell 命令**：选择管道名、决定 Python 解释器、格式化 `--enriched-test-case` 参数、在命令间管道名不匹配时重试，等等。这种临时发挥正是反复出现的 `cat: /tmp/eval-stdout.txt: No such file or directory` / 陈旧 PID / 144 退出码等失败的根源。

STEP 2.5 彻底消除了这个问题面。STEP 2 完成所有测试用例的丰富化之后，启动每个场景 driver 所需的所有信息已经已知且确定。STEP 2.5 将这些信息冻结到一个**字面 shell 字符串**文件中。STEP 3 随后变为一个轻量执行器：对每个场景，原文执行 `commands.pre_spawn_cleanup`，再原文执行 `commands.spawn`，用 `commands.read_one_event` 原文读取，用 `commands.write_action_template`（只替换 JSON 负载）写入，最后原文执行 `commands.post_scenario_cleanup`。

## STEP 2.5 的运行时机

STEP 2 生成所有 `enriched-cases/<tc_id>.json`，且 `evaluation_context.runtime_driver.driver_id` / `runtime_simulator.simulator_id` / `global_turn_cap` 均已确定后，立即运行。如果 `evaluation_context.json` 在更晚时（某些流程中为 STEP 6）才落盘，STEP 2.5 仍仅依赖 **Inputs** 中列出的子集——`runtime_driver.driver_id`、`runtime_driver.driver_config`（用于健全性检查）和 `global_turn_cap`。计划写入不依赖指标注册表、分数或报告。

## 执行流程

```
1. Read:
   - eval_id ← evaluation_context.evaluation_id
   - driver_id ← evaluation_context.runtime_driver.driver_id
   - run_py_path = "runtime-drivers/<driver_id>/run.py"
   - global_cap ← evaluation_context.global_turn_cap or 30
   - python_bin ← `python3`   # sandbox always provides python3; do NOT switch per-scenario
   - cwd ← <absolute path to the evaluation-expert-consumer directory>
   - scenarios_inputs ← list of every persisted enriched-cases/<tc_id>.enriched.json

2. Sanity-check (fail-fast unless a self-heal is defined below; any unrecovered failure ⇒ DO NOT write run_plan.json):
   - run_py_path exists and is executable
   - `python3` is available (`which python3`); if not found, fail-fast
   - dependency handling is fully automatic: if `websockets` is missing, install `runtime-drivers/ws_jwt/requirements.txt` once and continue; `ws_jwt/run.py` also performs the same one-shot fallback, so STEP 3 MUST NOT pause for user confirmation on `ModuleNotFoundError: websockets`
   - **websockets dependency prep (AUTOMATIC — do NOT ask the user)**: run `python3 -c "import websockets"` (single shell call); if exit-code ≠ 0, silently run `pip install -r runtime-drivers/ws_jwt/requirements.txt` once and continue; this is a sandbox-local operation that does not touch the evaluatee or business data; if pip fails, fail-fast with the install error
   - cwd ends with "/evaluation-expert-consumer"
   - len(scenarios_inputs) ≥ 1
   - every enriched tc has non-empty input.opening_message and turn_budget.hard_max_turns ≥ 1

3. For each enriched tc, compute the scenario plan entry:
   tc_id                    ← tc.test_case_id
   enriched_tc_path         = f"runs/{eval_id}/enriched-cases/{tc_id}.enriched.json"   # 注意 .enriched.json 后缀
   evaluation_context_path  = "/workspace/runtime/evaluation-context.json"
   # ALWAYS use the original runtime context — never a run_dir copy.
   # Any copy the agent makes may have credentials sanitized (e.g. client_secret → "REDACTED"),
   # which breaks the driver's client_credentials token fetch.
   trace_path               = f"runs/{eval_id}/traces/{tc_id}.trace.json"
   effective_max_turns      = min(tc.turn_budget.hard_max_turns, global_cap)
   opening_message          = tc.input.opening_message
   pad.dir                  = f"/tmp/eval-driver/{eval_id}/{tc_id}"
   pad.in_fifo              = f"{pad.dir}/in"      # regular file: agent appends actions with >>; tail -f streams into driver stdin
   pad.out_file             = f"{pad.dir}/out"     # regular file: driver stdout appended here; agent polls
   pad.cursor               = f"{pad.dir}/cursor"  # regular file: next unread line number in out_file (1-based)
   pad.err_file             = f"{pad.dir}/err"
   pad.pid_file             = f"{pad.dir}/pid"

4. For each scenario, compose the FIVE literal shell strings (no leftover `<placeholder>`):

   commands.pre_spawn_cleanup =
     f'PAD={pad.dir}; if [ -f "$PAD/pid" ]; then kill -TERM "$(cat "$PAD/pid")" 2>/dev/null; sleep 1; kill -KILL "$(cat "$PAD/pid")" 2>/dev/null; fi; rm -rf "$PAD"; mkdir -p "$PAD"; touch "$PAD/in"; touch "$PAD/out"; echo "pad ready: $PAD"'
     # pad/in is a regular file (NOT a FIFO). The agent appends action JSON lines
     # with >>, and tail -f streams them into the driver's stdin via a pipe.
     # Using a regular file avoids FIFO open-blocking and O_RDWR reference-count
     # races that cause premature EOF on container kernels.
     # pad/out is a pre-created regular file. Stdout goes to a file so the driver
     # never blocks waiting for a reader to open a FIFO.

   commands.spawn =
     f'PAD={pad.dir}; nohup sh -c \'tail -f "$1" 2>/dev/null | exec {python_bin} -u {run_py_path} --evaluation-context {evaluation_context_path} --enriched-test-case {enriched_tc_path} --output {trace_path}\' _ "$PAD/in" >> "$PAD/out" 2>> "$PAD/err" & DPID=$!; echo $DPID > "$PAD/pid"; sleep 0.3; echo "driver pid=$DPID"'
     # STEP 2.5 MUST generate canonical flags: --evaluation-context, --enriched-test-case, --output.
     # --evaluation-context MUST point to /workspace/runtime/evaluation-context.json (the original
     # runtime context written by C# at sandbox creation). NEVER use a run_dir copy — the agent
     # sanitizes credentials when writing files to disk (client_secret → "REDACTED"), which
     # breaks the client_credentials token fetch. The original file is written by the C# host and
     # is never touched by the agent, so its credentials are always intact.
     # DO NOT add --token or any other flag. The driver resolves its Bearer token
     # internally at startup via evaluation_context.hirebot_api.auth (client_credentials).
     # Adding --token is an error that will cause argparse to exit 2.
     #
     # stdin pipeline: tail -f follows pad/in (a regular file) and pipes new lines to the driver's
     # stdin. The sh -c wrapper owns the entire pipeline. $DPID captures the sh PID; killing it
     # breaks the pipe, tail -f exits on SIGPIPE, and the driver's readline() returns EOF.
     # The agent writes action JSONs with printf >> "$PAD/in" — appending to a regular file
     # NEVER blocks, regardless of whether the driver is reading.
     #
     # Replaces the old FIFO + <> (O_RDWR) + keeper approach which caused "stdin closed before
     # 'end' action received" (turns_used=0) on container kernels where O_RDWR on a FIFO does not
     # properly maintain the write-end reference count, causing readline() to return EOF immediately.
     # The tail -f approach avoids all FIFO semantics: pad/in is a regular file, tail -f never
     # returns EOF on a regular file, and the pipe stays open as long as tail -f is alive.

   commands.read_one_event =
     f'PAD={pad.dir}; N=$(cat "$PAD/cursor" 2>/dev/null || echo 1); DEADLINE=$(($(date +%s)+210)); while [ "$(date +%s)" -lt "$DEADLINE" ]; do L=$(sed -n "${N}p" "$PAD/out" 2>/dev/null); if [ -n "$L" ]; then printf "%d\n" $((N+1)) > "$PAD/cursor"; printf "%s\n" "$L"; exit 0; fi; if [ -f "$PAD/pid" ] && ! kill -0 "$(cat \"$PAD/pid\")" 2>/dev/null; then printf \'{{{"event":"error","detail":"driver process died"}}}\'\n\'; exit 1; fi; sleep 0.3; done; printf \'{{{"event":"error","detail":"read_one_event timeout after 210s"}}}\'\n\''
     # Polls pad/out (regular file) for line N. Writes N+1 to cursor on success.
     # No FIFO involved — never blocks on open.
     # Times out after 210 s (driver WS timeout 180 s + 30 s safety margin) with a synthetic error event.
     # Driver liveness check: if pad/pid exists but the process is gone, exit immediately
     # instead of waiting the full 210 s — gives fast-fail on driver crashes.
     # Shell variable ${N} is runtime shell; only {pad.dir} is substituted at plan-generation time.
     # This MUST remain an inline shell string. Do not reference read_one_event.py,
     # python, python3, or any helper file. Creating such a helper during a run is
     # a K8 violation and taints the run.

   commands.write_action_template =
     f"printf '%s\\n' '<<JSON_PAYLOAD>>' >> {pad.in_fifo}"
     # The agent substitutes <<JSON_PAYLOAD>> with the single-line action JSON.
     # CRITICAL: the payload is wrapped in single quotes by this printf command.
     # If the action text contains a single-quote character ('), it MUST be
     # escaped as '\'' (end-quote, backslash-quote, start-quote) in the substitution.
     # Example: text "I can't help" → payload fragment: I can'\''t help
     # Failure to escape will silently truncate or corrupt the written line.
     # pad/in is a regular file — appending with >> NEVER blocks. The tail -f
     # process picks up new lines via inotify and pipes them into the driver.

   commands.post_scenario_cleanup =
     f'PAD={pad.dir}; if [ -f "$PAD/pid" ]; then PID="$(cat "$PAD/pid")"; if kill -0 "$PID" 2>/dev/null; then kill -TERM "$PID"; sleep 1; kill -KILL "$PID" 2>/dev/null; fi; fi; pkill -f "tail -f $PAD/in" 2>/dev/null || true; tail -n 20 "$PAD/err" 2>/dev/null; rm -rf "$PAD"; echo "pad cleaned"'
     # Kill the sh wrapper PID first (this breaks the tail -f | driver pipeline).
     # Then pkill any orphaned tail -f process as a safety net.

5. Assemble the RunPlan object, validate against `runtime-schemas/run_plan.schema.json`,
   then write to `runs/<eval_id>/run_plan.json`.
```

## 输出格式：run_plan.json 完整骨架（必须严格遵守字段名）

> **K20 核心要求**：`run_plan.json` 是唯一的字面命令来源。STEP 3 仅凭 `commands.*` 字段名访问命令，**任何字段名拼写错误都会导致 STEP 3 静默跳过该命令**。
>
> 下方 `<COMPUTED_*>` 占位符必须全部替换为步骤 3–4 计算出的字面值，生成文件中**不得出现任何 `<placeholder>`**。

```jsonc
{
  "schema_version": "1.0",          // 固定字符串，不可改
  "eval_id": "<eval_id>",
  "generated_at": "<ISO 8601 timestamp>",
  "generated_by_step": "STEP 2.5 planRun",   // 固定字符串，不可改
  "driver": {
    "driver_id": "<driver_id>",               // e.g. "ws_jwt"
    "run_py_path": "runtime-drivers/<driver_id>/run.py"
  },
  "python_bin": "python3",
  "cwd": "<absolute path ending with /evaluation-expert-consumer>",
  "scenarios": [
    {
      "tc_id": "<tc_id>",                      // ← 字段名必须是 tc_id，不是 test_case_id
      "enriched_tc_path": "/workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/enriched-cases/<tc_id>.enriched.json",
      "evaluation_context_path": "/workspace/runtime/evaluation-context.json",
      //                         ↑ 必须指向 C# 写入的原始文件，绝不使用 run_dir 副本
      //                           run_dir 副本可能已被 Agent 过滤掉 client_secret
      "trace_path": "/workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/traces/<tc_id>.trace.json",
      //            ↑ driver --output 的目标路径；必须在 workspace 内，不是 /tmp
      "effective_max_turns": <COMPUTED_INT>,
      "opening_message": "<COMPUTED_opening_message verbatim>",
      "pad": {
        "dir":      "/tmp/eval-driver/<eval_id>/<tc_id>",
        "in_fifo":  "/tmp/eval-driver/<eval_id>/<tc_id>/in",
        "out_file": "/tmp/eval-driver/<eval_id>/<tc_id>/out",
        "cursor":   "/tmp/eval-driver/<eval_id>/<tc_id>/cursor",
        "err_file": "/tmp/eval-driver/<eval_id>/<tc_id>/err",
        "pid_file": "/tmp/eval-driver/<eval_id>/<tc_id>/pid"
      },
      "commands": {
        "pre_spawn_cleanup": "<COMPUTED — see §4 commands.pre_spawn_cleanup>",
        //                    ↑ 字段名必须是 pre_spawn_cleanup，不是 setup_pad
        "spawn": "<COMPUTED — see §4 commands.spawn>",
        "read_one_event": "<COMPUTED — see §4 commands.read_one_event>",
        //                 ↑ 必须是 shell sed 轮询；禁止使用 python3 或 heredoc inline 脚本
        "write_action_template": "<COMPUTED — see §4 commands.write_action_template>",
        //                        ↑ 必须包含且只包含一个 <<JSON_PAYLOAD>> 标记
        "post_scenario_cleanup": "<COMPUTED — see §4 commands.post_scenario_cleanup>"
        //                        ↑ 字段名必须是 post_scenario_cleanup，不是 teardown_pad
      }
    }
    // …每个 enriched tc 一条
  ]
}
```

### 常见字段名错误速查（来自实际运行日志）

| 错误写法 | 正确写法 | 后果 |
|---|---|---|
| `test_case_id` | `tc_id` | schema 验证失败；STEP 3 找不到 tc_id |
| `setup_pad` | `pre_spawn_cleanup` | STEP 3 执行 `commands.pre_spawn_cleanup` → undefined → 报错 |
| `teardown_pad` | `post_scenario_cleanup` | STEP 3 执行 `commands.post_scenario_cleanup` → undefined → 不清理 |
| `read_one_event` 用 Python inline | `read_one_event` 必须是 shell sed | schema 显式禁止 `python3?`；且 Python 脚本无超时循环，事件未就绪时静默返回空，导致 STEP 3 卡死 |
| `--output /tmp/.../driver_output.jsonl` | `--output runs/<eval_id>/traces/<tc_id>.trace.json` | trace 写入 /tmp，STEP 4 找不到 |
| `--evaluation-context runs/.../evaluation_context.json` | `--evaluation-context /workspace/runtime/evaluation-context.json` | 使用 run_dir 副本（client_secret 可能已被 REDACTED） |

## STEP 3 开始前的自检（K20）

以下所有条件必须成立；任何失败均意味着 STEP 2.5 未能干净运行，STEP 3 不得开始：

- `runs/<eval_id>/run_plan.json` 存在、是合法 JSON，且通过 `runtime-schemas/run_plan.schema.json` 验证；
- `run_plan.scenarios[].tc_id` 是 `enriched-cases/*.enriched.json` 文件名的精确集合（无缺失 tc，无孤立 tc）；
- 每个 `run_plan.scenarios[].commands.spawn` 包含与同一条目中 `pad.dir`、规范的 `$PAD/in`、`$PAD/out`、`$PAD/err`、`$PAD/pid`、`python_bin`、`driver.run_py_path`、`--evaluation-context`、`--enriched-test-case` 和 `--output` 匹配的字面子字符串（无残留 `<placeholder>`；无遗留 `--test-case-id` / `--endpoint` / `--pad-in` / `--pad-out`）；
- 每个 `run_plan.scenarios[].commands.spawn` 使用 `tail -f "$1" 2>/dev/null | exec ...` 模式（**不得**使用废弃的 `<> "$PAD/in"` O_RDWR FIFO 方式）；
- 每个 `run_plan.scenarios[].commands.spawn` 不含 `&;` 标记（后台 `&` 本身已是命令分隔符）；
- 每个 `run_plan.scenarios[].commands.read_one_event` 包含与 `pad.dir`、规范的 `$PAD/out`、`$PAD/cursor` 和 `sed -n` 匹配的字面子字符串，且不含 `read_one_event.py`、`python` 或 `python3`；
- 每个 `run_plan.scenarios[].commands.write_action_template` 恰好包含一个标记 `<<JSON_PAYLOAD>>`；
- 没有两个场景共享相同的 `pad.dir`（tc 运行间确定性隔离）；
- `run_plan.generated_by_step == "STEP 2.5 planRun"`（防止手写或 LLM 生成的计划）。

## STEP 3 消费此计划的方式（绑定合同）

在 STEP 3 中，对 `run_plan.scenarios` 中的每个场景，Agent：

| 阶段 | Agent 执行内容 |
|---|---|
| 1 | 原文执行 `commands.pre_spawn_cleanup`（单次 shell 工具调用） |
| 2 | 原文执行 `commands.spawn`（单次 shell 工具调用） |
| 3 | 原文执行 `commands.read_one_event`；将返回行解析为 JSON；期望 `{"event":"ready",...}` |
| 4 | 构建首个动作 JSON `{"action":"send","turn_index":0,"text":<run_plan 中的 opening_message 原文>,"decision":<确定性第 0 轮决策>}`；序列化为单行 JSON 字符串；在 `<<JSON_PAYLOAD>>` 标记处替换到 `commands.write_action_template`；执行结果字符串 |
| 5 | 循环：执行 `commands.read_one_event` → 解析 → simulator 决策 → 替换 `<<JSON_PAYLOAD>>` 到 `commands.write_action_template` → 执行。直到读取返回 `{"event":"trace_written",...}` 或 `{"event":"error",...}` 时停止 |
| 6 | 原文执行 `commands.post_scenario_cleanup`，无论结果如何 |

Agent 不得重构或修改 `commands.*` 中任何字符串，只能替换单个 `<<JSON_PAYLOAD>>` 标记。添加 `2>&1`、改变重定向、用 `cat` / `tail` 替换游标式 `sed -n "${N}p"` 轮询器，或将 spawn 拆分为两次工具调用，均为 K20 违规。

## 重新计划规则

如果 STEP 2.5 写入 `run_plan.json` 后输入发生任何变化（driver_id 替换、新增丰富 tc、evaluation_context 重命名等），STEP 2.5 必须端到端重新运行。禁止手动部分编辑 `run_plan.json`（`generated_by_step` 字面量 + `generated_at` 时间戳锚定审计链）。

## 反模式（每种均为 K20 违规）

| 反模式 | 症状 | 解决方式 |
|---|---|---|
| STEP 3 在 `run_plan.json` 不存在时开始 | 复现同样的"临时 shell"问题 | STEP 2.5 输入门；快速失败 |
| Agent 重写 `commands.spawn` 以添加 `--verbose` / 更改重定向 | Driver 跨场景行为不同；一次性 bug | 将所需变更纳入计划生成器后重新运行 STEP 2.5 |
| 计划包含残留的 `<placeholder>`（除唯一允许的 `<<JSON_PAYLOAD>>` 外） | Driver 因 argv 中有字面尖括号而退出 1 | 模式 `pattern: "<<JSON_PAYLOAD>>"` 拒绝；STEP 2.5 必须重新生成 |
| 两个场景共享相同的 `pad.dir` | 第二个场景继承第一个场景的陈旧文件；非确定性挂起 | `pad.dir = /tmp/eval-driver/<eval_id>/<tc_id>` 结构上唯一；K20 自检拒绝重复 |
| 场景间手动编辑 `run_plan.json` | 审计链断裂；可重现性丧失 | STEP 2.5 后将 run_plan.json 视为只读；任何变更 ⇒ 重新生成 |
| 通过 Agent 编写的脚本 `scripts/make_plan.py` 生成 `run_plan.json` | K8 违规叠加 K20 | STEP 2.5 逻辑在对话中内联运行（确定性文件操作 + 字符串模板） |
| 生成或引用 `runtime-drivers/ws_jwt/read_one_event.py` | K8 违规；陈旧计划自愈会创建污染运行 | 按本操作手册将 `commands.read_one_event` 重新生成为内联游标式 `sed -n` shell 字符串 |
| 在 spawn/pre_spawn_cleanup 中使用 `<> "$PAD/in"` 或 `mkfifo` | "stdin closed before 'end' action received"，turns_used=0 | 使用本操作手册中的 `tail -f` 管道模式；FIFO O_RDWR 竞争在容器内核上导致提前 stdin EOF |
