# Evaluation Expert

This package is the evaluator sandbox package for HireBot evaluation sessions.
The package root remains `evaluation-expert`, but the only runtime entry skill is:

```text
skills/evaluation-expert-consumer
```

## Runtime Model

1. The platform creates a target sandbox and loads the employee being evaluated.
2. The platform creates an evaluator sandbox and loads this `evaluation-expert` package.
3. The evaluator sandbox receives `workspace/runtime/evaluation-context.json`.
4. `evaluation-expert-consumer` reads the runtime context, material paths, runtime driver config, simulator config, metric catalog, and role catalog.
5. The consumer workflow drives the target sandbox through `runtime_driver.driver_config`.
6. The consumer workflow writes structured run artifacts and evaluation reports for the platform to persist.

## Package Layout

```text
evaluation-expert/
  manifest.json
  config/
    AGENTS.md
    IDENTITY.md
    MEMORY.md
    SOUL.md
    workspace.json
  ontology/
    evaluation-baseline.md
  skills/
    evaluation-expert-consumer/
      SKILL.md
      runtime-drivers/
      simulators/
      metrics/
      role-catalog/
      runtime-schemas/
```

## Entry Contract

- `manifest.json` must keep `entry_skill` set to `skills/evaluation-expert-consumer`.
- Evaluation materials are expected under `/workspace/uploads/evaluation-expert-consumer`.
- Test cases are expected under `/workspace/uploads/evaluation-expert-consumer/test-cases`.
- Ontology material is expected under `/workspace/uploads/evaluation-expert-consumer/ontology`.
- Runtime context is expected at `/workspace/runtime/evaluation-context.json`.
- Target sandbox access must come from `runtime_driver.driver_config.endpoint` and `runtime_driver.driver_config.token`.

## Boundary Rules

- Do not infer target sandbox connection settings outside runtime context.
- Do not print access tokens, secrets, or credentials in user-visible output.
- Do not write directly to the platform database.
- Do not execute any legacy evaluator flow.
