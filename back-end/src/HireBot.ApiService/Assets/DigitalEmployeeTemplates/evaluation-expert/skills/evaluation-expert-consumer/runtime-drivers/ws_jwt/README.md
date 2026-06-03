# ws_jwt driver

The built-in **WebSocket + JWT** runtime driver for STEP 3 (`driveEmployeeOnScenario`). It lives inside this consumer's hot-pluggable `runtime-drivers/` layer.

## What this driver does (v2.0, long-lived stdin/stdout protocol)

STEP 3 is **dual-role with asymmetric execution**:

| Role | Execution model | Lives in |
|---|---|---|
| **driver_role** (this directory) | A long-lived subprocess (`python run.py …`) | `./runtime-drivers/ws_jwt/` |
| **simulator_role** | The host evaluation-expert agent itself, using its OWN LLM brain. **NOT a subprocess.** | `./simulators/<simulator_id>/` (role profile only) |

This driver is the long-lived I/O subprocess. It owns the WebSocket+JWT wire, sends customer utterances to the evaluatee, collects the evaluatee's replies, and writes the final `ExecutionTrace`. It makes **no** decision about what the customer says or when to stop — those decisions belong to the host agent (acting as the customer simulator with its own LLM, the same brain that runs STEP 1.5 / STEP 4 / STEP 8 / STEP 9).

`run.py` connects once per scenario, emits `{"event":"ready",...}` on stdout, then enters a loop:

- **On `{"action":"send","turn_index":N,"text":"...","decision":{...}}` from stdin**: cache `decision` into `simulator_trail[]`, send `text` over WS, collect the evaluatee turn until `assistant_done`, append `dialog_turns[]` + `actual_tool_calls[]`, and emit `{"event":"evaluatee_turn","turn_index":N,"content":"...","tool_calls":[...],"raw_messages":[...]}` on stdout.
- **On `{"action":"end","decision":{...},"termination":{...}}` from stdin**: cache the final decision, assemble `ExecutionTrace`, write to `--output`, emit `{"event":"trace_written","path":"..."}` on stdout, close WS, exit 0.
- **On any I/O error**: emit `{"event":"error","detail":"..."}`, write a best-effort partial trace, exit 2.

Auto-approves any `approval_required` from the evaluatee when `auto_approve_tools=true`. The output `ExecutionTrace` validates against `runtime-schemas/execution_trace.schema.json`.

It does **not** score, judge red lines, raise `observed_signals`, or filter signals. STEP 4 fan-out is the only place where any of that happens.

## Files

| File | Role |
|---|---|
| `driver.json` | Manifest, validated against `runtime-schemas/runtime_driver.schema.json` |
| `run.py` | STEP-3-conformant long-lived stdin/stdout orchestrator |
| `ws_client.py` | Low-level WebSocket connect + per-turn collection (unchanged) |
| `requirements.txt` | `websockets>=12.0` |

There is no simulator binary in this directory or anywhere else under `evaluation-expert-consumer/`. The simulator role is played by the host agent's own LLM; `evaluation_context.paths.simulators_dir / runtime_simulator.simulator_id / simulator.json` is just a role profile that the host agent reads.

## Invocation contract

STEP 3 spawns this driver once per scenario:

```bash
python run.py \
  --evaluation-context ./runs/<eval_id>/evaluation_context.json \
  --enriched-test-case ./runs/<eval_id>/enriched-cases/<test_case_id>.json \
  --output             ./runs/<eval_id>/traces/<test_case_id>.trace.json
```

`run.py` reads `driver_config` from `evaluation_context.runtime_driver.driver_config`. STEP 3 is responsible for validating that block against `driver.json#/config_schema` BEFORE spawning us; `run.py` only re-checks the absolute minimum (`endpoint` and `token` non-empty).

## Wire protocol (line-delimited JSON)

### driver → host agent (stdout, one JSON object per line)

```json
{"event":"ready","driver_id":"ws_jwt","effective_max_turns":15,"evaluation_id":"eval-001","test_case_id":"tc-..."}
{"event":"evaluatee_turn","turn_index":0,"content":"...","tool_calls":[...],"raw_messages":[...]}
{"event":"evaluatee_turn","turn_index":1,"content":"...","tool_calls":[...],"raw_messages":[...]}
{"event":"trace_written","path":"./runs/.../traces/tc-....trace.json","termination":{"reason":"completed_normally","turns_used":4}}
```

On unrecoverable failure:

```json
{"event":"error","detail":"<diagnostic>"}
```

### host agent → driver (stdin, one JSON object per line)

```json
{"action":"send","turn_index":0,"text":"我已经等了一星期了 …","decision":{...full SimulatorDecision...}}
{"action":"send","turn_index":1,"text":"那能不能再给点补偿 …","decision":{...}}
{"action":"end","decision":{...final SimulatorDecision with should_continue=false...},
 "termination":{"reason":"completed_normally","detail":"...","final_emotion":"satisfied","turns_used":4}}
```

The host agent MAY end early (e.g. `bottom_line_violated`, `goal_achieved`) at any turn. The driver does **not** auto-end on `effective_max_turns` — when the cap is reached the driver simply stops accepting further `send` actions; the host agent is expected to issue an `end` action with `termination.reason=max_turns_reached`.

## driver_config + runtime_simulator example

```json
{
  "runtime_driver": {
    "driver_id": "ws_jwt",
    "driver_config": {
      "endpoint": "localhost:18789",
      "token": "<JWT>",
      "timeout": 60,
      "auto_approve_tools": true
    }
  },
  "runtime_simulator": {
    "simulator_id": "customer_realistic"
  },
  "global_turn_cap": 30
}
```

Notes:

- The legacy `max_turns` field has been **removed**. The per-scenario hard cap now comes from `min(test_case.turn_budget.hard_max_turns, evaluation_context.global_turn_cap)`.
- The legacy `simulator_timeout` field has been **removed**. There is no simulator subprocess to time out — the simulator runs inside the host agent.
- `runtime_simulator.simulator_config` is gone too (no `model`, no `api_key_env`). The LLM that powers the customer role is the host agent's own LLM, configured at the agent runtime level — never inside this contract.

## Termination semantics

| Condition (driven by the host agent's `end` action unless noted) | `termination.reason` |
|---|---|
| Host agent ends with `stop_reason=goal_achieved` | `completed_normally` |
| Host agent ends with `stop_reason=bottom_line_violated` | `bottom_line_violated` |
| Host agent ends with `stop_reason=deadlock_detected` or `customer_gave_up` | `deadlock_detected` |
| Host agent ends with `reason=max_turns_reached` after `effective_max_turns` exchanges | `max_turns_reached` |
| Any `error` message from the evaluatee | `evaluatee_error` |
| Per-turn timeout exhausted (no `assistant_done`) | `timeout` |
| stdin closed before `end` action / unhandled exception | `evaluatee_error` (with detail) |

This mapping is intentional: the driver does NOT decide that a missing tool call is an error. STEP 4 fan-out + STEP 7 redLineCheck do that.

## Setup

```bash
cd evaluation-expert-consumer/runtime-drivers/ws_jwt
pip install -r requirements.txt
```

There are no simulator-side dependencies to install. The customer role is the host agent itself; it does not call any external LLM API or read any extra environment variable on this driver's behalf.
