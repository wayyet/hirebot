# testcase-ontology projection contracts (consumer-side mirror)

This directory mirrors the projection contracts produced by the `testcase-ontology` skill, consumed by `evaluation-expert-consumer`.

## Layout

```
testcase-ontology/
├── README.md
├── contract-index.json
└── testcase-library/
    ├── README.md
    ├── REVIEW.md
    ├── testcase-library.test-case-catalog.projection.json
    └── schemas/
        └── test-case.schema.json
```

## Boundaries

- **Contract layer (this directory)**: declares the schema, governance, discovery rules, and provenance policy for evaluation test cases.
- **Data layer (`evaluation-expert-consumer/test-cases/`)**: holds the actual `*.tc.json` instances (one test case per file), hot-loadable.

The data layer must validate against `testcase-library/schemas/test-case.schema.json`.

## Producer skill

The notional producer skill is `testcase-ontology`. If later created as a standalone skill, this directory becomes a synced mirror of its export.

## Topics

- **`testcase-library`**: enumerated catalog of evaluation test cases (input + expected_output [+ optional applicable_metrics]).

## Relationship with `STEP 1.5 parseTestCases`

Auto-synthesized test cases (when SOP exists or when user provides scenarios) are written to **`./runs/<eval-id>/synthesized-cases/`**, NOT to this catalog. The catalog only holds **manually curated / regression baseline** test cases.

## Trigger signals

- `测试用例` / `test case` / `场景` / `scenario` / `SOP` / `期望行为` / `expected output`
