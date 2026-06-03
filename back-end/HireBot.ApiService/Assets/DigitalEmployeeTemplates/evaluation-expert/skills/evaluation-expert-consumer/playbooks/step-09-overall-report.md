# STEP 9 — buildOverallReport (LLM synthesis, dual-format output)

**Kind**: LLM synthesis (prose only, exactly once)
**Authority**: workflow contract `S9` + K4 + K6 + K7 + K11 + scoring-judgement K5 (`AllIssuesMustBeReported`)
**Inputs**: `evaluation_context`, STEP 5/6/7 artifacts, all ScenarioReport files, `evaluation_report.schema.json`
**Outputs**: two files (see below)

## Two output files

| File | Path | Purpose |
|---|---|---|
| JSON | `./runs/<eval_id>/reports/evaluation_report.json` | Machine-readable, validated against `evaluation_report.schema.json` |
| HTML | `./runs/<eval_id>/reports/evaluation_report.html` | Human-readable, self-contained single-file report |

## Numeric fields are byte-copies (K7)

`dimension_scores` / `overall_score` / `red_line` / `passed` MUST be byte-identical to STEP 5 / 6 / 7 outputs:

| EvaluationReport field | Source file |
|---|---|
| `per_metric_final_scores` | `aggregated_metric_scores.json` |
| `dimension_scores` | `dimension_scores.json` |
| `red_line` (incl. `triggered`, `evidence`) | `red_line_check.json` |
| `overall_score` | `dimension_scores.json` (weighted) |
| `passed` | derived deterministically (see passing criteria below) |

The LLM is allowed to author **only**:

- `executive_summary`
- `strengths`
- `weaknesses`
- `cross_scenario_patterns`
- `improvement_plan`
- `open_questions`

Any LLM-authored value that contradicts the byte-copied numbers is a K7 violation; the report MUST be regenerated.

## Pass/fail derivation

```
passed = (red_line.triggered == false)
         AND (overall_score >= 70)
         AND (∀d ∈ dimension_scores: d.value >= 60)
```

These thresholds are the customer-service-ecommerce defaults; per-template overrides may be declared in the relevant workflow-contract projection.

## Open questions surface (K11 + K16)

`EvaluationReport.open_questions[]` MUST contain entries for:

- every Tier-2 case (`provenance.reliability == "low"`) → caveat `synthesized_from_sop_only_no_user_grounding` (K11)
- every K-rule violation that tainted the run (K8 / K9 / K10 / K12 / K13 / K14 / K16) → severity `critical`
- every duplicate-`scored_at` pair found at STEP 5 input gate (K16) → severity `critical`
- every rejected trace (K14) → list affected `tc_id`s
- missing user consultation when `test_case_status == "missing"` was hit (K11)

Language for Tier-2 / tainted findings MUST be downgraded: use "indicative" / "preliminary" rather than "definitive".

## scenario_report inclusion (K6)

STEP 9 MUST link to `./runs/<eval_id>/reports/scenarios/<tc_id>.report.json` files. **MUST NOT inline them.** STEP 9 also MUST NOT begin until every applicable scenario has a ScenarioReport file.

## HTML generation procedure (K17 — template-only, no free-form HTML)

**K17 (HARD)**: STEP 9 MUST render the HTML by loading `./runtime-schemas/report-template.html` verbatim and replacing only the three contract placeholders. The agent MUST NOT hand-author HTML / CSS / `<script>`. Any HTML produced without first reading the template byte-for-byte is a K17 violation and the run is tainted; the report MUST be regenerated from the template.

1. Load the template at `./runtime-schemas/report-template.html`.
2. Collect all scenario data: for each test case, gather `{ report: <scenario .report.json>, trace: <.trace.json>, enriched: <enriched-case .json> }`.
3. Replace placeholders in the template:

   | Placeholder | Replacement | Notes |
   |---|---|---|
   | `{{REPORT_DATA}}` | full `evaluation_report.json` content as a JSON string | drives the radar chart and headline numbers |
   | `{{SCENARIOS_DATA}}` | array of scenario objects as JSON string | one Tab per scenario |
   | `{{EMPLOYEE_NAME}}` | employee display name | inside `<title>` and the page header |

4. Write the final HTML to `./runs/<eval_id>/reports/evaluation_report.html`.

These three placeholders are **a contract**. If you change the template, keep the placeholder names stable, or update this playbook + `runtime-schemas/report-template.html` together.

### K17 self-check (mandatory before STEP 9 returns)

Before handing the run back, the agent MUST verify all of the following on the produced HTML; failure on any line means K17 violation:

- the file's first 8 lines are byte-identical to the template's first 8 lines (after `{{EMPLOYEE_NAME}}` substitution);
- the file contains exactly one `<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js"></script>`;
- the file contains the `<canvas id="radarChart">` element and the `new Chart(...)` constructor call;
- the file contains zero occurrences of `{{REPORT_DATA}}` / `{{SCENARIOS_DATA}}` / `{{EMPLOYEE_NAME}}`;
- the embedded `<script id="report-data" type="application/json">` and `<script id="scenarios-data" type="application/json">` blocks parse as valid JSON.

## Chinese-narrative presentation contract (K18 — no raw English tokens for end users)

**K18 (HARD)**: Every human-facing string in the HTML MUST be Chinese narrative. The agent MUST NOT surface raw English `metric_code`, `trigger_kind`, `stop_reason`, or signal-token strings (e.g. `tool_call_correctness · missing_required_signal`, `missing_required_tool_call:query_order_status`) as the primary display label. The English code MAY appear only as a small parenthesised technical hint after the Chinese label.

