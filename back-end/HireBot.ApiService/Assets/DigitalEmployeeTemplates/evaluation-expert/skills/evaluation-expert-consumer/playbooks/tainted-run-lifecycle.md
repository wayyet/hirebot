# Tainted run lifecycle

A run becomes **tainted** when a HARD RULE is violated or a K-rule fails its input-gate validator. Tainted runs are not silently aborted — they continue under restricted rules so the audit trail is preserved.

## When does a run become tainted?

| Trigger | K-rule | Detected by |
|---|---|---|
| Agent authors any executable under the skill root outside the whitelist | K8 | Pre-flight invariant 10; in-step audit |
| `selected_metrics` is the full registry (role filter skipped) | K9 | STEP 1 self-check |
| Inline `enriched_test_cases[]` diverges from persisted `enriched-cases/<tc_id>.json` | K10 | STEP 2 / 3 / 4 input gate |
| STEP 5 / 6 / 7 artifact missing or invalid | K12 | STEP 9 input gate |
| `dimension_scores.json` keys ≠ `{ parent_dimension for m ∈ selected_metrics }` | K13 | STEP 6 self-check |
| Trace fails the four-clause rejection rule | K14 | STEP 4 input gate |
| Multiple `<tc>__<metric>.json` files share `scored_at` | K16 | STEP 5 input gate |
| `MetricScore.scoring_reasoning` cites no trace evidence | K16 | STEP 5 input gate (per-file) |
| `employee.employee_provenance` missing/invalid, or `reliability=low` with no `caveat`, or a later step mutated `employee.role.role_id` | K17 | STEP 0 self-check; STEP 9 input gate |
| A curate decision has empty `evidence`, a missing `curate_log` entry, or a citation failing the source-field-and-substring check | K18 | STEP 1.2 self-check; STEP 2 input gate |

## What happens when tainted

1. **Stop scoring on the tainted output immediately.** Do NOT cite partial outputs as valid.
2. **Drop a `TAINTED.md`** under `./runs/<eval_id>/` (or at the skill root if no run dir exists yet) describing:
   - which K-rule was violated
   - what file or step triggered the violation
   - what the next safe action is
3. **Decide step-by-step what continues:**

   | Tainted scope | What continues | What stops |
   |---|---|---|
   | One trace (K14) | Other scenarios continue; their scores valid | STEP 4 skipped for the tainted `tc_id`; STEP 9 lists tc in `open_questions` |
   | One score file (K16 reasoning) | Other (case, metric) pairs continue; that one is regenerated | None — the score file is regenerated, not skipped |
   | All score files (K16 duplicate timestamps) | Nothing — every score file is suspect | STEP 5 / 6 / 7 / 8 / 9 must wait for re-scoring |
   | STEP 1 metric filter (K9) | Nothing | Entire run halts; restart from STEP 1 with a fresh `eval_id` |
   | STEP 6 dimension fabrication (K13) | Nothing | Re-run STEP 6 deterministically |
   | Agent-authored script (K8) | Nothing | Delete the script, restart from STEP 0 with a fresh `eval_id` |
   | Employee provenance (K17) | **Nothing — atomic-fail** | The whole run fails: we no longer know who was evaluated, so the report is meaningless. Restart from STEP 0 with a fresh `eval_id` |
   | Curate audit gap (K18) | **Other scoring continues** (partial-success): the scores are valid; only the curation transparency is in question | Surface the offending decision; if the three taint-actions partially succeed, continue + record failed actions in `open_questions`; if none succeed, halt |

## K17 recovery procedure

- **Trigger**: STEP 0 produced no `employee_provenance`, or `reliability=low` without a `caveat`, or a step after STEP 0 changed `employee.role.role_id`.
- **Corrective action**: re-run STEP 0 to produce a valid provenance block (re-resolve from file / user-dialog / inferred-fallback as available); fix the offending step that mutated `role_id`.
- **Resume**: K17 is atomic-fail — create a **fresh `eval_id`** and re-run from PRE.A / STEP 0. Do not patch provenance into a half-scored run; the identity uncertainty invalidates everything downstream.
- **Atomicity**: the three taint-actions (write `TAINTED.md`, stop scoring, surface in `open_questions`) are one atomic outcome. If any fails, the entire run fails with a non-success status and emits no successful EvaluationReport. If the `TAINTED.md` write itself fails, still halt scoring and emit the violation to run logs.

