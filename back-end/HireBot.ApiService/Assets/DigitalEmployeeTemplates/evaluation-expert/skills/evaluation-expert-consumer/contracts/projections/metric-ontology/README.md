# metric-ontology projection contracts (consumer-side mirror)

This directory mirrors the projection contracts produced by the `metric-ontology` skill, consumed by `evaluation-expert-consumer`.

## Layout

```
metric-ontology/
├── README.md
├── contract-index.json           # route selection index
└── metric-library/
    ├── README.md
    ├── REVIEW.md
    ├── metric-library.metric-catalog.projection.json    # contract for the metric registry
    └── schemas/
        └── metric.schema.json    # JSON Schema for a single hot-loadable .metric.json file
```

## Boundaries

- **Contract layer (this directory)**: declares the schema, governance rules, and routing for evaluation metric definitions.
- **Data layer (`evaluation-expert-consumer/metrics/`)**: holds the actual `*.metric.json` instances (one metric per file), hot-loadable.

The data layer must validate against `metric-library/schemas/metric.schema.json`. The contract layer is stable; the data layer is what business stakeholders extend.

## Producer skill

The notional producer skill is `metric-ontology`. If the producer skill is later created as a standalone skill, this directory becomes a synced mirror of its `contracts/projections/exports/` output.

## Topics

- **`metric-library`**: enumerated catalog of evaluation sub-metrics. Default target view: `metric-catalog`.

## Trigger signals

- `指标` / `指标库` / `指标清单` / `metric` / `catalog` / `挑选指标` / `evaluation dimension`
