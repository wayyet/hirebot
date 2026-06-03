# STEP 1.2 — curateMetrics (LLM, bounded + auditable)

**Kind**: LLM-bounded-and-auditable
**Authority**: workflow contract `S1_2` + K9 (rewritten) + K18 (in `metric-selection.workflow-contract.projection.json`)
**Runs**: after STEP 1 (`candidate_metrics`), before STEP 1.5 / STEP 2
**Inputs**: `candidate_metrics`, `metric_registry`, `employee.industry`, `employee.role.responsibility_tags`, `employee.job_responsibilities`, `metric_selection_policy`
**Outputs**: `evaluation_context.selected_metrics`, `evaluation_context.curate_log[]`, appended `user_consultation_log` entries

STEP 1.2 refines the deterministic role-filter result by reasoning over the employee's actual industry and responsibilities plus each metric's semantic fields. It can **remove** false-positive string matches and **add** semantically-correct misses.

> **The K9 equation.** `selected_metrics = (candidate_metrics − removed) ∪ added`. `candidate_metrics` stays deterministic and machine-verifiable; `removed` / `added` are LLM-authored but every decision is audited in `curate_log` (K18).

## Invocation gate

```
mode == "never"   → SKIP; selected_metrics = candidate_metrics
mode == "always"  → RUN unconditionally (even if size-trigger evaluation errors)
mode == "auto"    → RUN iff  len(candidate_metrics) < size_triggers.candidate_count_lower_bound (default 3)
                          OR len(candidate_metrics) > size_triggers.candidate_count_upper_bound (default 15)
                    else SKIP; selected_metrics = candidate_metrics
```

Defaults (when `metric_selection_policy` or any field is omitted): `mode=auto`, `max_metrics=8`, `min_dimensions_covered=1`, `auto_apply_threshold=0.7`, `size_triggers={3,15}`.

## Curate algorithm

### 1. Build the curate prompt (one LLM call)

Slices fed to the LLM:
- `employee.{industry, role.responsibility_tags, job_responsibilities}`
- `candidate_metrics[*].{metric_code, description, tags, industry, responsibility_tags}` — the keep/remove pool
- `(metric_registry − candidate_metrics)[*].{same fields}` — the addable pool
- `metric_selection_policy`

### 2. LLM emits structured decisions

```jsonc
{
  "removed": [ { "metric_code": "...", "decision": "removed", "evidence": [...], "confidence": 0.0-1.0 } ],
  "added":   [ { "metric_code": "...", "decision": "added",   "evidence": [...], "confidence": 0.0-1.0 } ]
}
```

- `removed[] ⊆ candidate_metrics` (semantically inappropriate string matches)
- `added[] ⊆ (metric_registry − candidate_metrics)` (string-match missed)
- the two arrays MUST be disjoint
- `len(removed) + len(added) ≤ 2 × len(metric_registry)`

### 3. Deterministic post-processing (orchestrator, NOT the LLM)

1. Validate subset + disjointness constraints. Violation → failure handling (§ below).
2. Resolve low-confidence adds via user confirmation (see Confidence gate).
3. Compute `selected_metrics = (candidate_metrics − removed) ∪ confirmed_adds`.
4. Enforce bounds:
   - `len(selected_metrics) > max_metrics` → `block_or_escalate` + curate_log entry citing observed vs configured.
   - distinct `parent_dimension` count `< min_dimensions_covered` → `block_or_escalate`.
5. Persist `curate_log` and verify K18 (every decision has an evidence citation).

## Confidence gate (R13)

| confidence vs `auto_apply_threshold` | user prompt? | included? | `confirmed_by_user` |
|---|---|---|---|
| `>= threshold` (default 0.7) | no | yes | `"auto_applied"` (string) |
| `< threshold`, user confirms | yes | yes | `true` (boolean) |
| `< threshold`, user declines | yes | no | `false` (boolean) |
| `< threshold`, 300s timeout | yes | no | `false` (boolean) + timeout recorded |

Multiple low-confidence adds are prompted **one at a time, in curate_log order** (R13.6). Every prompt + response is persisted to `evaluation_context.user_consultation_log` using the same record shape as the K11 consultation log.

## Evidence citation (K18)

Every `removed` / `added` decision MUST carry ≥1 evidence citation:

```jsonc
"evidence": [
  { "source_field": "employee.job_responsibilities", "quote": "handles refund disputes" }
]
```

- `source_field` ∈ { `employee.industry`, `employee.job_responsibilities`, `employee.role.responsibility_tags`, `metric.description`, `metric.tags`, `metric.industry`, `metric.responsibility_tags`, `metric.complementary_metrics`, `metric.exclusive_with` }
- `quote` is a verbatim (case-sensitive, contiguous), ≥1-char substring of that field's actual value in the run's data.
- `len(curate_log) == len(removed) + len(added)`; each `(removed ∪ added)` metric_code appears in exactly one entry.

A decision with empty evidence, a missing curate_log entry, or a citation that fails the source-field-and-substring check → **K18 taint** (see `tainted-run-lifecycle.md`).

## Failure handling — degrade to candidate (safety property)

Any of: curator failure, malformed output, subset-constraint violation, missing/null input, or 30s timeout →
**fall back to `selected_metrics = candidate_metrics`** + `open_question` identifying the failure category; the run proceeds.

> **This is the single most important property of STEP 1.2 (design Decision #1 / Correctness Property 10).** Worst case, STEP 1.2 no-ops and the evaluation runs on the deterministic role-filter result — exactly today's behavior. Adding STEP 1.2 can only refine or no-op, never degrade.

## Backward compatibility (R16.4/16.5)

- Legacy context with only `selected_metrics` (no `candidate_metrics`) → treat `selected_metrics` as `candidate_metrics` + `open_question` (`legacy_selected_metrics_treated_as_candidate_metrics`).
- Context with both `selected_metrics` and `candidate_metrics` → `candidate_metrics` wins + `open_question` (`legacy_selected_metrics_ignored_in_favor_of_candidate_metrics`).

## Worked example

`employee.role.role_id = customer-service-ecommerce`, `industry = ecommerce`, `job_responsibilities = "处理售前咨询、退款、物流投诉，无需撰写正式文档"`. STEP 1 produced 10 candidates (3 role-specific + 7 generics). Suppose a future registry also string-matched `bid_clause_completeness` onto this role by mistake (it didn't here, but illustrating):

- **removed**: `bid_clause_completeness` — evidence `{source_field: "employee.job_responsibilities", quote: "无需撰写正式文档"}`, confidence 0.9 → auto-applied.
- **added**: none (the 7 generics already cover the cross-role concerns).
- `selected_metrics` = 10 candidates (unchanged here); `curate_log` has the one removal.

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| Decision with empty `evidence` | K18 | taint |
| Batch-fabricate decisions without per-decision evidence | K18 | taint |
| `selected_metrics` exceeds `max_metrics` | R12.8 | block_or_escalate |
| Silently include a low-confidence add without user confirmation | R13.1 | unaudited injection |
| Block the whole run on curator failure instead of degrading to candidate | R10.8 / CP10 | violates safety property |
| Let STEP 1.2 write `employee.role.role_id` | K17 / R6.5 | unauthorized_role_id_mutation |
