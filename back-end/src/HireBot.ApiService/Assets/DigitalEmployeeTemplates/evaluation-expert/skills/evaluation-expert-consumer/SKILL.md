---
name: evaluation-expert-consumer
version: 1.1.0
description: Consumer skill for employee evaluation. Drives a deterministic 13-step workflow that resolves the evaluatee (file / user-dialog / inferred), canonicalizes the role, role-filters then LLM-curates the metric set, parses/enriches test cases, drives scenarios via a driver+simulator dual-role STEP 3, fans out per-metric LLM scoring, and produces both per-scenario reports and a final consolidated evaluation report (JSON + HTML). Triggered by requests like "evaluate employee", "assess performance", "run evaluation", "评估员工", "绩效评估", "客服打分".
keywords: [evaluation, employee, assessment, performance, scoring, 评估, 员工评估, 绩效评估, 打分, 客服评估, 评估专家, evaluation-expert]
metadata:
  openclaw:
    emoji: 📊
upstream_producer_dependencies:
  - producer_skill: ontology_extraction
    contract_index: contracts/projections/ontology_extraction/contract-index.json
    min_version: "1.0.0"
  - producer_skill: role-ontology
    contract_index: contracts/projections/role-ontology/contract-index.json
    min_version: "1.0.0"
  - producer_skill: metric-ontology
    contract_index: contracts/projections/metric-ontology/contract-index.json
    min_version: "1.0.0"
  - producer_skill: testcase-ontology
    contract_index: contracts/projections/testcase-ontology/contract-index.json
    min_version: "1.0.0"
---

# evaluation-expert-consumer

Triggered when the user asks to "evaluate employee", "assess performance", "run evaluation", or act as an "evaluation expert".

It is **template-agnostic**: every employee role is evaluated through the **same deterministic workflow**. Per-role variation lives in **six hot-pluggable data layers** (`./metrics/`, `./test-cases/`, `./runtime-drivers/`, `./simulators/`, `./role-catalog/`, `./employees/`) governed by upstream producer skills or directory-drop convention — NOT by edits to this skill.

## High-level flow

```
              ┌───────────────────────────────────────────────────────────────┐
              │  6 hot-pluggable data layers                                    │
              │  ./metrics/  ./test-cases/  ./runtime-drivers/  ./simulators/   │
              │  ./role-catalog/  ./employees/                                  │
              └───────────────────────────────┬───────────────────────────────┘
                                              │
                                              ▼
  PRE.A loadRoleCatalog ──► STEP 0 resolveEmployee ──► PRE loadMetricRegistry
        (deterministic)        (LLM + user confirm)        (deterministic)
                                              │
                                              ▼
  STEP 1 (candidate_metrics) ──► STEP 1.2 curateMetrics (selected_metrics) ──(test_case_status?)──► STEP 1.5 ─┐
        (deterministic)              (LLM, bounded+auditable)                                                  │  STEP 2
                                                                                                              └────►  ┌──────────────────────┐
                                                                                                                     │  per scenario:        │
                                                                                                                     │  STEP 3 ──► STEP 4   │ × N
                                                                                                                     └──────────┬───────────┘
                                                                                                                                │
                                                          STEP 5 ──► STEP 6 ──► STEP 7 ──────────────────────────────────────────┘
                                                                                  │
                                                          STEP 8 (per scenario) ──► STEP 9 (overall)
                                                                                  │
                                                                   JSON + HTML report
```

Legend: deterministic = white-box; **LLM** = STEP 1.5 (conditional), STEP 4 (fan-out), STEP 8 (per-scenario synthesis), STEP 9 (overall synthesis); **driver subprocess** in STEP 3 only.

## Producer skill dependencies

| Producer | What it publishes | Where this skill reads |
|---|---|---|
| `ontology_extraction` | Workflow contract, scoring/judgement prompt-constraint, metric-selection prompt-constraint | `contracts/projections/ontology_extraction/` |
| `role-ontology` | `role-catalog` projection + `role-catalog-entry.schema.json` | contracts read from `contracts/projections/role-ontology/`; data from `./role-catalog/*.role.json` |
| `metric-ontology` | `metric-catalog` projection + `metric.schema.json` | contracts read from `contracts/projections/metric-ontology/`; data from `./metrics/*.metric.json` |
| `testcase-ontology` | `test-case-catalog` projection + `test-case.schema.json` | contracts read from `contracts/projections/testcase-ontology/`; data from `./test-cases/*.tc.json` |

