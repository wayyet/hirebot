# Pre-flight invariants

These invariants MUST hold before the host agent enters PRE.A / STEP 0. They are short-circuit checks; failing any one of them means the run does not start.

The agent MUST run this checklist inline (filesystem reads + arithmetic) — no LLM call.

## Invariants

| # | Invariant | How to check | On failure |
|---|---|---|---|
| 1 | All six hot-plug data layers exist and are readable | stat `./metrics/`, `./test-cases/`, `./runtime-drivers/`, `./simulators/`, `./role-catalog/`, `./employees/` (or their env-overridden roots) | `block_or_escalate` with the missing path |
| 2 | At least one `*.metric.json` validates against `metric.schema.json` | filesystem scan + schema validation | K1 — `block_or_escalate` (empty registry) |
| 3 | `evaluation_context.runtime_driver.driver_id` resolves to a directory under `EVALUATION_DRIVERS_DIR` | check `./runtime-drivers/<driver_id>/driver.json` exists and validates against `runtime_driver.schema.json` | `fail_fast` — silent default disallowed |
| 4 | `evaluation_context.runtime_simulator.simulator_id` resolves to a directory under `EVALUATION_SIMULATORS_DIR` | check `./simulators/<simulator_id>/simulator.json` exists and validates against `simulator.schema.json` | `fail_fast` — silent default disallowed |
| 5 | The selected simulator directory contains the `.no-decide-script` sentinel | stat `./simulators/<simulator_id>/.no-decide-script` | warn; if any `.py` / `.sh` / executable exists in the dir, treat as K8 violation and taint preemptively |
| 6 | `evaluation_context.runtime_driver.driver_config` validates against `driver.json#/config_schema` | JSON Schema validate | `fail_fast` |
| 7 | `evaluation_context.global_turn_cap` is set (default 30 if absent) and `1 <= cap <= 50` | bounds check | `fail_fast` |
| 8 | At least one metric's `applicable_roles` covers the canonical employee `role_id` (or `*`) | inline filter | K9 path — `block_or_escalate` after STEP 1 if `candidate_metrics` ends up empty |
| 9 | `./runs/<eval_id>/` does not already exist (no overwrite) OR existing dir contains `TAINTED.md` and the user explicitly opted in to retry | path check | `fail_fast` to prevent silent overwrites |
| 10 | The host agent has not authored any executable file under the skill root since skill creation | inventory `./` for `.py`/`.sh`/`.ts`/`.js`/`.mjs`/`.ipynb`/`Makefile`/`*.cmd`/`*.ps1` outside the runtime-drivers whitelist | K8 — taint immediately |
| 11 | The Role_Catalog directory is readable and (if non-empty) at least parses | scan `EVALUATION_ROLES_DIR` (default `./role-catalog/`); per-file failures are fail-soft (skip + open_question), but the directory itself must be statable | `block_or_escalate` only if the directory is unreadable; individual bad files do NOT block (role-catalog K1–K3) |
| 12 | If an Employee_File is expected, `EVALUATION_EMPLOYEES_DIR` (default `./employees/`) is readable | stat the directory; absence of `<employee_id>.json` is NOT a failure (STEP 0 falls to user-dialog / inferred) | `block_or_escalate` only if the directory path is set but unreadable |
| 13 | `evaluation_context.metric_selection_policy` (if present) has resolvable defaults | validate `mode` ∈ {auto, always, never}; bounds on max_metrics [1,100], min_dimensions_covered [1,5], auto_apply_threshold [0,1]; omitted fields take documented defaults | `fail_fast` on out-of-range explicit values; omission is fine |

## When to run

- Before PRE.A on a fresh evaluation
- After any environment change (new driver, new simulator, new role, env var override)
- When recovering from a tainted run, before reusing artifacts

## Why these invariants

Most hard-to-debug evaluation failures stem from silent fallbacks:

- `silent_default_disallowed` on driver_id / simulator_id (workflow contract S3) — without invariants 3/4 the agent would happily score against the wrong protocol or persona
- empty `metric_registry` after PRE (K1) — without invariant 2 STEP 1 would block deep into the workflow rather than at the door
- agent-authored scripts under the skill root (K8) — invariant 10 catches this BEFORE STEP 3 spends turns producing a tainted trace
- a missing/unreadable Role_Catalog directory (invariant 11) would silently disable STEP 0 canonicalization, sending every role down the `role_id_no_catalog_entry` caveat path — surfacing it up front distinguishes "no catalog configured" from "catalog misconfigured"
- an out-of-range `metric_selection_policy` (invariant 13) would otherwise surface as a confusing STEP 1.2 block deep in the run

Surface invariants up front so the run either starts clean or fails loud.
