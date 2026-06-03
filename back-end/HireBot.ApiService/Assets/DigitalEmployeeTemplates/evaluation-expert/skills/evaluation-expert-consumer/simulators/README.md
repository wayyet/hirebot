# simulators

The fourth hot-pluggable data layer of `evaluation-expert-consumer`, alongside `./metrics/`, `./test-cases/`, and `./runtime-drivers/`.

A **user simulator** is the **role profile** the evaluation-expert agent itself plays in STEP 3 (`driveEmployeeOnScenario`) to impersonate a customer. Together with a runtime driver (the I/O subprocess), the simulator forms the **dual-role STEP 3**:

| Role | Execution model | Responsibility | Lives in |
|---|---|---|---|
| `runtime-drivers/<driver_id>/` | **Subprocess** (e.g. `python run.py …`) | Wire-level I/O — talking to the evaluatee, applying JWT, sending/receiving frames | `./runtime-drivers/` |
| `simulators/<simulator_id>/` | **NOT a subprocess.** Prompt template + manifest consumed by the host agent's own LLM | Customer brain — decide next utterance + when/why to stop | `./simulators/` |

> ⚠️ Critical asymmetry. **Drivers are subprocesses** because protocol I/O (WebSocket / JWT / TLS / tool-approval) is not something an LLM can perform itself. **Simulators are not subprocesses**: deciding what a customer would say next is exactly the kind of dialogue task the evaluation-expert agent's own LLM is built for — same brain that runs STEP 1.5 / STEP 4 / STEP 8 / STEP 9. Spawning a second LLM with its own API key just to talk to ourselves would duplicate cost, complicate ops, and break parity with how every other LLM step in this skill works.

The contract layer (`contracts/projections/**`) is **provider-agnostic**: it never references a specific LLM, model, or prompt. Persona-specific prompt templates live **only** inside a simulator directory; the LLM that consumes them is whatever brain is hosting the evaluation-expert agent at runtime.

## Hot-plug rule

Adding a new persona is **a directory drop**:

```
simulators/
└── <simulator_id>/
    ├── simulator.json        ← required, validated against runtime-schemas/simulator.schema.json
    ├── system_prompt.md      ← required, template file named in simulator.json.system_prompt
    ├── .no-decide-script     ← required SENTINEL (see below)
    └── ...                   ← few-shot examples, optional helpers (no executables)
```

You do **NOT** edit any `*.projection.json`, `SKILL.md`, or workflow contract when adding a new simulator. You also do **NOT** add any `decide.py` / entry script — there is no subprocess to invoke.

### The `.no-decide-script` sentinel

Every simulator directory MUST contain a hidden file named `.no-decide-script`. It serves three purposes:

1. **Self-documenting marker** that this directory is `kind: "llm_persona"` and has NO entry script.
2. **K8 audit anchor** — the workflow contract's K8 (`NoAdhocOrchestratorScripts`) extends to `./simulators/<simulator_id>/`. The audit looks for this sentinel to confirm the directory is intentionally script-free; removing it weakens the guard.
3. **Onboarding hint** — anyone adding a new simulator copies `customer_realistic/.no-decide-script` so the contract is reaffirmed in every new persona.

Sample content (copy verbatim when creating a new simulator):

> SENTINEL — DO NOT REMOVE THIS FILE
>
> This simulator is a `llm_persona` profile (see `simulator.json` → `kind: "llm_persona"`).
> It has NO entry script (no `decide.py` / `run.py` / `*.py` / `*.sh` / `*.ts` / `*.js` / `*.mjs` / `*.ipynb` / Makefile / `*.cmd` / `*.ps1`). The host evaluation-expert agent itself plays the customer in-process using `system_prompt.md`, with its own LLM brain. There is NO subprocess, NO independent LLM api_key, NO HTTP call.

If pre-flight invariant 5 finds an executable file in a simulator directory, it is treated as a K8 violation immediately — the run is tainted before STEP 3 starts.

