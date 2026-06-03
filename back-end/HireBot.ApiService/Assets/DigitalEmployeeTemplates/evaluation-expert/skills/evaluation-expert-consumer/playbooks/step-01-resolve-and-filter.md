# STEP 1 — resolveEmployeeAndCheckTestCases (role-filter into candidate_metrics)

**Kind**: deterministic
**Authority**: workflow contract `S1` + K9 (rewritten) + K10 (in `metric-selection.workflow-contract.projection.json`)
**Inputs**: `employee` (from STEP 0, with canonical `role.role_id`), `metric_registry` (from PRE), `EVALUATION_TEST_CASES_DIR`
**Outputs**: `candidate_metrics`, `dropped_metrics`, `test_case_status`

> **Changed by the metric-curation feature.** Employee resolution + role canonicalization now happen in **STEP 0** (`resolveEmployee`). STEP 1 no longer resolves the employee — it consumes the already-canonical `employee.role.role_id`. STEP 1's role-filter output is now named **`candidate_metrics`** (the deterministic input to STEP 1.2), not `selected_metrics`. STEP 1.2 produces `selected_metrics = (candidate_metrics − removed) ∪ added`.

STEP 1 has TWO duties; both are deterministic and inline. The agent does NOT call the LLM here.

## Duty A — probe test cases

Set `test_case_status` (`ready` / `missing`) by checking whether `./test-cases/` (or `EVALUATION_TEST_CASES_DIR`) holds any case matching `employee.role.role_id` + `employee.scenarios`. This only decides whether STEP 1.5 runs; it does NOT affect metric filtering. (The employee object itself — `role`, `scenarios`, `sop_documents` — was already resolved and persisted by STEP 0.)

## Duty B — role-filter `metric_registry` → candidate_metrics

For each metric `m` loaded by PRE:

- if `employee.role.role_id ∈ m.applicable_roles` OR `"*" ∈ m.applicable_roles` → push to `candidate_metrics`
- otherwise → push to `dropped_metrics` with `{ metric_code, applicable_roles, drop_reason: "role_mismatch" }`

Persist BOTH lists in `evaluation_context.json`. `candidate_metrics` is the deterministic, machine-verifiable input to STEP 1.2 — it is **NOT** the full registry, and it is **NOT** yet the final `selected_metrics`.

## Self-check before continuing (K9 invariants)

```
assert len(candidate_metrics) + len(dropped_metrics) == len(metric_registry)
assert set(candidate_metrics) ∩ set(dropped_metrics) == ∅
for m in candidate_metrics:
    assert employee.role.role_id in m.applicable_roles or "*" in m.applicable_roles
for m in dropped_metrics:
    assert employee.role.role_id not in m.applicable_roles and "*" not in m.applicable_roles
if len(candidate_metrics) == 0 and len(metric_registry) > 0:
    block_or_escalate("no metric applies to this employee role")  # do NOT proceed to STEP 1.2
```

## Worked example

`employee.role = "customer-service-ecommerce"`. Registry has 15 metrics: 7 cross-role generics (every role gets all 7) + 8 role-specific. Correct STEP 1 output keeps **10** metrics and drops **5**:

| Selected (10) | Dropped (5) |
|---|---|
| `tool_call_correctness` (via `*`) | `attendance_rule_compliance` |
| `interaction_empathy` | `bid_clause_completeness` |
| `order_refund_policy_accuracy` | `legal_citation_accuracy` |
| `problem_resolution_completeness` (via `*`) | `code_change_risk_disclosure` |
| `response_clarity_and_structure` (via `*`) | `confidentiality_boundary_compliance` |
| `response_conciseness` (via `*`) |  |
| `factual_accuracy` (via `*`) |  |
| `proactive_clarification` (via `*`) |  |
| `safety_and_ethics_boundary` (via `*`) |  |
| `professional_tone_consistency` (via `*`) |  |

Copying all 15 into `candidate_metrics` is the **K9 violation pattern observed in `runs/eval-xiaofu-001/`** — the run is tainted.

## Cross-step invariant (K10)

STEP 1.2 refines `candidate_metrics` into `selected_metrics`; STEP 2 then narrows further by `applicable_scenarios ∩ tc.scenarios`. Therefore for every enriched test case `tc`:

```
tc.applicable_metrics ⊆ evaluation_context.selected_metrics   (the STEP 1.2 output)
```

STEP 3 / STEP 4 MUST consume `./runs/<eval_id>/enriched-cases/<tc_id>.json` as the authoritative source — NOT the inline copy embedded in `evaluation_context.enriched_test_cases[]`. The two MUST be byte-identical; any divergence taints the run.

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| Copy full `metric_registry` into `candidate_metrics` without role filter | K9 | Run tainted at STEP 1 |
| Skip persisting `dropped_metrics` (auditability hole) | K9 | Run tainted at STEP 1 |
| Emit `selected_metrics` directly at STEP 1 (skipping the candidate→curate split) | K9 | STEP 1 must output `candidate_metrics`; STEP 1.2 owns `selected_metrics` |
| Allow `tc.applicable_metrics` to contain a metric not in `selected_metrics` | K10 | Run tainted at STEP 2 |
