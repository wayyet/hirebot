# STEP 4 — scoreScenario (LLM fan-out)

**Kind**: LLM fan-out
**Authority**: workflow contract `S4` + K3 + K16 + scoring-judgement prompt-constraint K1–K4 (per_metric_fanout_prompt layer)
**Inputs**: ExecutionTrace per scenario, enriched test case, metric definition, scoring-judgement rules slice
**Output**: `./runs/<eval_id>/scores/<tc_id>__<metric_code>.json` per (case, metric) pair

## Why fan-out (not bundled)

A single prompt that bundles "all metrics + all rubrics + full trace + output schema" explodes token usage and dilutes attention. STEP 4 instead runs **one slim LLM call per `(test_case, metric)` pair**, where each prompt is built from:

- the relevant slice of `scoring-judgement.prompt-constraint.projection.json` (only constraints whose `applies_to_layer = per_metric_fanout_prompt`)
- the single metric's `scoring_rubric` and `runtime_slice_selector`
- the runtime data filtered through that selector (typically: this test case's expected output + this scenario's trace, scoped further per metric)
- the strict response schema `metric_score.schema.json`

K3 enforces this: exactly one LLM invocation per `(test_case, metric)` pair where `metric_code ∈ enriched_test_cases[tc].applicable_metrics`. Batching multiple metrics or scenarios into a single call is forbidden.

## Why red-line judgement is deterministic, not LLM (K4)

LLMs may underweight red lines under social/empathy pressure. STEP 4 LLM calls may only **raise `observed_signals`** (e.g. `missing_required_tool_call`). The final pass/fail decision is computed in STEP 7 by deterministic code, using each metric's declared `red_line` config. The LLM never sees `red_line_passed` and cannot return it.

Note: `metric_score.schema.json` deliberately does NOT include a `red_line_passed` or `pass_fail` field.

## Hard rules (K16)

1. **No batch fabrication.** The agent MUST NOT compute scores from its own knowledge of the trace and metric definitions, then emit all score files at once with a uniform timestamp. Each prompt is built from (i) that exact trace + (ii) that exact metric definition + (iii) the rubric/red-line config + (iv) per-case `stop_conditions`, and submitted independently to the evaluator LLM.

2. **Real `scored_at`.** `MetricScore.scored_at` MUST be the real ISO8601 timestamp captured at LLM-response receipt time, accurate to at least the second, and **different across distinct LLM calls** (millisecond/microsecond drift expected).

3. **Duplicate-timestamp taint.** If MORE THAN ONE score file in the same run shares an identical `scored_at` value (string equality), the run is marked tainted and STEP 9 MUST list every duplicate-timestamp pair in `open_questions` with severity `critical`. This catches the **`runs/eval-soul-001/`** pattern where all 10 score files carried `scored_at = "2026-05-29T14:30:00Z"` verbatim.

4. **Reasoning must cite evidence.** `MetricScore.scoring_reasoning` MUST quote at least one concrete substring from `dialog_turns` or `actual_tool_calls` of the trace being scored. Reasoning that consists only of generic phrases ("based on standards", "reasonable demonstration result", "as a typical case", "基于评估标准生成") with no observable evidence is rejected as fabrication; the score file MUST be regenerated.

5. **Forbidden shortcut (mirror of K14).** The agent MUST NOT skip the per-(case, metric) LLM call citing "demonstration", "preview", "sample run", "illustrative scoring", "time pressure", or any other reason. There is no demonstration mode — every metric on every case requires a real evaluator LLM call.

## Validation pseudo-code (applied at STEP 5 input gate)

```
scored_at_set = { read(f).scored_at for f in scores/*.json }
assert len(scored_at_set) == count(scores/*.json), \
    "K16 violation: duplicate scored_at across score files — evaluator LLM was not invoked per (case, metric)"

for f in scores/*.json:
    score = read(f)
    assert score.scoring_reasoning quotes at least one substring of \
           traces[score.test_case_id].dialog_turns OR actual_tool_calls
```

## scoring-judgement K-rules baked into the per-metric prompt

The per-metric fan-out prompt MUST inject `scoring-judgement.prompt-constraint.projection.json` constraints whose `applies_to_layer == "per_metric_fanout_prompt"`:

| scoring-judgement K# | Rule | Effect on scoring |
|---|---|---|
| K1 | `BaselineIsFiftyAndEvidenceDriven` | Every dimension starts at 50; up only with concrete evidence; down only with quotable issues; no vibe-based scoring |
| K3 | `HighScoresMustBeRare` | Scores ≥ 80 must be exceptional; most acceptable employees land in 70–75 range |
| K4 | `EveryAdjustmentNeedsEvidence` | Every adjustment cites the conversation snippet or tool call that supports it; un-evidenced adjustments removed |

scoring-judgement K2 (`RedLineTriggersAreNonNegotiable`) and K5 (`AllIssuesMustBeReported`) live elsewhere — see `step-05-07-deterministic-rollup.md` and `step-09-overall-report.md` respectively.

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| Bundle all metrics for one trace into a single LLM call | K3 | Run tainted at STEP 4 |
| Emit all `<tc>__<metric>.json` files with the same `scored_at` timestamp | K16 | Run tainted; STEP 5 input-gate rejects |
| Compute scores from your own analysis without invoking the LLM | K16 | Run tainted; reasoning lacks trace quotes |
| Set `red_line_passed` or `pass_fail` in MetricScore | K4 | Schema rejects the field |
| Use generic boilerplate ("based on rubric") as `scoring_reasoning` | K16 | Score file MUST be regenerated |
| Skip a (case, metric) pair because "this metric obviously doesn't apply" | K3 / K16 | All `applicable_metrics` are mandatory at STEP 4 |
