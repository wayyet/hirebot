# metric-library topic

The `metric-library` topic provides the **enumerated catalog of evaluation sub-metrics** plus its governance rules.

## Files

- `metric-library.metric-catalog.projection.json` — the contract declaring how metric files must look and how they are discovered
- `schemas/metric.schema.json` — JSON Schema for a single hot-loadable metric file
- `REVIEW.md` — review notes and current status

## How the registry is populated

1. At evaluation start (PRE step `loadMetricRegistry`), the runtime scans `evaluation-expert-consumer/metrics/*.metric.json`
2. Each file is validated against `schemas/metric.schema.json`
3. Files passing validation are loaded into `metric_registry`, keyed by `metric_code`
4. New metrics are added by **dropping a new `*.metric.json` file** into the data layer — no code or contract change required

## Trigger signals (this topic is selected when a request contains)

- 指标库 / 指标清单 / 可选指标 / catalog / metric registry
- Combined with explicit artifacts: `metric_code`, `.metric.json`, `applicable_roles`, `scoring_rubric`

## Reading order

1. Read this README
2. Read `REVIEW.md` for governance status
3. Read the projection JSON for the formal contract
4. Read `schemas/metric.schema.json` for the per-file structure