## K18 recovery procedure

- **Trigger**: a `curate_log` entry with empty `evidence`, a `removed`/`added` decision with no matching entry, or a citation that does not quote a real substring of the named source field.
- **Corrective action**: re-run STEP 1.2 to regenerate the curate decisions with proper evidence citations; OR, if STEP 1.2 itself is unreliable, set `metric_selection_policy.mode = "never"` so `selected_metrics = candidate_metrics` (the deterministic baseline) and re-run from STEP 1.2.
- **Resume**: K18 is partial-success-tolerant — the already-computed `candidate_metrics` and any valid scores remain usable. Re-run STEP 1.2 forward in the same `eval_id` once the curate decisions are fixed, then continue to STEP 2.
- **Partial-failure rule**: if at least one of the three taint-actions succeeds, accept partial state, continue the evaluation, and record the failed actions in `evaluation_context.open_questions`. If none succeeds, halt with a non-success status and no successful EvaluationReport.

4. **STEP 9 surfaces the violation.** `EvaluationReport.open_questions` MUST list every tainted artifact with severity `critical`, and the language for findings derived from tainted scope MUST be downgraded.

5. **HTML report shows a red banner** above the radar chart whenever any `open_questions` entry is severity `critical`.

## How to recover

A tainted run is **not** auto-deleted. Audit it, then choose a recovery path:

### A. Local fix (one trace / one score file)

If only a single artifact is tainted, you can:

- regenerate that artifact (e.g. re-score a single (case, metric) pair to fix K16 reasoning)
- mark the original tainted artifact in `TAINTED.md` with `superseded_by: <new file>`
- re-run STEP 5 onwards if the regeneration affected aggregated values

### B. Partial restart (one step's output set)

If a deterministic step's output (STEP 5 / 6 / 7) is tainted but its inputs are clean:

- delete the tainted artifact
- re-run that step inline
- re-run all downstream steps (STEP 9 picks up new inputs)

### C. Full restart (K8 / K9 / mass fabrication)

If the violation indicates the agent's process itself broke (authored a script, copied the full registry, or fabricated all scores in a batch):

- create a new `eval_id`
- copy the `evaluation_context.json` if its inputs are still valid
- start from PRE / STEP 1 with a fresh run directory
- keep the tainted directory for audit; do NOT delete it

## What `TAINTED.md` should contain

```markdown
# TAINTED — <eval_id>

**Detected at**: <ISO8601>
**Violated rule(s)**: K9 (SelectedMetricsRoleFilteredAtStep1), K12 (StepIntermediateArtifactsPersisted)
**Trigger**:
- selected_metrics in evaluation_context.json contains all metrics from the registry without role filtering (the eval-xiaofu-001 historical incident copied all 8 metrics that existed at the time; today the registry has 15),
  even though employee.role = "customer-service-ecommerce" should only match a strict subset.
- aggregated_metric_scores.json was never written before STEP 6 ran.

**Affected artifacts**:
- ./runs/<eval_id>/evaluation_context.json
- ./runs/<eval_id>/dimension_scores.json (downstream)
- ./runs/<eval_id>/reports/evaluation_report.json (downstream)

**Recovery path**: Full restart (Section C). Created new eval_id <eval_id_v2>.

**Audit notes**: This run is preserved as a reference fixture demonstrating
the K9 violation pattern.
```

## Anti-patterns during recovery

| Anti-pattern | Why it's wrong |
|---|---|
| Delete `TAINTED.md` to "clean up" the run | Loses the audit trail; STEP 9 can no longer surface the violation |
| Reuse the tainted `eval_id` directory for a new run | Mixes clean and tainted artifacts; the audit trail becomes ambiguous |
| Patch a tainted artifact in place without `TAINTED.md` superseded_by entry | Future readers can't tell which version is authoritative |
| Skip STEP 9 surface step ("the run is broken anyway") | Loses the K-rule self-reporting feedback loop; future operators don't learn |