**Hot-plug rule.** Adding a new metric or test case is a **file drop** under `./metrics/` or `./test-cases/`, never an edit of any `*.projection.json`.

## Execution discipline (HARD RULES)

The host agent executes this skill **by directly performing each STEP**, not by generating intermediate scripts. The following rules are blocking:

1. **No ad-hoc orchestrator scripts (whitelist, not blacklist) — K8.** The ONLY executable files allowed inside this skill package are the ones already committed at skill-creation time:
   - `./runtime-drivers/<driver_id>/run.py` and its sibling files inside the same driver directory
   - any future `runtime-*/<id>/` adapter directory shipped with the skill at creation time

   The agent MUST NOT create ANY new `.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / `Makefile` / `*.cmd` / `*.ps1` file ANYWHERE under the skill root. Full anti-pattern list and recovery: see [`playbooks/step-03-driver-and-simulator-loop.md`](./playbooks/step-03-driver-and-simulator-loop.md#hard-rule-no-orchestrator-scripts-k8).

2. **Drivers are called, not reimplemented.** STEP 3 spawns the selected driver as a subprocess via shell and communicates over stdin/stdout line-JSON. The agent MUST NOT `import` driver modules into agent-authored code, and MUST NOT replicate WebSocket / JWT / trace-writing logic outside the driver directory.

3. **Simulator is the agent's own LLM.** The simulator role profile under `./simulators/<simulator_id>/` is consumed in-process by the host agent's LLM (same brain as STEP 1.5 / 4 / 8 / 9). The agent MUST NOT spawn the simulator as a subprocess and MUST NOT configure an independent LLM key for it.

4. **Per-run directory is data-only.** `./runs/<eval_id>/` may contain JSON artifacts (synthesized-cases, enriched-test-cases, traces, scores, reports, logs, `TAINTED.md`). It MUST NOT contain executable code, agent scratchpads, or duplicated implementations of any STEP.

5. **Determinism stays deterministic.** PRE / STEP 1 / STEP 2 / STEP 5 / STEP 6 / STEP 7 are pure file-scan or arithmetic. The agent performs them inline (read files, compute, write JSON). It MUST NOT call the LLM for these steps and MUST NOT defer them to a generated script.

6. **LLM steps stay in-process.** STEP 1.5 / STEP 4 / STEP 8 / STEP 9 invoke the agent's own LLM brain directly. The agent MUST NOT generate a Python script that calls an HTTP LLM endpoint as a substitute.

If the agent feels tempted to write any `.py` file, that is a signal the prompt or contract is unclear — surface the ambiguity instead of fabricating an orchestrator.

## K-rules at a glance

The workflow contract (`contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`) defines K1–K21. Full table with severity, owning step, taint policy, and recovery: [`playbooks/k-rules.md`](./playbooks/k-rules.md).

| # | Name | Owning step(s) | One-line summary |
|---|---|---|---|
| K1  | `MetricRegistryNonEmpty` | PRE | Empty registry → block_or_escalate |
| K2  | `EnrichTestCasesAlwaysRuns` | STEP 2 | STEP 2 runs unconditionally |
| K3  | `FanOutIsUniformAndPerMetric` | STEP 4 | One LLM call per (case, metric); no batching |
| K4  | `AggregationAndRedLineAreDeterministic` | STEP 5 / 6 / 7 | LLM forbidden; STEP 7 is pure code; STEP 9 cannot flip `triggered` |
| K5  | `SynthesizedCasesIsolatedFromCatalog` | STEP 1.5 | Synthesized cases go to `./runs/<eval-id>/synthesized-cases/`, never `./test-cases/` |
| K6  | `ReportLayerIsTwoTier` | STEP 8 / 9 | STEP 9 links scenario reports, never inlines |
| K7  | `ReportNumericFieldsAreCopiesNotRecomputations` | STEP 8 / 9 | Numbers in reports are byte-copies of upstream |
| K8  | `NoAdhocOrchestratorScripts` | all | No agent-authored executables outside whitelist |
| K9  | `SelectedMetricsRoleFilteredAtStep1` | STEP 1 + STEP 1.2 | STEP 1 produces `candidate_metrics` (deterministic role filter); STEP 1.2 produces `selected_metrics = (candidate − removed) ∪ added`; both lists persisted; skip/fail ⇒ `selected_metrics = candidate_metrics` |
| K10 | `InlineEnrichedCasesMatchPersistedFiles` | STEP 2 / 3 / 4 | Inline `applicable_metrics` ⊆ `selected_metrics` and matches persisted file |
| K11 | `UserScenarioConsultationBeforeSynthesis` | STEP 1.5 | Ask user FIRST; SOP only on explicit decline; consultation persisted |
| K12 | `StepIntermediateArtifactsPersisted` | STEP 5 / 6 / 7 | Three artifacts written before next step begins |
| K13 | `DimensionScoresKeysMatchSelectedMetrics` | STEP 6 | Keys MUST equal `{ m.parent_dimension : m ∈ selected_metrics }` |
| K14 | `DriverProtocolLoopComplete` | STEP 3 | Strict alternation; never close stdin before `end`; send-then-end on final utterance |
| K15 | `StopConditionsAlignedWithExpectedToolCalls` | STEP 1.5 / 2 design + STEP 3 runtime | (design) `stop_conditions.success` not satisfiable without must-tools; (runtime) simulator can't `goal_achieved` while customer's required-info utterance is undelivered |
| K16 | `ScoringMustInvokeEvaluatorLLMPerCaseMetric` | STEP 4 | Real LLM call per (case, metric); distinct `scored_at`; reasoning quotes trace |
| K17 | `EmployeeResolutionProvenanceRequired` | STEP 0 | `employee.employee_provenance` present + valid; low reliability needs caveat; only STEP 0 writes `role_id`; **atomic-fail** taint |
| K18 | `CurateDecisionsMustBeAudited` | STEP 1.2 | Every removed/added decision cites verbatim evidence; bounds enforced; **partial-success** taint |
| K19 | `DriverSubprocessWiringContract` | STEP 3 | Canonical FIFO pad `/tmp/eval-driver/<eval_id>/<tc_id>/{in,out,err,pid}`; `head -n 1` to read; mandatory pre-spawn + post-scenario cleanup; ad-hoc pipe names forbidden |
| K20 | `RunPlanMaterialisedBeforeStep3` | STEP 2.5 / STEP 3 | STEP 2.5 writes `runs/<eval_id>/run_plan.json` with five **literal shell strings** per scenario; STEP 3 executes them **verbatim**; ONLY `<<JSON_PAYLOAD>>` may be substituted at runtime; runtime string composition forbidden |
| K21 | `NegativeCasesMustMeet20Percent` | STEP 1.5 | Synthesized cases MUST include **negative-polarity** cases at target ratio `positive : negative ≈ 80 : 20`; `N ∈ [2,4] ⇒ #negative ≥ 1`; `N ≥ 5 ⇒ #negative ≥ ceil(0.20*N)`; every `negative` MUST set `paired_case_id` OR `polarity_rationale`; silent omission rejected; only `negative_coverage_exemption` allows skipping |

