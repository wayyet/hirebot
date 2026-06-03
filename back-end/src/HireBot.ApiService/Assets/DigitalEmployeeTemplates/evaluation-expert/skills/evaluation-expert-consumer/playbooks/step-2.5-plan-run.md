# STEP 2.5 — planRun (materialise the execution plan-of-record)

**Kind**: deterministic (NO LLM)
**Authority**: workflow contract `S2.5` (new) + K20
**Inputs**: `evaluation_context.json` (post-STEP-6 materialisation OR post-STEP-2 enrich, see ordering note), every `runs/<eval_id>/enriched-cases/<tc_id>.json`
**Output**: `runs/<eval_id>/run_plan.json` (validated against `runtime-schemas/run_plan.schema.json`)

## Why this step exists

STEP 3 used to ask the agent to **invent shell commands per turn**: pick a pipe name, decide a Python interpreter, format a `--enriched-test-case` argument, retry when the pipe name didn't match between commands, etc. That improvisation is the root cause of the recurring `cat: /tmp/eval-stdout.txt: No such file or directory` / stale-PID / 144-exit-code class of failures.

STEP 2.5 removes that surface entirely. After STEP 2 has enriched every test case, every piece of information needed to launch every scenario's driver is already known and deterministic. STEP 2.5 freezes that information into a single file of **literal shell strings**. STEP 3 then becomes a thin executor: for each scenario it runs `commands.pre_spawn_cleanup` verbatim, then `commands.spawn` verbatim, reads with `commands.read_one_event` verbatim, writes with `commands.write_action_template` (substituting only the JSON payload), and ends with `commands.post_scenario_cleanup` verbatim.

## When STEP 2.5 runs

Immediately after STEP 2 has produced every `enriched-cases/<tc_id>.json` AND after `evaluation_context.runtime_driver.driver_id` / `runtime_simulator.simulator_id` / `global_turn_cap` are fixed. If `evaluation_context.json` is materialised later (STEP 6 in some flows), STEP 2.5 still depends only on the subset listed under **Inputs** above — `runtime_driver.driver_id`, `runtime_driver.driver_config` (for sanity check), and `global_turn_cap`. The plan-writing has no dependency on metric registries, scores, or reports.

## Procedure