## Required input/output contract

Every simulator, regardless of which model the host agent uses, MUST honor the same contract:

| Direction | Shape | Schema |
|---|---|---|
| **Input** (consumed by the host agent's LLM via prompt expansion) | The enriched test case (persona/goal/stop_conditions/opening_message) + the in-progress execution trace (dialog_turns + previous simulator_trail) | `runtime-schemas/enriched_test_case.schema.json` + `runtime-schemas/execution_trace.schema.json` |
| **Output** (produced by the host agent's LLM, validated locally before append) | Exactly one `SimulatorDecision` per turn | `runtime-schemas/simulator_decision.schema.json` |

If the produced JSON does not validate against `simulator_decision.schema.json`, STEP 3 MUST fail fast for that turn; the scenario terminates with `reason=evaluatee_error` and `detail` records the validation failure.

## Selecting a simulator at runtime

`evaluation_context.runtime_simulator.simulator_id` decides which directory under `./simulators/` is loaded by the host agent. Resolution order (mirrors driver resolution):

1. `EvaluationContext.runtime_simulator.simulator_id`
2. Environment variable `EVALUATION_SIMULATOR_ID`
3. Hard fail (no implicit default — wrong persona corrupts evaluation just as silently as wrong protocol).

The directory `./simulators/` itself can be relocated via `EVALUATION_SIMULATORS_DIR`.

## `simulator.json` minimum

```json
{
  "simulator_id": "customer_realistic",
  "version": "2.0.0",
  "kind": "llm_persona",
  "system_prompt": "system_prompt.md",
  "produces": "runtime-schemas/simulator_decision.schema.json",
  "consumes": [
    "runtime-schemas/enriched_test_case.schema.json",
    "runtime-schemas/execution_trace.schema.json"
  ],
  "capabilities": {
    "supports_emotion_tracking": true,
    "supports_progress_assessment": true,
    "supports_bottom_line_check": true
  }
}
```

This file is validated against `runtime-schemas/simulator.schema.json`. STEP 3 reads it once per evaluation run.

There are **no** `entry`, `language`, `config_schema`, `model`, or `api_key_env` fields anywhere in the simulator layer. Those concepts belong to drivers, not simulators.

## Hard rules for simulator authors

1. **One SimulatorDecision per turn.** The host agent calls its LLM once per customer turn, expanding the rendered system prompt + context.
2. **Conversation state lives in the trace.** The host agent re-derives `current_emotion` / `dialog_so_far` from `execution_trace.simulator_trail` + `dialog_turns` each turn — never from hidden agent memory.
3. **Honor `goal.bottom_line`.** If the latest evaluatee response falls below the customer's bottom line, the decision MUST be `should_continue=false`, `stop_reason=bottom_line_violated`, `violated_bottom_line=true`. STEP 3 trusts the customer brain on this.
4. **Honor `stop_conditions.success`.** Don't keep talking once the customer's primary goal is met. Emit `should_continue=false` with `stop_reason=goal_achieved`.
5. **Don't drift the persona.** Emotion may evolve (`calmer` / `more_upset`), but `customer_persona.personality` is fixed for the scenario. Don't suddenly turn an "急性子" customer into a patient one.
6. **No evaluation logic.** Simulators play the customer; they MUST NOT score the employee, mention metrics, or judge red lines. Scoring is STEP 4's job.
7. **`internal_emotion` and `rationale` are NEVER shown to the evaluatee.** Only `next_utterance` is forwarded to the driver. Everything else is audit-only and lives in `simulator_trail`.

## Built-in simulators

| `simulator_id` | `kind` | Notes |
|---|---|---|
| `customer_realistic` | `llm_persona` | Default. Realistic customer respecting persona / goal / stop_conditions. Emits emotion arc + perceived progress per turn. |

Add new simulators (e.g. `customer_calm`, `customer_aggressive`) by dropping a sibling directory with its own `simulator.json` + `system_prompt.md`.