> Namespace note. Each prompt-constraint projection has its own internal K1–K5 namespace. Unless explicitly prefixed (e.g. "scoring-judgement K3"), "K9" / "K12" / etc. always refer to the **workflow contract** namespace above. Also: `playbooks/step-09-overall-report.md` uses internal labels K17 / K18 that **collide** with workflow-contract K17 / K18 — those step-09 labels should be renamed `K-S9-TPL` / `K-S9-NAR` in a future cleanup; until then, any "K17" / "K18" inside `step-09-overall-report.md` means the STEP-9-local rules described there, not these workflow-contract rules.

## The 5 fixed parent dimensions

These names are **frozen** so red-line floors stay stable as sub-metrics evolve. New sub-metrics roll up here via `metric.parent_dimension`.

| Dimension | Default weight | Default red-line floor |
|---|---|---|
| `functional_completeness` | 0.25 | ≤ 40 |
| `interaction_quality`     | 0.20 | ≤ 30 |
| `process_compliance`      | 0.20 | ≤ 30 |
| `problem_resolution`      | 0.15 | (per-template) |
| `tool_call_correctness`   | 0.20 | = 0 (must-tool absent) |

These floors are evaluated by STEP 7 `redLineCheck` after STEP 6 roll-up. New metrics can declare their own `red_line` block in `*.metric.json`; STEP 7 unions them with the floors above.

