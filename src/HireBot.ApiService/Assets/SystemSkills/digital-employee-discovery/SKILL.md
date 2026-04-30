---
name: digital-employee-discovery
description: Root skill package for HireBot hiring discovery. Sub-skills under this directory orchestrate staged employment-coach conversation, ontology extraction, skill generation, external configuration, and diagnosis.
version: 3.0.0
---

# digital-employee-discovery

This package is designed for the HireBot hiring workflow.

Entry point in runtime stage mapping:
- `employment-coach-conversation`

Supporting skills:
- `ontology_extraction`
- `skill_generation`
- `external_config`
- `diagnosis`

Execution principle:
1. Guide the user through material, skill, external, and packaging-readiness stages.
2. Produce handoff todos and dispatch downstream skills at the correct time.
3. Merge callback artifacts into `ontology/`, `skills/`, `external/`, and `config/`.
4. Run diagnosis before finalize and block packaging until the workflow is complete.
