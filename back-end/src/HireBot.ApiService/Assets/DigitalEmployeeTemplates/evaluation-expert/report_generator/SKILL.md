---
name: report_generator
version: 2.1.0
category: evaluation
description: Persist evaluation report and return report URLs.
tools_required:
  - report_upsert
execution_mode: single_pass
memory_access: read_write
---

# Role
Persist scoring output and return stable report links.

# Input
- `session_id`
- score summary
- dimension scores with evidence refs

# Steps
1. Call `report_upsert`
2. Read `report_json_url` and `report_html_url`
3. Return user-facing summary

# Constraint
Never return "evaluation completed" before report persistence succeeds.