**Default passing criteria** (customer-service-ecommerce):

- Overall weighted score ≥ 70
- All 5 parent dimensions ≥ 60
- No red lines triggered

## The 11 steps

The authoritative execution graph lives in `contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`. Per-step playbooks live under `./playbooks/`.

| # | Step | Kind | Playbook |
|---|---|---|---|
| PRE.A | `loadRoleCatalog` | deterministic | inline (filesystem scan of `./role-catalog/*.role.json`; fail-soft per role-catalog K1–K3) |
| 0    | `resolveEmployee` | LLM with mandatory confirmation, conditional | [`step-00-resolve-employee.md`](./playbooks/step-00-resolve-employee.md) |
| PRE  | `loadMetricRegistry` | deterministic | inline (filesystem scan of `./metrics/*.metric.json`; fails fast on empty registry) |
| 1    | `resolveEmployeeAndCheckTestCases` | deterministic | [`step-01-resolve-and-filter.md`](./playbooks/step-01-resolve-and-filter.md) — role-filter into `candidate_metrics` |
| 1.2  | `curateMetrics` | LLM, bounded + auditable, conditional | [`step-1.2-curate-metrics.md`](./playbooks/step-1.2-curate-metrics.md) — `selected_metrics = (candidate − removed) ∪ added` |
| 1.5  | `parseTestCases` | LLM, conditional (only when `test_case_status == "missing"`) | [`step-1.5-consult-then-synthesize.md`](./playbooks/step-1.5-consult-then-synthesize.md) |
| 2    | `enrichTestCases` | deterministic, always runs | inline (attaches `applicable_metrics ⊆ selected_metrics` per K10; `*` is wildcard, not a literal) |
| 2.5  | `planRun` | deterministic, NO LLM | [`step-2.5-plan-run.md`](./playbooks/step-2.5-plan-run.md) — materialises `runs/<eval_id>/run_plan.json` (validated against `runtime-schemas/run_plan.schema.json`): per-scenario literal shell strings for the entire driver lifecycle. Owns **K20**. STEP 3 MUST NOT start before this file exists. |
| 3    | `driveEmployeeOnScenario` | dual-role (driver subprocess + host-LLM simulator) | [`step-03-driver-and-simulator-loop.md`](./playbooks/step-03-driver-and-simulator-loop.md) — thin executor: reads `run_plan.scenarios[i].commands.*` and runs them **verbatim** (K19 + K20); ONLY `<<JSON_PAYLOAD>>` may be substituted |
| 4    | `scoreScenario` | LLM fan-out | [`step-04-fanout-scoring.md`](./playbooks/step-04-fanout-scoring.md) |
| LOOP | (STEP 3, STEP 4) per scenario | — | repeat until all enriched cases done |
| 5    | `aggregateAcrossScenarios` | deterministic | [`step-05-07-deterministic-rollup.md`](./playbooks/step-05-07-deterministic-rollup.md) |
| 6    | `rollUpToDimensions` | deterministic | same playbook |
| 7    | `redLineCheck` | deterministic, LLM-disallowed | same playbook |
| 8    | `buildScenarioReports` | LLM synthesis (prose only, per scenario) | inline (numeric fields byte-copied from MetricScore; LLM only writes prose) |
| 9    | `buildOverallReport` | LLM synthesis (prose only, exactly once) | [`step-09-overall-report.md`](./playbooks/step-09-overall-report.md) |

Before any of the above runs, verify the [pre-flight invariants](./playbooks/pre-flight-invariants.md). When a HARD RULE or K-rule fails, follow the [tainted-run lifecycle](./playbooks/tainted-run-lifecycle.md).

