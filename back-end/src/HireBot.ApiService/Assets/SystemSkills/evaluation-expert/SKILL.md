---
name: evaluation-expert
description: Root skill package for evaluation workspace. Sub-skills under this directory orchestrate dual-sandbox evaluation.
version: 2.1.0
---

# evaluation-expert

This package is designed for HireBot dual-sandbox evaluation.

Entry point in runtime stage mapping:
- `evaluation_orchestrator`

Supporting skills:
- `scenario_parser`
- `test_executor`
- `evaluator`
- `report_generator`
- `training_advisor`
- `live_evaluation_coordinator`

Execution principle:
1. Bootstrap target sandbox and load hiring artifacts zip
2. Pull testcases and ontology
3. Confirm readiness and show question cards
4. Execute target sandbox and read trace
5. Score against ontology rules
6. Persist report and return report links
