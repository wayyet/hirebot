# playbooks/

Step-by-step operating procedures for the evaluation-expert-consumer skill. `SKILL.md` is the router; this directory holds the long-form details.

| File | Step(s) | Kind |
|---|---|---|
| [`step-00-resolve-employee.md`](./step-00-resolve-employee.md) | PRE.A + STEP 0 | deterministic role-catalog load + LLM-with-confirmation employee resolution & canonicalization |
| [`step-01-resolve-and-filter.md`](./step-01-resolve-and-filter.md) | STEP 1 | deterministic — role-filter metrics into `candidate_metrics` |
| [`step-1.2-curate-metrics.md`](./step-1.2-curate-metrics.md) | STEP 1.2 | LLM, bounded+auditable — `selected_metrics = (candidate − removed) ∪ added` |
| [`step-1.5-consult-then-synthesize.md`](./step-1.5-consult-then-synthesize.md) | STEP 1.5 | LLM, conditional — user-first fallback chain; **owns K21** (positive : negative ≈ 80 : 20) |
| [`step-2.5-plan-run.md`](./step-2.5-plan-run.md) | STEP 2.5 | deterministic — materialise `runs/<eval_id>/run_plan.json` with literal shell commands for STEP 3 (K20) |
| [`step-03-driver-and-simulator-loop.md`](./step-03-driver-and-simulator-loop.md) | STEP 3 | dual-role — driver subprocess + host-LLM simulator (reads commands verbatim from `run_plan.json`, K19+K20) |
| [`step-04-fanout-scoring.md`](./step-04-fanout-scoring.md) | STEP 4 | LLM fan-out — one call per (case, metric) |
| [`step-05-07-deterministic-rollup.md`](./step-05-07-deterministic-rollup.md) | STEP 5/6/7 | deterministic — aggregate + roll-up + red-line |
| [`step-09-overall-report.md`](./step-09-overall-report.md) | STEP 9 | LLM synthesis — JSON + HTML dual-format output |
| [`k-rules.md`](./k-rules.md) | all | K1–K21 reference table with one-line summary, owning step, severity, taint policy |
| [`pre-flight-invariants.md`](./pre-flight-invariants.md) | before PRE.A | invariants the host agent MUST verify before starting any run |
| [`tainted-run-lifecycle.md`](./tainted-run-lifecycle.md) | any | how a run becomes tainted, what continues, how to recover |

PRE (loadMetricRegistry) / STEP 2 / STEP 8 are short enough to live inline in `SKILL.md` and have no separate playbook. STEP 2.5 has its own playbook because it owns the K20 contract.

## How to use

1. The host agent starts at `SKILL.md` (the router).
2. When entering a step, it reads the corresponding playbook for that step's full operating procedure.
3. K-rules are referenced by number throughout; the canonical lookup is [`k-rules.md`](./k-rules.md).

## Authoring rules

- Each playbook is the **single source of truth** for its step. SKILL.md links here, never duplicates.
- Worked examples cite the reference fixtures under `../runs/eval-soul-001/` / `eval-xiaofu-001/` / `eval-xiaofu-002/`.
- K-rule numbers reference the workflow contract `metric-selection.workflow-contract.projection.json` (K1–K16). Anti-patterns must cite the K-rule they break.