## Skill-Specific Constraints

- **Supported deliverables**: evaluation_report, scoring_criteria, workflow_contract, metric_set
- **Supported projection types**: workflow-contract, prompt-constraint, domain-model, metric-catalog, test-case-catalog
- **Supported projection fields beyond the shared minimum**: `concept_mappings.target_path`, `concept_mappings.target_kind`, `constraint_mappings.severity_mapping`, `constraint_mappings.applies_to_layer`, `delivery_artifacts.path`, `metric_catalog.scoring_dimensions`, `evaluation_criteria.red_lines`, `workflow_step.kind`, `workflow_step.fallback_chain`, `workflow_step.always_runs`, `workflow_step.uniform_fanout`, `workflow_step.llm_disallowed`
- **Hot-plug data**:
  - `./role-catalog/*.role.json` (one role per file; basename MUST equal `role_id`)
  - `./employees/<employee_id>.json` (one employee per file; basename MUST equal `employee_id`)
  - `./metrics/*.metric.json` (one metric per file; basename MUST equal `metric_code`)
  - `./test-cases/*.tc.json` (one case per file; basename MUST equal `test_case_id`)
  - `./runtime-drivers/<driver_id>/` (driver manifest + executable entry + helpers)
  - `./simulators/<simulator_id>/` (simulator manifest + system_prompt.md; no executables)
- **Local exclusions**: do not invent unsupported evaluation criteria, do not bypass mapped constraints, do not modify files outside `./runs/<eval_id>/`, do not write runtime evidence back into any `*.projection.json`

## Projection Contracts

This skill is augmented by bound projection contracts discovered under `contracts/projections/**/contract-index.json`.

- Discovery, route selection, and prompt patching are handled by runtime, not by manual rules in this file.
- For human review: read `contract-index.json` first, then the selected topic's `README.md` and `REVIEW.md`, then the chosen `*.projection.json`.
- The selected projection is authoritative for terminology, clarifications, dropped scope, and blocking conditions.
- If `mapping_policy` requires `block_or_escalate`, or `open_questions` is non-empty, do not finalize the output before surfacing the issue.
- Do not recreate items listed in `dropped_items`.

## Path defaults & overrides

