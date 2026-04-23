# NCrew discovery demo library

This index collects the standard demo cases for `digital-employee-discovery`.
Use it when you want to quickly choose a reference example before running a live discovery session.

Presentation guide:
- `references/presentation-guide.md`

All demos in this library follow the same NCrew framing:
- digital employee = identity carrier
- ontology slice = context layer
- skills = capability layer
- CLI = tool layer

## Available demos

### 1. 售后工单分诊与升级
File:
- `references/support-ticket-triage-demo.md`

Companion dialogue demo:
- `references/support-ticket-triage-dialogue-demo.md`

Best for showing:
- how a service/support entry workflow can be framed as one digital employee
- how ontology slice captures ticket, customer, priority, SLA, and escalation concepts
- how required skills map to triage, knowledge matching, escalation, and SLA monitoring
- how CLI stays at interface level instead of implementation detail
- how the bot should sound in a live discovery conversation

Recommended audience:
- customer service leaders
- support operations
- after-sales teams

### 2. 销售线索分诊与分配
File:
- `references/sales-lead-routing-demo.md`

Companion dialogue demo:
- `references/sales-lead-routing-dialogue-demo.md`

Best for showing:
- how a sales entry workflow can be framed as one digital employee
- how ontology slice captures lead, account, territory, owner, and routing concepts
- how required skills map to validation, de-duplication, scoring, assignment, and SLA tracking
- how CLI connects CRM, marketing systems, enrichment data, and notifications
- how the bot should sound in a live discovery conversation

Recommended audience:
- sales operations
- SDR leaders
- revenue operations

### 3. 财务报销预审
File:
- `references/expense-precheck-demo.md`

Companion dialogue demo:
- `references/expense-precheck-dialogue-demo.md`

Best for showing:
- how a finance entry workflow can be framed as one digital employee
- how ontology slice captures claims, invoices, budgets, policies, and approval paths
- how required skills map to completeness checks, invoice extraction, compliance checks, and route suggestions
- how CLI connects expense systems, OCR, HR, ERP, policy knowledge, and notifications
- how the bot should sound in a live discovery conversation

Recommended audience:
- finance shared services
- finance operations
- internal control teams

## Suggested use order

If the stakeholder is new to the method:
1. Start with this index
2. Open the most familiar demo
3. Explain the four NCrew layers
4. Then run a live discovery session on the stakeholder's own scenario

## Notes for facilitators

When presenting a demo, keep the message simple:
- first explain which problem the digital employee solves
- then explain what ontology slice it needs to understand the scene
- then explain which skills it needs to perform the work
- finally explain which CLI interfaces are needed to reach systems and data

Avoid expanding into internal platform architecture unless explicitly requested.
