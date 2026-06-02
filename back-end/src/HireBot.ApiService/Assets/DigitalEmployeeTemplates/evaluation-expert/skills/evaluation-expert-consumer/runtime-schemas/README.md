# runtime-schemas

Runtime data shapes produced/consumed during a single evaluation run. **These are NOT projection contracts.** They live alongside the skill purely so that every workflow step can validate its inputs/outputs against a stable shape.

## Files

| Schema | Producer step | Consumer step | Persisted at |
|---|---|---|---|
| `evaluation_context.schema.json` | STEP 6 `materializeEvaluationContext` (deterministic) | STEP 4 fan-out, STEP 5–8 | `./runs/<eval_id>/evaluation_context.json` |
| `enriched_test_case.schema.json` | STEP 2 `enrichTestCases` (deterministic, always runs) | STEP 3, STEP 4 | `./runs/<eval_id>/enriched-cases/<test_case_id>.json` |
| `run_plan.schema.json` | STEP 2.5 `planRun` (deterministic; freezes literal shell strings for every scenario) | STEP 3 (verbatim execution) | `./runs/<eval_id>/run_plan.json` |
| `execution_trace.schema.json` | STEP 3 `driveEmployeeOnScenario` (evaluator-driver) | STEP 4 fan-out | `./runs/<eval_id>/traces/<test_case_id>.trace.json` |
| `metric_score.schema.json` | STEP 4 fan-out (one LLM call per pair) | STEP 5, STEP 7 | `./runs/<eval_id>/scores/<test_case_id>__<metric_code>.json` |
| `scenario_score.schema.json` | STEP 4 (post-fan-out aggregator, deterministic) | STEP 5, STEP 7 | `./runs/<eval_id>/scenarios/<test_case_id>.json` |
| `scenario_report.schema.json` | STEP 8 `buildScenarioReports` (LLM synthesis, prose only) | STEP 9 | `./runs/<eval_id>/reports/scenarios/<test_case_id>.report.json` |
| `evaluation_report.schema.json` | STEP 9 `buildOverallReport` (LLM synthesis, prose only) | end-of-run consumer | `./runs/<eval_id>/reports/evaluation_report.json` |
| `runtime_driver.schema.json` | Author of a `runtime-drivers/<driver_id>/driver.json` manifest | STEP 3 driver loader | `./runtime-drivers/<driver_id>/driver.json` (NOT under `./runs/`) |
| `simulator.schema.json` | Author of a `simulators/<simulator_id>/simulator.json` manifest | STEP 3 simulator-profile loader (host agent) | `./simulators/<simulator_id>/simulator.json` (NOT under `./runs/`) |
| `simulator_decision.schema.json` | The host evaluation-expert agent's own LLM, once per customer turn | STEP 3 (consumed in-memory; persisted into `execution_trace.simulator_trail`) | not persisted standalone — embedded in trace |

## Hard rules

- The contents of these files **MUST NEVER** be written back into `contracts/projections/**`. The contract layer is read-only at runtime.
- `metric_score.schema.json` deliberately does NOT include a `red_line_passed` or `pass_fail` field. Red-line judgement is deterministic and lives in STEP 7 `redLineCheck` only. The LLM may only RAISE `observed_signals` for STEP 7 to consume.
- `enriched_test_case.schema.json` requires `applicable_metrics` to be non-empty: STEP 2 enforces this even for fully curated test cases that already shipped with metric bindings.
- Synthesized test cases (those produced by STEP 1.5 `parseTestCases`) MUST be persisted under `./runs/<eval_id>/synthesized-cases/` and MUST NOT pollute `./test-cases/` (the canonical catalog).
- **Reports are two-tier**: STEP 8 produces one `ScenarioReport` per test case; STEP 9 produces exactly one `EvaluationReport` after all ScenarioReports exist. STEP 9 MUST link to scenario reports by path and MUST NOT inline them.
- **Report numeric fields are copies, not recomputations**: every numeric field in `ScenarioReport.metric_results[].score` and in `EvaluationReport.per_metric_final_scores` / `.dimension_scores` / `.overall_score` / `.red_line` / `.passed` MUST be byte-identical to upstream `MetricScore` / STEP 5 / STEP 6 / STEP 7 outputs. The LLM in STEP 8 / STEP 9 may author prose only.
- **Runtime drivers are protocol-only adapters**: every driver under `./runtime-drivers/<driver_id>/` MUST publish a `driver.json` validated against `runtime_driver.schema.json`, and MUST output an `ExecutionTrace` validated against `execution_trace.schema.json`. Drivers MUST NOT contain evaluation logic, MUST NOT be referenced from any `*.projection.json`, and MUST NOT be the implicit fallback when `runtime_driver.driver_id` is missing — STEP 3 fails fast in that case.
- **User simulators are persona-only role profiles**: every simulator under `./simulators/<simulator_id>/` MUST publish a `simulator.json` validated against `simulator.schema.json` plus a `system_prompt.md` template. Simulators are **NOT subprocesses** — the host evaluation-expert agent's own LLM (the same brain that runs STEP 1.5 / STEP 4 / STEP 8 / STEP 9) consumes the system prompt each turn and produces a `SimulatorDecision` validated against `simulator_decision.schema.json` before the decision is forwarded to the driver and appended to `simulator_trail`. Simulator directories MUST NOT contain executable entrypoints, MUST NOT score the employee, mention metrics, judge red lines, or be referenced from any `*.projection.json`. STEP 3 fails fast when `runtime_simulator.simulator_id` cannot be resolved — no implicit default.
- **STEP 3 is dual-role with asymmetric execution**: `runtime_driver` is a long-lived subprocess (line-delimited JSON over stdin/stdout — `{"action":"send",...}` / `{"action":"end",...}` from agent to driver, `{"event":"ready"}` / `{"event":"evaluatee_turn",...}` / `{"event":"trace_written",...}` from driver to agent). `runtime_simulator` is consumed inside the host agent's own LLM, NO subprocess boundary. The driver MUST NOT generate customer text; the host agent (acting as simulator) MUST NOT touch the protocol wire. `turn_budget.hard_max_turns` (or `evaluation_context.global_turn_cap`, whichever is smaller) is a HARD ceiling — `should_continue=true` cannot bypass it; once the cap is reached the host agent MUST issue an `end` action with `reason=max_turns_reached`.

## Why this directory is separate from `contracts/projections/`

- `contracts/projections/` defines **what is true forever** (vocabulary, constraints, workflow shape).
- `runtime-schemas/` defines **what flows during one run** (transient evidence, scores, plans).

Mixing them would let runtime data drift back into the contract and break reproducibility.