| Layer | Default path (relative to skill root) | Override env var |
|---|---|---|
| Role catalog data (`<role_id>.role.json`) | `./role-catalog/` | `EVALUATION_ROLES_DIR` |
| Employee files (`<employee_id>.json`) | `./employees/` | `EVALUATION_EMPLOYEES_DIR` |
| Metrics data | `./metrics/` | `EVALUATION_METRICS_DIR` |
| Test-cases data | `./test-cases/` | `EVALUATION_TEST_CASES_DIR` |
| Per-run artifacts | `./runs/<eval_id>/` | `EVALUATION_RUN_DIR` |
| Synthesized test cases (STEP 1.5 output) | `./runs/<eval_id>/synthesized-cases/` | derived from run dir |
| Runtime drivers (STEP 3 protocol adapters) | `./runtime-drivers/` | `EVALUATION_DRIVERS_DIR` |
| Selected driver id | (none — required field on `evaluation_context.runtime_driver`) | `EVALUATION_DRIVER_ID` |
| User simulators (STEP 3 customer-brain role profiles, consumed by the host agent's own LLM — NOT subprocesses) | `./simulators/` | `EVALUATION_SIMULATORS_DIR` |
| Selected simulator id | (none — required field on `evaluation_context.runtime_simulator`) | `EVALUATION_SIMULATOR_ID` |
| Per-scenario hard turn cap | `turn_budget.hard_max_turns` on each `*.tc.json`; falls back to `evaluation_context.global_turn_cap` (default 30) | — |

## Built-in Route Selection

Route table for the `ontology_extraction` contract index (signals trigger the topic / target_view shown):

| Employee Template | Primary Topic | Default View | Trigger Signals |
|---|---|---|---|
| customer-service-ecommerce | customer-service-ecommerce | workflow-contract | "客服", "售后", "退货", "投诉", "电商", "工单" |
| any                        | metric-selection | workflow-contract | "测试用例", "用例匹配", "指标库", "评估流程", "fan-out", "评估编排" |
| any                        | metric-selection | prompt-constraint | "指标", "评分维度", "评估标准", "维度权重" |
| any                        | scoring-judgement | prompt-constraint | "打分", "评分", "严格评估", "红线", "起评分" |

Within `metric-selection/workflow-contract`, the metric registry holds **15 metrics**: 7 cross-role generic metrics (every role gets all 7) plus 8 role-specific metrics. Per-role metric counts after STEP 1's role filter:

| Role | Role-specific / wildcard hits | Generic | Role total (STEP 1 candidate_metrics) |
|---|---|---|---|
| `customer-service-ecommerce` | `tool_call_correctness`, `interaction_empathy`, `order_refund_policy_accuracy` | 7 | 10 |
| `after-sales-agent` | `tool_call_correctness`, `interaction_empathy` | 7 | 9 |
| `hr-attendance` | `tool_call_correctness`*, `attendance_rule_compliance`, `confidentiality_boundary_compliance` | 7 | 10 |
| `bid-writer` | `tool_call_correctness`*, `bid_clause_completeness`, `confidentiality_boundary_compliance` | 7 | 10 |
| `legal-expert` | `tool_call_correctness`*, `legal_citation_accuracy`, `confidentiality_boundary_compliance` | 7 | 10 |
| `software-engineer` | `tool_call_correctness`*, `code_change_risk_disclosure`, `confidentiality_boundary_compliance` | 7 | 10 |

The 7 generic metrics: `problem_resolution_completeness`, `response_clarity_and_structure`, `response_conciseness`, `factual_accuracy`, `proactive_clarification`, `safety_and_ethics_boundary`, `professional_tone_consistency`. `*` indicates match via `applicable_roles: ["*"]` wildcard. See [`metrics/README.md`](./metrics/README.md#当前内置指标15-个--7-通用--8-角色专属) for full per-metric details.

## References

### Authoritative contracts

- [`metric-selection.workflow-contract.projection.json`](./contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json) — the deterministic flow (now with STEP 2.5 `planRun`) + K1–K21
- [`metric-selection.prompt-constraint.projection.json`](./contracts/projections/ontology_extraction/metric-selection/metric-selection.prompt-constraint.projection.json) — metric-selection guardrails (its own K1–K4 namespace)
- [`scoring-judgement.prompt-constraint.projection.json`](./contracts/projections/ontology_extraction/scoring-judgement/scoring-judgement.prompt-constraint.projection.json) — layered scoring policy (K1–K5 with `applies_to_layer`)
- [`metric-library.metric-catalog.projection.json`](./contracts/projections/metric-ontology/metric-library/metric-library.metric-catalog.projection.json) — metric registry contract
- [`testcase-library.test-case-catalog.projection.json`](./contracts/projections/testcase-ontology/testcase-library/testcase-library.test-case-catalog.projection.json) — test-case registry contract
- [`ontology_extraction/contract-index.json`](./contracts/projections/ontology_extraction/contract-index.json) — route selection index (declares `upstream_producer_dependencies`)

### Data-layer authoring

- [`role-catalog/README.md`](./role-catalog/README.md), [`employees/README.md`](./employees/README.md), [`metrics/README.md`](./metrics/README.md), [`test-cases/README.md`](./test-cases/README.md), [`runtime-drivers/README.md`](./runtime-drivers/README.md), [`simulators/README.md`](./simulators/README.md), [`runs/README.md`](./runs/README.md), [`runtime-schemas/README.md`](./runtime-schemas/README.md)

### Operating playbooks

- [`playbooks/`](./playbooks/) — per-step procedures, K-rules table, pre-flight invariants, tainted-run lifecycle

### Shared templates

- [`templates/CONSUMER_SKILL_PROJECTION_SECTION.md`](./contracts/projections/ontology_extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md), [`templates/NEW_CONSUMER_SKILL_CHECKLIST.md`](./contracts/projections/ontology_extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md), [`references/PROJECTION_CONSUMPTION_GUIDE.md`](./contracts/projections/ontology_extraction/references/PROJECTION_CONSUMPTION_GUIDE.md), [`references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`](./contracts/projections/ontology_extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md)
