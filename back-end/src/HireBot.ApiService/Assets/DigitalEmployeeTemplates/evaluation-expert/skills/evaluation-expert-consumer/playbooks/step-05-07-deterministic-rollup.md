# STEP 5 / 6 / 7 — deterministic roll-up + red-line check

**Kind**: deterministic, LLM-disallowed
**Authority**: workflow contract `S5` / `S6` / `S7` + K4 + K12 + K13
**Inputs**: per-(case, metric) MetricScore files from STEP 4
**Outputs**: three persisted JSON artifacts (see below)

These three steps are pure functions over numeric inputs. The agent performs them inline (read files, compute, write JSON). No LLM call is allowed.

## Required persistence (K12)

Each step MUST persist a typed JSON artifact under `./runs/<eval_id>/` BEFORE the next step begins. STEP 9 byte-copies values from these files (per K7) and MUST NOT run if any is missing.

| Step | Artifact | Key constraint |
|---|---|---|
| 5 | `aggregated_metric_scores.json` | keys ⊇ `{ m.metric_code for m ∈ selected_metrics }` |
| 6 | `dimension_scores.json` | keys **==** `{ m.parent_dimension for m ∈ selected_metrics }` (K13) |
| 7 | `red_line_check.json` | one entry per metric whose `red_line` config is non-null |

## STEP 5 — aggregateAcrossScenarios

For each metric `m ∈ selected_metrics`:

1. Collect all per-case scores: `{ tc_id → MetricScore }` from `./runs/<eval_id>/scores/<tc_id>__<m.metric_code>.json`
2. Apply `m.aggregation_strategy` to collapse the matrix row into a single per-metric score:
   - `worst_case` → take the lowest `overall_score`
   - `simple_average` → arithmetic mean
   - `weighted_average_by_difficulty` → use `test_case.difficulty` as weight
   - `pass_rate` → fraction of cases at or above the implicit pass threshold
   - `coverage` → fraction of required signals/elements actually observed
3. Persist to `aggregated_metric_scores.json` keyed by `metric_code`.

## STEP 6 — rollUpToDimensions (K13)

`dimension_scores.json` key set MUST equal `{ m.parent_dimension for m ∈ selected_metrics }`. Concretely:

- **No key may appear** that is not the `parent_dimension` of any selected metric. Fabricating scores for parent dimensions whose sub-metrics were dropped at STEP 1 is forbidden.
- **Every parent_dimension contributed by `selected_metrics` MUST appear.**
- Each value is a deterministic roll-up of upstream MetricScore values. LLM-disallowed (K4).
- `EvaluationReport.dimension_scores` is a byte-copy (K7) and inherits the same key constraints.

### Validation

```
expected_dims = { m.parent_dimension for m in selected_metrics }
assert set(dimension_scores.keys()) == expected_dims
```

### What NOT to do (the `runs/eval-xiaofu-001/` fabrication bug)

If `selected_metrics` covers only `{interaction_empathy, order_refund_policy_accuracy, tool_call_correctness}` (e.g. `customer-service-ecommerce` after STEP 1 filtering), `dimension_scores.json` MUST contain exactly the parent dimensions those three roll up to:

```
{
  "interaction_quality": ...,
  "functional_completeness": ...,
  "tool_call_correctness": ...
}
```

Inserting `process_compliance=87`, `problem_resolution=82`, etc. when no selected metric rolls up to those dimensions is the **K13 violation observed in `runs/eval-xiaofu-001/`** — the LLM in STEP 9 invented numeric scores for dimensions with no upstream evidence. K13 hard-blocks this; STEP 9 MUST reject any `dimension_scores.json` whose key set is a strict superset.

## STEP 7 — redLineCheck (K4)

STEP 7 is pure code — no LLM, no rationalization. The exact algorithm:

```
red_line_check = {}
for m in selected_metrics:
    cfg = m.red_line                     # may be null → skip
    if cfg is None: continue
    triggered = False
    evidence = []
    if cfg.trigger_kind == "missing_required_signal":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            tc    = enriched_cases[tc_id]
            trace = traces[tc_id]
            must_tools = [t for t in tc.expected_tool_calls if t.criticality == "must"]
            absent     = [t for t in must_tools if t.tool_name not in trace.actual_tool_calls]
            if absent:
                triggered = True
                evidence.append({"tc_id": tc_id, "missing": [t.tool_name for t in absent]})
    elif cfg.trigger_kind == "score_below_threshold":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            if score.overall_score < cfg.threshold:
                triggered = True
                evidence.append({"tc_id": tc_id, "score": score.overall_score, "threshold": cfg.threshold})
    elif cfg.trigger_kind == "forbidden_behavior":
        # observed_signals raised by STEP 4 LLM call must include
        # forbidden_behavior_observed; deterministic code only checks presence
        ...
    elif cfg.trigger_kind == "dimension_floor":
        # consult dimension_scores.json (already persisted at STEP 6)
        if dimension_scores[m.parent_dimension] <= cfg.threshold:
            triggered = True
            evidence.append({"dimension": m.parent_dimension, "score": dimension_scores[m.parent_dimension]})

    red_line_check[m.metric_code] = {
        "trigger_kind": cfg.trigger_kind,
        "triggered": triggered,
        "evidence": evidence,
    }
```

### The LLM is not allowed to overwrite `triggered`

Narrative justifications such as *"tool_call_correctness scored 10/100 but red_line is not triggered because the agent had reasonable substitute behavior"* are **K4 violations** — the `runs/eval-xiaofu-001/` bug. The LLM in STEP 9 may surface the triggered red lines in `executive_summary` prose, but the `red_line.triggered` field is byte-copied from `red_line_check.json` (per K7).

## Built-in red-line floors (customer-service-ecommerce template)

These trigger automatic failure regardless of weighted total:

- `tool_call_correctness = 0` (a metric with `criticality = must` had no matching call in the trace)
- `process_compliance ≤ 30`
- `interaction_quality ≤ 30`
- `functional_completeness ≤ 40`

Per-metric `red_line` blocks declared in `*.metric.json` are unioned with these floors at STEP 7.

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| Skip persisting `aggregated_metric_scores.json` and let STEP 9 LLM compute it | K12 | Run tainted; STEP 9 input-gate rejects |
| Fabricate `dimension_scores` for parent dimensions whose sub-metrics were dropped at STEP 1 | K13 | Run tainted at STEP 6 |
| Call the LLM to "double-check" red-line triggers and let it flip `triggered` | K4 | Run tainted at STEP 7 |
| LLM-rationalize a triggered red line into "not really triggered" in EvaluationReport | K4 + K7 | Report MUST be regenerated |
| STEP 9 begins before all three of `aggregated_metric_scores.json` / `dimension_scores.json` / `red_line_check.json` exist | K12 | STEP 9 refuses to run |
