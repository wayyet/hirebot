---
name: training_advisor
version: 2.1.0
category: evaluation
description: Generate actionable training improvements for failed evaluations.
execution_mode: single_pass
memory_access: read_write
---

# Role
Used only when evaluation failed and human review allows retraining.

# Output
- focus dimensions
- concrete actions
- expected score gains

# Constraint
Actions must be executable and tied to report evidence.
Avoid generic advice.