Mandatory presentation rules:

| Element | Wrong (raw token) | Right (Chinese narrative) |
|---|---|---|
| Metric label | `tool_call_correctness` | `工具调用准确度` (with `(tool_call_correctness)` as small grey hint) |
| Dimension label | `process_compliance` | `流程合规` |
| Red-line headline | `tool_call_correctness · missing_required_signal` | `工具调用准确度：得分 10/100，触发"必须工具调用缺失"红线` |
| Red-line evidence | `tc-001: 缺失 query_order_status, query_logistics_tracking` | `物流催派 (tc-001)：未调用 查询订单状态 / 查询物流轨迹 / 提交催派工单` |
| Scenario signal | `missing_required_tool_call:query_product_info` | `必须工具未调用：查询商品信息` |

Sources of truth for Chinese labels (STEP 9 MUST inject all of them into `REPORT_DATA`; the template intentionally has **no built-in metric / tool fallback** so newly added metrics or tools cannot silently regress into raw English codes):

| Field in `REPORT_DATA` | Source of truth | Coverage rule |
|---|---|---|
| `metric_labels` | `metrics/<metric_code>.metric.json#display_name` | MUST contain every `metric_code` referenced by `aggregated_metric_scores`, `red_line.triggers`, and any scenario report's `metric_results[].metric_code`. Missing entries = K18 violation. |
| `tool_labels` | role-catalog tool `display_name` (e.g. `role-catalog/<role>.role.json#tools[].display_name`); fallback to a 2–6 字 Chinese gloss only if the catalog has none | MUST contain every `tool_name` that appears in `expected_tool_calls`, `actual_tool_calls`, and any `missing_required_tool_call:<tool>` signal in the run. |
| `dimension_labels` | `evaluation_context.dimension_meta[<dim>].display_name` (optional; falls back to template's built-in 5-dimension `DIM_CONFIG.label`) | If a customer-template introduces non-default dimensions, they MUST be supplied here. |

`TRIGGER_KIND_LABEL` (`missing_required_signal` / `forbidden_behavior` / `threshold_breach`) is an enum-level vocabulary owned by the workflow contract and lives in the template; new trigger kinds MUST be added to both the template enum and this playbook in the same change.

The `red_line.narratives` field in `evaluation_report.json` MUST already be a list of Chinese narrative sentences (the byte-copy rule K7 still applies for `triggered` / `triggers` — narratives are an additional K18 surface authored by STEP 9, not a paraphrase of numeric values). Recommended shape:

```json
"red_line": {
  "triggered": true,
  "triggers": [...],
  "narratives": [
    "工具调用准确度：得分 10/100，触发「必须信号缺失」红线。原因：4 个用例下 must-criticality 必调工具全部未触发。",
    "物流催派（tc-001）：未调用「查询订单状态」「查询物流轨迹」「提交催派工单」"
  ]
}
```

K18 self-check (mandatory): before STEP 9 returns, search the produced HTML for the following raw tokens — finding any of them is a K18 violation:

- `missing_required_signal`, `forbidden_behavior`, `missing_required_tool_call:`
- any `metric_code` shown as a primary label without a Chinese counterpart on the same line
- any of the 5 dimension codes (`tool_call_correctness`, `interaction_quality`, `functional_completeness`, `problem_resolution`, `process_compliance`) shown without a Chinese label
- any `metric_code` referenced anywhere in `aggregated_metric_scores` / `red_line.triggers` / scenario `metric_results[]` that is missing from `report.metric_labels` (would render as raw English code)
- any `tool_name` referenced in `expected_tool_calls` / `actual_tool_calls` / `missing_required_tool_call:<tool>` that is missing from `report.tool_labels` (would render as raw English code)

## HTML report features

- **能力雷达图**: 5 维度能力覆盖范围，同心圆参考线（0/20/40/60/80/100），灰色虚线目标值（85分），维度标签外置并注明权重
- **场景 Tab 切换**: 每个用例一个 Tab，展示会话聊天历史、模拟器决策过程、工具调用（工具名 + 参数 + 结果）、指标得分、叙述分析
- **自包含**: 单个 HTML 文件，仅依赖 Chart.js CDN，可直接用浏览器打开
- **Tainted run banner**: when `EvaluationReport.open_questions` contains a `critical` entry, the HTML MUST render a red banner above the radar chart explaining the run is tainted

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| LLM "improves" `overall_score` based on its judgement of context | K7 | Report regenerated |
| LLM flips `red_line.triggered` from true to false | K4 + K7 | Report regenerated |
| Inline scenario-report contents into the overall report instead of linking | K6 | Report rejected |
| Begin STEP 9 before STEP 5 / 6 / 7 artifacts exist | K12 | STEP 9 refuses to run |
| Omit Tier-2 caveat in `open_questions` when run has any `reliability=low` case | K11 | Report flagged |
| Omit duplicate-`scored_at` pairs from `open_questions` when STEP 5 input-gate found them | K16 | Report flagged |
| Hand-author HTML/CSS/JS instead of rendering through `runtime-schemas/report-template.html` | K17 | Report rejected, run tainted |
| Surface raw English `metric_code` / `trigger_kind` / signal tokens to end users | K18 | Report rejected |
| Build a per-run helper script (e.g. `scripts/rebuild-eval-report.py`) to bypass STEP 9 | K17 | Script removed, STEP 9 re-executed under the playbook |
