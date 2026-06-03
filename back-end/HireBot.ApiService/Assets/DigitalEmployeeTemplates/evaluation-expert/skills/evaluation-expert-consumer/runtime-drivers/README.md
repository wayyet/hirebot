# runtime-drivers

The third hot-pluggable data layer of `evaluation-expert-consumer`, alongside `./metrics/` and `./test-cases/`.

A **runtime driver** is the deterministic I/O adapter STEP 3 (`driveEmployeeOnScenario`) uses to talk to the evaluatee sandbox. The contract layer (`contracts/projections/**`) is protocol-agnostic; protocol-specific code (WebSocket, HTTP, stdio, mock, …) lives **only** inside a driver directory.

## Hot-plug rule

Adding a new protocol (or stubbing one for tests) is **a directory drop**:

```
runtime-drivers/
└── <driver_id>/
    ├── driver.json     ← required, validated against runtime-schemas/runtime_driver.schema.json
    ├── <entry>         ← required, the executable named in driver.json.entry
    └── ...             ← any helper modules
```

You do **NOT** edit any `*.projection.json`, `SKILL.md`, or workflow contract when adding a new driver.

## Required input/output contract

Every driver, regardless of protocol, MUST honor the same contract:

| Direction | Shape | Schema |
|---|---|---|
| **Input** | One enriched test case per invocation, plus the run's evaluation context (for paths and `driver_config`) | `runtime-schemas/enriched_test_case.schema.json` + `runtime-schemas/evaluation_context.schema.json` |
| **Output** | Exactly one ExecutionTrace per invocation, written to `./runs/<eval_id>/traces/<test_case_id>.trace.json` | `runtime-schemas/execution_trace.schema.json` |

If the produced JSON does not validate against `execution_trace.schema.json`, STEP 3 MUST fail fast for that scenario; downstream STEP 4 fan-out is then skipped for the failed `(test_case, *)` pairs.

## Selecting a driver at runtime

`evaluation_context.runtime_driver.driver_id` decides which directory under `./runtime-drivers/` is invoked. Resolution order:

1. `EvaluationContext.runtime_driver.driver_id` (materialized at STEP 0/1; usually copied from user input)
2. Environment variable `EVALUATION_DRIVER_ID`
3. Hard fail (no implicit default — drivers are evaluatee-specific and silent fallbacks would corrupt traces)

The directory `./runtime-drivers/` itself can be relocated via `EVALUATION_DRIVERS_DIR`.

## `driver.json` minimum

```json
{
  "driver_id": "ws_jwt",
  "version": "1.0.0",
  "protocol": "websocket+jwt",
  "entry": "run.py",
  "language": "python",
  "produces": "runtime-schemas/execution_trace.schema.json",
  "consumes": [
    "runtime-schemas/evaluation_context.schema.json",
    "runtime-schemas/enriched_test_case.schema.json"
  ],
  "capabilities": {
    "supports_multi_turn": true,
    "supports_tool_call_observation": true,
    "supports_auto_approval": true
  },
  "config_schema": {
    "type": "object",
    "required": ["endpoint", "token"],
    "properties": {
      "endpoint": { "type": "string", "description": "HOST:PORT or full ws:// URL" },
      "token":    { "type": "string", "description": "JWT bearer token" },
      "timeout":  { "type": "integer", "default": 60 }
    }
  }
}
```

This file is validated against `runtime-schemas/runtime_driver.schema.json`. STEP 3 reads it once per evaluation run, then validates `evaluation_context.runtime_driver.driver_config` against the embedded `config_schema` before invoking `entry`.

## Hard rules for driver authors

1. **Output must be ExecutionTrace, not a raw transcript.** If your protocol produces something else, your `entry` must transform it before writing.
2. **No evaluation logic.** Drivers observe and persist; they MUST NOT score, judge red lines, or filter signals.
3. **No silent dropping.** Unknown messages from the evaluatee should land in `actual_tool_calls` / `dialog_turns` / `actual_artifacts` (with sensible enum-compatible classification) or trigger an `evaluatee_error` termination — never be discarded.
4. **No writes outside `./runs/<eval_id>/`.** All on-disk effects belong to the run directory.
5. **One ExecutionTrace per invocation.** Multi-test-case batching is the workflow's responsibility, not the driver's.

## Built-in drivers

| `driver_id` | Protocol | Notes |
|---|---|---|
| `ws_jwt` | `websocket+jwt` | Connects to an OpenClaw Gateway over WS, JWT in URL query. Multi-turn capable; auto-approves tool calls. Implemented inside the consumer runtime-driver layer. |

Add new drivers by dropping a sibling directory with its own `driver.json`.
