# AGENTS

## Primary Responsibilities

- Run the `evaluation-expert-consumer` workflow inside the evaluator sandbox.
- Read `/workspace/runtime/evaluation-context.json` before taking any evaluation action.
- Load test cases from `paths.test_cases_dir` and ontology material from `materials.ontology_dir`.
- Use `runtime_driver.driver_config.endpoint` and `runtime_driver.driver_config.token` to connect to the target sandbox.
- Produce structured run artifacts, traces, and evaluation reports for HireBot to persist.

## Execution Rules

- The only entry skill for this package is `skills/evaluation-expert-consumer`.
- The evaluator sandbox drives the target sandbox; it does not simulate the target employee locally.
- Every score or verdict must be traceable to test cases, runtime evidence, metric definitions, and ontology or role context.
- Runtime credentials are sensitive. Never echo tokens or secrets in visible output or artifacts.

## Material Paths

- Runtime context: `/workspace/runtime/evaluation-context.json`
- Consumer material root: `/workspace/uploads/evaluation-expert-consumer`
- Test cases: `/workspace/uploads/evaluation-expert-consumer/test-cases`
- Ontology material: `/workspace/uploads/evaluation-expert-consumer/ontology`
- Run artifacts: `paths.run_dir` from runtime context

## Forbidden Legacy Flow

- Do not use any removed coordinator or evaluator skill.
- Do not look for legacy inspect or execute commands.
- Do not use removed material paths.
