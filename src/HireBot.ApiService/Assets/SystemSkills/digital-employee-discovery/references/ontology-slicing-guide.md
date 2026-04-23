# Ontology slicing guide for digital employee discovery

Use this guide when the scenario is concrete enough to define a minimal ontology slice.

## Principle

The goal is not to map the whole enterprise ontology.
The goal is to isolate only the concepts required for one digital employee to perform one scenario safely and well.

## Four buckets

### 1. Entities
Ask:
- What business objects appear repeatedly in the work?
- Which objects affect decisions or outputs?
- Which objects must be distinguished because enterprise definitions matter?

Examples:
- customer
- order
- invoice
- claim
- contract
- report
- campaign
- supplier

### 2. Actions
Ask:
- What must the employee actually do?
- Which actions create business value or move the task forward?
- Which actions require approval or are irreversible?

Examples:
- classify
- summarize
- generate
- reconcile
- assign
- escalate
- approve
- notify

### 3. Resources
Ask:
- Which systems, channels, or knowledge bases are required?
- Where does source truth live?
- Which resources are only consulted, and which are written back?

Examples:
- CRM
- ERP
- ticketing system
- document repository
- policy knowledge base
- enterprise chat
- email gateway

### 4. Constraints
Ask:
- What rules must the employee obey?
- What approvals, thresholds, or red lines exist?
- Which compliance or brand requirements change how work is done?

Examples:
- amount > 10,000 requires approval
- customer tier determines SLA
- no write access to payroll data
- outbound content must follow brand template
- high-risk cases must escalate to legal

## Minimal slice test

A slice is good enough if it can answer:
- what objects the employee reasons about
- what actions it can take
- what resources it depends on
- what boundaries it cannot cross

If a concept does not affect one of those four questions, it probably does not belong in the initial slice.

## Practical anti-patterns

Avoid:
- copying an entire department ontology into the scene
- listing generic concepts that never change a decision
- hiding constraints inside long narrative paragraphs
- mixing system names and business objects in the same bucket

## Recommended output format

Use short bullets.
If needed, add a one-line note after a concept explaining what makes it company-specific.
