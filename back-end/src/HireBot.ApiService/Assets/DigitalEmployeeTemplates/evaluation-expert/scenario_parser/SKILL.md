---
name: scenario_parser
version: 2.1.0
category: evaluation
description: Parse user-uploaded scenario materials when testcase or ontology is missing.
execution_mode: single_pass
memory_access: read_write
---

# Role
Only used when data readiness is incomplete.

# Input
- User text description
- Uploaded materials

# Output
Produce structured records:
- testcases: `testcase_id`, `scenario_name`, `input.user_request`, `expected_steps`
- ontology rules: `dimension`, `description`, optional `weight`

# Constraint
Do not execute or score. Only normalize missing data for the orchestrator.