```
1. Read:
   - eval_id ← evaluation_context.evaluation_id
   - driver_id ← evaluation_context.runtime_driver.driver_id
   - run_py_path = "runtime-drivers/<driver_id>/run.py"
   - global_cap ← evaluation_context.global_turn_cap or 30
   - python_bin ← `python3`   # sandbox always provides python3; do NOT switch per-scenario
   - cwd ← <absolute path to the evaluation-expert-consumer directory>
   - scenarios_inputs ← list of every persisted enriched-cases/<tc_id>.json

2. Sanity-check (fail-fast unless a self-heal is defined below; any unrecovered failure ⇒ DO NOT write run_plan.json):
   - run_py_path exists and is executable
   - `python3` is available (`which python3`); if not found, fail-fast
   - **websockets dependency prep (AUTOMATIC — do NOT ask the user)**: run `python3 -c "import websockets"` (single shell call); if exit-code ≠ 0, silently run `pip install -r runtime-drivers/ws_jwt/requirements.txt` once and continue; this is a sandbox-local operation that does not touch the evaluatee or business data; if pip fails, fail-fast with the install error
   - cwd ends with "/evaluation-expert-consumer"
   - len(scenarios_inputs) ≥ 1
   - every enriched tc has non-empty input.opening_message and turn_budget.hard_max_turns ≥ 1

3. For each enriched tc, compute the scenario plan entry:
   tc_id                    ← tc.test_case_id
   enriched_tc_path         = f"runs/{eval_id}/enriched-cases/{tc_id}.json"
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
     f'PAD={pad.dir}; N=$(cat "$PAD/cursor" 2>/dev/null || echo 1); DEADLINE=$(($(date +%s)+60)); while [ "$(date +%s)" -lt "$DEADLINE" ]; do L=$(sed -n "${N}p" "$PAD/out" 2>/dev/null); if [ -n "$L" ]; then printf "%d\n" $((N+1)) > "$PAD/cursor"; printf "%s\n" "$L"; exit 0; fi; sleep 0.3; done; printf \'{{"event":"error","detail":"read_one_event timeout after 60s"}}\n\''
     # Polls pad/out (regular file) for line N. Writes N+1 to cursor on success.
     # No FIFO involved — never blocks on open. Times out after 60 s with a synthetic error event.
     # Shell variable ${N} is runtime shell; only {pad.dir} is substituted at plan-generation time.

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

## Self-check before STEP 3 may begin (K20)

All MUST hold; any failure means STEP 2.5 has not run cleanly and STEP 3 MUST NOT start:

- `runs/<eval_id>/run_plan.json` exists, is valid JSON, and validates against `runtime-schemas/run_plan.schema.json`;
- `run_plan.scenarios[].tc_id` is the exact set of `enriched-cases/*.json` filenames (no missing tc, no orphan tc);
- every `run_plan.scenarios[].commands.spawn` contains literal substrings that match `pad.dir`, canonical `$PAD/in`, `$PAD/out`, `$PAD/err`, `$PAD/pid`, `python_bin`, `driver.run_py_path`, `--evaluation-context`, `--enriched-test-case`, and `--output` from the same entry (no `<placeholder>` left; no legacy `--test-case-id` / `--endpoint` / `--pad-in` / `--pad-out`);
- every `run_plan.scenarios[].commands.spawn` uses `tail -f "$1" 2>/dev/null | exec ...` pattern (NOT `<> "$PAD/in"` which is the deprecated O_RDWR FIFO approach);
- every `run_plan.scenarios[].commands.spawn` contains no `&;` token (background `&` must be followed by the next command, not by an extra semicolon);
- every `run_plan.scenarios[].commands.read_one_event` contains literal substrings that match `pad.dir`, canonical `$PAD/out`, `$PAD/cursor`, and `sed -n`;
- every `run_plan.scenarios[].commands.write_action_template` contains exactly one occurrence of the marker `<<JSON_PAYLOAD>>`;
- no two scenarios share the same `pad.dir` (deterministic isolation between tc runs);
- `run_plan.generated_by_step == "STEP 2.5 planRun"` (guards against hand-written or LLM-written plans).

## How STEP 3 consumes this (binding contract)

In STEP 3, for each scenario in `run_plan.scenarios`, the agent:

| Phase | What the agent runs |
|---|---|
| 1 | Execute `commands.pre_spawn_cleanup` **verbatim** (single shell tool-call) |
| 2 | Execute `commands.spawn` **verbatim** (single shell tool-call) |
| 3 | Execute `commands.read_one_event` **verbatim**; parse the returned line as JSON; expect `{"event":"ready",...}` |
| 4 | Build the first action JSON `{"action":"send","turn_index":0,"text":<opening_message verbatim>,"decision":<deterministic turn-0 decision>}`; produce a single-line JSON string; substitute it into `commands.write_action_template` at the `<<JSON_PAYLOAD>>` marker; execute the resulting string |
| 5 | Loop: execute `commands.read_one_event` → parse → simulator decision → substitute into `commands.write_action_template` → execute. Continue until the read returns `{"event":"trace_written",...}` or `{"event":"error",...}` |
| 6 | Execute `commands.post_scenario_cleanup` **verbatim**, regardless of outcome |

The agent MUST NOT rebuild or modify any string from `commands.*` other than substituting the single `<<JSON_PAYLOAD>>` marker. Adding `2>&1`, changing the redirection, replacing the cursor-based `sed -n "${N}p"` poller with `cat` / `tail`, or splitting the spawn into two tool-calls is a K20 violation.

## Re-plan rules

If anything in the inputs changes after STEP 2.5 has written `run_plan.json` (driver_id swap, new enriched tc added, evaluation_context renamed, ...), STEP 2.5 MUST be re-run end-to-end. Partial editing of `run_plan.json` by hand is forbidden (the `generated_by_step` literal + the `generated_at` timestamp anchor the audit chain).

## Anti-patterns (each is a K20 violation)

| Anti-pattern | Symptom | Cure |
|---|---|---|
| STEP 3 begins without `run_plan.json` present | Same "ad-hoc shell" recurrence | STEP 2.5 input gate; fail fast |
| Agent rewrites `commands.spawn` to add `--verbose` / change redirection | Driver behaves differently across scenarios; one-off bugs | Re-run STEP 2.5 with the desired change wired into the plan generator |
| Plan contains residual `<placeholder>` (other than the one allowed `<<JSON_PAYLOAD>>`) | Driver exits 1 because of literal angle-brackets in argv | Schema `pattern: "<<JSON_PAYLOAD>>"` rejects; STEP 2.5 must regenerate |
| Two scenarios share the same `pad.dir` | Second scenario inherits first scenario's stale files; nondeterministic hangs | `pad.dir = /tmp/eval-driver/<eval_id>/<tc_id>` is structurally unique; K20 self-check rejects duplicates |
| Hand-edit `run_plan.json` between scenarios | Audit chain broken; reproducibility lost | Treat run_plan.json as read-only post STEP 2.5; any change ⇒ regenerate |
| Generate `run_plan.json` via an agent-authored script `scripts/make_plan.py` | K8 violation on top of K20 | STEP 2.5 logic runs inline in the conversation (deterministic file ops + string templates) |
| Use `<> "$PAD/in"` or `mkfifo` in spawn/pre_spawn_cleanup | "stdin closed before 'end' action received" with turns_used=0 | Use `tail -f` pipe pattern from this playbook; FIFO O_RDWR races cause premature stdin EOF on container kernels |
