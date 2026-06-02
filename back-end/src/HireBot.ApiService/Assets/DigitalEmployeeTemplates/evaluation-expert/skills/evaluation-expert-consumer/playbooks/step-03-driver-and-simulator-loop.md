# STEP 3 — driveEmployeeOnScenario (driver + simulator dual-role loop)

**Kind**: dual-role (I/O subprocess + host-agent simulator)
**Authority**: workflow contract `S3` + K8 + K14 + K15 (runtime facet)
**Inputs**: enriched test case, `evaluation_context.runtime_driver`, `evaluation_context.runtime_simulator`, `evaluation_context.global_turn_cap`
**Output**: `./runs/<eval_id>/traces/<tc_id>.trace.json` (validated against `execution_trace.schema.json`)

## Asymmetric execution model

| Role | Execution | Lives in |
|---|---|---|
| `runtime_driver` | **Subprocess** — line-delimited JSON over stdin/stdout | `./runtime-drivers/<driver_id>/` |
| `runtime_simulator` | **NOT a subprocess** — role profile consumed by the host agent's own LLM | `./simulators/<simulator_id>/` |

The driver does protocol I/O (WebSocket / JWT / TLS / tool approval). The simulator decides what the customer says — same brain that runs STEP 1.5 / 4 / 8 / 9. The two communicate via the line-JSON protocol below.

## Per-scenario loop (one shell command per agent turn)

For each enriched test case `tc`:

### 1. Resolve

`runtime_driver.driver_id` and `runtime_simulator.simulator_id` from `evaluation_context`. Fail-fast if either is missing.

### 2. Spawn the driver subprocess

```
python -u runtime-drivers/<driver_id>/run.py \
  --evaluation-context <eval_ctx_path> \
  --test-case <enriched_tc_path> \
  --output ./runs/<eval_id>/traces/<tc_id>.trace.json
```

One driver process per scenario. No long-running daemon for the whole evaluation.

### 3. Read first stdout line

Must be `{"event":"ready", ...}`. Anything else → abort STEP 3 for this scenario.

### 4. Turn 0 (deterministic, no LLM)

Write the first `send` action:

```json
{"action":"send","turn_index":0,"text":"<tc.input.opening_message verbatim>","decision":<deterministic turn-0 SimulatorDecision>}
```

DO NOT consult the LLM for turn 0.

### 5. Loop until termination

Each iteration:

1. Read next stdout line. Expect `{"event":"evaluatee_turn", ...}`. Anything else → handle as error event.
2. Render `simulators/<simulator_id>/system_prompt.md` against placeholders:
   - `customer_persona` / `goal` / `stop_conditions` / `context` / `current_emotion` / `dialog_so_far` / `effective_max_turns`
3. The agent's own LLM consumes the rendered prompt and returns a `SimulatorDecision` JSON. Validate against `runtime-schemas/simulator_decision.schema.json`.
4. Compute:
   ```
   effective_max_turns = min(tc.turn_budget.hard_max_turns, evaluation_context.global_turn_cap or 30)
   ```
5. Decide which action to write:

   | Condition | Action |
   |---|---|
   | `turn_index + 1 >= effective_max_turns` | `end` with `termination.reason = "max_turns_reached"` (regardless of `decision.should_continue`) |
   | `decision.should_continue == false` AND `decision.next_utterance` non-empty | first write `send` carrying `next_utterance`, THEN write `end` |
   | `decision.should_continue == false` AND `next_utterance` empty | `end` with `termination.reason` mapped from `stop_reason` |
   | otherwise | `send` carrying `decision.next_utterance` |

   `stop_reason` mapping: `goal_achieved` → `completed_normally`; `bottom_line_violated` → `bottom_line_violated`; `deadlock_detected` / `customer_gave_up` → `deadlock_detected`.

### 6. Wait for `{"event":"trace_written", ...}`

The driver writes the final trace and exits. The trace file at `./runs/<eval_id>/traces/<tc_id>.trace.json` is now the authoritative `ExecutionTrace`.

### 7. On `{"event":"error", ...}`

Surface the detail and abort the scenario. The driver writes a partial trace before exit.

## HARD RULE: no orchestrator scripts (K8)

The agent does this entire loop **interactively in the conversation**, not by generating a script. The conversation itself IS the orchestrator.

The ONLY executable files allowed under the skill are the ones committed at skill-creation time:

- `./runtime-drivers/<driver_id>/run.py` and its sibling files in the same directory
- any future `runtime-*/<id>/` adapter directory shipped with the skill

