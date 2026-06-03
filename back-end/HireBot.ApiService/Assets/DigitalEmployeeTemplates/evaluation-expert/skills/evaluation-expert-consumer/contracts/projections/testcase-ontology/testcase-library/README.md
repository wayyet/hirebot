# testcase-library topic

The `testcase-library` topic provides the **enumerated catalog of evaluation test cases** plus its governance rules.

## Files

- `testcase-library.test-case-catalog.projection.json` — the contract declaring how test case files must look and how they are discovered
- `schemas/test-case.schema.json` — JSON Schema for a single hot-loadable test case file
- `REVIEW.md` — review notes and current status

## How the test case set is populated

1. Step 1 first checks the curated catalog (`evaluation-expert-consumer/test-cases/`) for files matching the employee's role and scenarios
2. If matches exist, they are used directly
3. If no matches and SOP / user_scenarios are available, **STEP 1.5 parseTestCases** auto-synthesizes new cases (under SOP-first fallback chain) and writes them to `./runs/<eval-id>/synthesized-cases/`
4. STEP 2 (enrichTestCases) **always runs**, ensuring every selected test case has `applicable_metrics` bound

## Provenance

Every test case carries a `provenance.source` field:

| source | meaning |
|---|---|
| `manual_curation` | Curated by humans for regression / golden set |
| `regression_baseline` | Pinned baseline used to detect regressions |
| `employee_sop` | Auto-synthesized from the employee's SOP document |
| `synthesized_from_user_scenarios` | Auto-synthesized when SOP missing but user provides scenarios |

Only `manual_curation` and `regression_baseline` are stored in the catalog. Synthesized cases live in run-scoped directories.

## Trigger signals

- 测试用例 / 用例库 / test case / scenario / SOP-derived cases / regression set

## Reading order

1. Read this README
2. Read `REVIEW.md`
3. Read the projection JSON
4. Read `schemas/test-case.schema.json`