The agent MUST NOT create ANY new `.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / `Makefile` / `*.cmd` / `*.ps1` file ANYWHERE under the skill root. This includes:

- orchestrator / runner / coordinator scripts (`run_scenario.py`, `run_step3.py`, `run_evaluation.py`, `runner.py`, `orchestrator.py`, `coordinator.py`, `main.py`, `eval.py`, `test_driver.py`, `driver_client.py`, ...)
- helper scripts that render prompts, parse JSON, drive the loop, or call an LLM endpoint
- inline shell scripts that chain multiple agent responsibilities

If the agent has just written `subprocess.Popen(... runtime-drivers/...)` or `proc.stdin.write(json.dumps(...))` into a file it authored, that is a **K8 violation**. The same logic MUST live as **agent tool-call rounds in the conversation** instead.

Path whitelist for executables: `./runtime-drivers/<driver_id>/**`, `./runtime-*/<id>/**`. Anything else taints the run.

## Driver subprocess wiring contract (K19 + K20 — read literal commands from run_plan.json)

**K20 (HARD)**: STEP 3 MUST NOT compose shell commands at runtime. Before STEP 3 begins, STEP 2.5 (`planRun`, see `playbooks/step-2.5-plan-run.md`) has materialised every `(pre_spawn_cleanup, spawn, read_one_event, write_action_template, post_scenario_cleanup)` as **literal shell strings** under `runs/<eval_id>/run_plan.json`. The agent reads `run_plan.scenarios[i].commands.*` and executes the strings verbatim. The ONLY runtime substitution permitted is replacing the marker `<<JSON_PAYLOAD>>` inside `commands.write_action_template` with the current single-line `send`/`end` action JSON.

**K19 (HARD)**: The canonical FIFO pad layout `/tmp/eval-driver/<eval_id>/<tc_id>/{in,out,err,pid}` is the structural rule the pre-materialised commands obey. Agents inspecting failures should verify the pad layout matches K19; agents executing the loop should NOT inspect or modify the layout — just run the commands.

Repeated `cat: /tmp/eval-stdout.txt: No such file or directory`-class failures are now K20 violations (STEP 3 improvised instead of reading the plan), not Python instability.

### Where the commands live (read-only)

```
runs/<eval_id>/run_plan.json
   .scenarios[i].tc_id
   .scenarios[i].pad.{dir,in_fifo,out_fifo,err_file,pid_file}
   .scenarios[i].commands.pre_spawn_cleanup       ← execute verbatim
   .scenarios[i].commands.spawn                   ← execute verbatim
   .scenarios[i].commands.read_one_event          ← execute verbatim (per event)
   .scenarios[i].commands.write_action_template   ← substitute <<JSON_PAYLOAD>> only
   .scenarios[i].commands.post_scenario_cleanup   ← execute verbatim (success OR error)
   .scenarios[i].opening_message                  ← verbatim text for turn-0 send
   .scenarios[i].effective_max_turns              ← already pre-computed; no min() at runtime
```

The pad file names listed above are FIXED by STEP 2.5. Forbidden ad-hoc names: `/tmp/eval-stdin-pipe`, `/tmp/eval-stdout.txt`, `/tmp/eval_driver_in`, `/tmp/eval_driver_out`, `/tmp/eval-stdin`, `/tmp/eval-stdout`, or anything else the agent invents on the fly.

### Per-scenario execution (six tool-calls of literal strings)

| Phase | Tool call | What the agent does |
|---|---|---|
| **1. Pre-spawn cleanup** | shell | Execute `run_plan.scenarios[i].commands.pre_spawn_cleanup` **verbatim** — not modified, not wrapped, not chained |
| **2. Spawn (background)** | shell | Execute `run_plan.scenarios[i].commands.spawn` **verbatim**; record the PID line in the conversation log |
| **3. Read first event** | shell | Execute `run_plan.scenarios[i].commands.read_one_event` **verbatim**; parse the one line as JSON; expect `{"event":"ready",...}` |
| **4. Send turn 0** | shell | Build `{"action":"send","turn_index":0,"text":<opening_message verbatim from run_plan>,"decision":<deterministic turn-0 decision>}`; serialise to a single line of JSON; substitute it into `commands.write_action_template` at `<<JSON_PAYLOAD>>`; execute |
| **5. Loop until termination** | shell × N | Repeat: read with `commands.read_one_event` → simulator decision (host LLM) → substitute `<<JSON_PAYLOAD>>` into `commands.write_action_template` → execute. Stop when read returns `{"event":"trace_written",...}` or `{"event":"error",...}` |
| **6. Post-scenario cleanup** | shell | Execute `run_plan.scenarios[i].commands.post_scenario_cleanup` **verbatim**, regardless of outcome |

All six commands are literal strings. The agent NEVER decides a pipe name, an interpreter path, a `--flag`, a redirection, or a cleanup ordering at runtime.

### K20 self-check (mandatory before STEP 3 enters phase 1)

For the current scenario `i`:

- `runs/<eval_id>/run_plan.json` exists and validates against `runtime-schemas/run_plan.schema.json`;
- `run_plan.scenarios[i]` exists for the tc the agent is about to run;
- the five `commands.*` strings each have non-zero length;
- the four pad paths in `commands.spawn` literally match `pad.in_fifo` / `pad.out_fifo` / `pad.err_file` / `pad.pid_file` from the same entry;
- `commands.write_action_template` contains exactly one occurrence of `<<JSON_PAYLOAD>>`;
- no scenario has been started under a different command string in this conversation (search the tool-call transcript for previous shell calls referencing the same `tc_id`).

### K19 self-check (mandatory before STEP 3 returns the scenario)

After the scenario ends and `commands.post_scenario_cleanup` has run:

- `ps -ef | grep "runtime-drivers/.*run.py" | grep "<tc_id>"` returns zero lines;
- `pad.dir` no longer exists on disk;
- the first `commands.read_one_event` returned a parseable `{"event":"ready",...}` (not empty, not a Python traceback);
- every shell tool-call for this scenario references the exact pad paths from `run_plan.scenarios[i].pad.*` (no other `/tmp/eval-*` names appear).

### Anti-patterns (each is a K19 or K20 violation — the symptoms the user keeps seeing)

| Anti-pattern | Symptom | Cure |
|---|---|---|
| Compose `mkfifo /tmp/eval-stdin-pipe; ... > /tmp/eval-stdout.txt` from scratch in STEP 3 | `cat: /tmp/eval-stdout.txt: No such file or directory`; PID leaks | Execute `commands.*` from `run_plan.json` verbatim; if it's not in the plan, re-run STEP 2.5 |
| Modify `commands.spawn` to add `2>&1` / change redirection / swap python binary | One scenario behaves differently than the rest; flaky runs | Re-run STEP 2.5 with the desired change wired into the plan generator |
| Use `cat "$PAD/out"` instead of the plan's `head -n 1 ...` | Tool-call hangs forever waiting for FIFO EOF | Use `commands.read_one_event` verbatim |
| Skip `commands.pre_spawn_cleanup` or `commands.post_scenario_cleanup` | Stale `ps aux` entries; FIFOs leak under `/tmp/eval-driver/` | Both cleanups are mandatory tool-calls; they are in the plan for a reason |
| Substitute anything other than `<<JSON_PAYLOAD>>` (e.g. patch in a different `pad.in_fifo`) | Driver receives nothing because the write went to a non-existent FIFO | Only the marker is variable; everything else is read-only literal |
| Run STEP 3 without `run_plan.json` present | The improvisation cycle returns; users see "Exit code 1" again | STEP 2.5 input gate: STEP 3 refuses to start; re-run STEP 2.5 first |

### Why this is a contract, not advice

The driver protocol (`ready` → `send`/`evaluatee_turn` × N → `end`/`trace_written`) is correct and stable. Every error class the user has been seeing ("Exit code 1", "No such file or directory", "PID still alive after cleanup", 144 noise) is generated by **string composition at runtime**, not by `run.py`. Materialising the commands in STEP 2.5 and reading them verbatim in STEP 3 eliminates that entire failure surface and shortens the per-scenario STEP-3 cost to: `1 read + 1 substitution + 1 execute` per turn, with no per-turn shell authoring.

## Loop completeness (K14)

The driver expects strict alternation: `send → read evaluatee_turn → send | end`. Closing stdin before writing `end` is a **protocol violation**, not a graceful shutdown.

### Trace rejection rule

A trace is rejected at the STEP 4 input gate iff any of:

```
1. termination.reason == "evaluatee_error"
   AND termination.detail contains "stdin closed before 'end' action received"

2. termination.reason == "evaluatee_error"
   AND turns_used == 1
   AND actual_tool_calls == []

3. termination.reason == "max_turns_reached"
   AND turns_used < effective_max_turns
   AND simulator_trail[-1].should_continue == true

4. simulator_trail is non-empty
   AND simulator_trail[-1].next_utterance is a non-empty string
   AND that exact string is NOT the content of the LAST
       dialog_turns entry whose actor == "evaluator"
```

- **Clause 3** catches the "demonstration shortcut" bug: the agent self-caps turns below `effective_max_turns` while the simulator still wants to continue.
- **Clause 4** catches the **`runs/eval-soul-001/` "simulator decided but agent never delivered"** bug: simulator_trail records `next_utterance = "订单号是 ORD…"` with `should_continue=false` and `stop_reason=goal_achieved`, but `dialog_turns` shows the customer never actually said it because the agent closed stdin before issuing the final `send`.

### The fix: send-then-end

Whenever `decision.next_utterance` is non-empty, the agent MUST first write a `send` carrying that exact text, THEN write `end` — even when `should_continue==false`. The customer's last utterance (providing an order number, saying "thanks, bye", etc.) is part of the dialog and MUST appear in `dialog_turns`.

### Recovery when the LLM rendering errors mid-loop

Write `{"action":"end","termination":{"reason":"deadlock_detected","detail":"<reason>"}}` THEN close stdin. NEVER close stdin first.

### Forbidden shortcut (K14)

The agent MUST NOT terminate the loop early citing "demonstration", "preview", "sample", "testing", "brevity", or any other self-invented reason. The only valid reasons to write `end` inside the loop are:

1. `decision.should_continue == false` (simulator decided to stop)
2. `turn_index + 1 >= effective_max_turns` (hard budget exhausted)
3. Driver emits `{"event":"error"}` (unrecoverable driver failure)

### Symmetric simulator-side rule (K15 runtime facet)

A simulator decision MUST NOT set `goal_progress = "goal_achieved"` or `stop_reason = "goal_achieved"` on the **first** decision after the evaluatee asked the customer for required information (e.g. `order_number`, `refund_id`) UNLESS the customer's reply containing that information has already been delivered to the evaluatee in a prior turn. Self-declaring `goal_achieved` while the required info is still locked inside `next_utterance` trips the trace-rejection rule above (clause 4).

A rejected trace taints the run; the affected `tc_id`s MUST appear in `EvaluationReport.open_questions`.

## Anti-patterns (each is a stop-and-taint)

| Anti-pattern | K-rule | Cure |
|---|---|---|
| Author any `run_*.py` / `runner.py` / `orchestrator.py` to drive the loop | K8 | Drive the loop turn-by-turn in conversation |
| `subprocess.Popen([..., 'runtime-drivers/...'])` in agent-authored code | K8 | Spawn from a single shell tool call |
| `while True:` loop bundling multiple turns into one execution | K8 | One round-trip per agent turn |
| HTTP call to "the LLM" from a script you wrote | K8 | The simulator IS the host LLM |
| `.sh` / `Makefile` chaining the spawn with anything else | K8 | Single shell command per turn |
| Write one `send` and close stdin | K14 | Always write `end` before closing |
| Self-cap turns below `effective_max_turns` for "demo" | K14 | Let the budget exhaust naturally |
| Skip the final `send` when `should_continue=false` and `next_utterance` is non-empty | K14 (clause 4) | send-then-end pattern |
| Simulator declares `goal_achieved` before customer's required info reached evaluatee | K15 runtime | Customer must utter required info first |
| Improvise pipe filename per turn (`/tmp/eval-stdin-pipe` + `/tmp/eval-stdout.txt`, etc.) | K19 / K20 | Read the literal commands from `run_plan.json#scenarios[i].commands.*`; do not author shell at runtime |
| Redirect driver stdout to a regular `*.txt` file instead of the FIFO | K19 / K20 | The plan's `commands.spawn` redirects to `pad.out_fifo`; execute it verbatim |
| `cat "$PAD/out"` (waits for EOF, blocks forever) | K19 / K20 | Use `commands.read_one_event` (which is `head -n 1 <pad.out_fifo>`) verbatim |
| Skip pre-spawn or post-scenario cleanup | K19 / K20 | `commands.pre_spawn_cleanup` and `commands.post_scenario_cleanup` are mandatory tool-calls |
| Begin STEP 3 without `runs/<eval_id>/run_plan.json` | K20 | Run STEP 2.5 first; STEP 3 fails fast if the plan is missing |
| Modify any string from `commands.*` other than substituting `<<JSON_PAYLOAD>>` | K20 | Re-run STEP 2.5 with the change wired into the plan generator |
