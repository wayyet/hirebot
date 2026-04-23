# NCrew discovery demo presentation guide

Use this guide when presenting the `digital-employee-discovery` demo library to internal teams, prospects, design partners, or implementation stakeholders.

This is not a product spec.
It is a speaking guide for walking someone through the demos in a clear and repeatable way.

---

## 1. What this demo library is for

This library is meant to help a stakeholder quickly understand one thing:
NCrew can use a guided conversation to turn a vague business scene into four concrete outputs:
- which digital employee should solve which problem
- what ontology slice that employee needs
- which skills the employee needs
- what CLI or system/data interfaces are required

The goal is not to explain the whole platform first.
The goal is to make the business scene legible.

---

## 2. Recommended presentation order

When presenting live, use this order:

### Step 1: start with the index
Open:
- `references/index.md`

Say something like:
- 我先不给你讲整个平台，我先给你看三个具体场景。
- 每个场景最后都会收敛成同样四层：数字员工、Ontology slice、Skills、CLI。
- 你只需要先看哪个场景最像你们自己。

### Step 2: choose the closest business scene
Use the stakeholder's function to choose a starting demo:
- customer service / after-sales -> support-ticket-triage
- sales / revenue operations -> sales-lead-routing
- finance / shared services -> expense-precheck

### Step 3: show the final-output demo first
Open the scene's final-output demo before the dialogue demo.

Reason:
- stakeholders usually care about the finished artifact first
- the final brief shows the destination clearly
- once they understand the destination, they are more willing to look at the conversation process

### Step 4: show the dialogue demo second
After the final-output demo is understood, open the dialogue demo.

Reason:
- this makes the bot feel real
- it demonstrates pacing and tone
- it helps people imagine how a live session would work

### Step 5: transition to live discovery
Say something like:
- 刚才你看到的是完成态和对话过程。
- 如果换成你们自己的场景，我就按同样方式带你们收敛。

---

## 3. The four-layer explanation order

Always explain the result in this order:

### 1. Bring the employee to life first
Start with:
- 它是谁
- 它像团队里的哪一类人
- 它每天上岗后第一件事做什么
- 它最终要对什么结果负责

The stakeholder should first feel that this is a believable virtual employee, not a stack of functions.

Do not start with tools.
If you start with tools, the conversation becomes technical too early.

### 2. Ontology slice
Then explain:
- 它要理解哪些业务对象
- 它要执行哪些动作
- 它依赖哪些资源
- 它必须遵守哪些约束

Use plain language first.
Only use the word ontology after the stakeholder sees the structure.

### 3. Skills
Then explain:
- 这个数字员工在工作过程中会调用哪些能力单元
- 每个 skill 是为了解决哪一步业务问题
- 哪些 skill 可以复用到相邻场景

This is where the stakeholder usually starts seeing repeatability.

A good phrasing is:
- 它先用哪个 skill 看懂情况
- 再用哪个 skill 做判断
- 最后用哪个 skill 完成交付或升级

### 4. CLI
Explain CLI last:
- 它不是底层实现细节
- 它只是说明这些 skill 需要通过什么工具接口去接系统和数据

Keep the interface examples short and legible.

---

## 4. Which demo to use for which audience

### Support / after-sales audience
Start with:
- `references/support-ticket-triage-demo.md`
Then optionally:
- `references/support-ticket-triage-dialogue-demo.md`

What they usually care about:
- high-risk cases not being missed
- escalation speed
- SLA stability
- consistency of triage

### Sales / RevOps audience
Start with:
- `references/sales-lead-routing-demo.md`
Then optionally:
- `references/sales-lead-routing-dialogue-demo.md`

What they usually care about:
- response speed for high-value leads
- duplicate control
- routing fairness and consistency
- auditable assignment decisions

### Finance / shared services audience
Start with:
- `references/expense-precheck-demo.md`
Then optionally:
- `references/expense-precheck-dialogue-demo.md`

What they usually care about:
- reducing repetitive review work
- policy consistency
- exception detection
- reducing back-and-forth for补件

---

## 5. Common stakeholder questions and how to answer them

### Q1. Why not just say "AI agent"?
Suggested answer:
- 可以，但在 NCrew 里我们会更明确地把它落到“数字员工”。
- 因为企业更容易围绕“谁负责什么结果”来理解，而不是围绕一个抽象 agent 来理解。

### Q2. Why do we need ontology slice?
Suggested answer:
- 因为企业用户说的很多词，在不同公司里含义不一样。
- Ontology slice 不是大而全知识库，而是这个数字员工完成这个场景所需的最小业务语境。

### Q3. Why separate skills from CLI?
Suggested answer:
- Skills 说的是“它会做什么”。
- CLI 说的是“它通过什么接口去接系统和数据”。
- 一个是能力层，一个是工具层，不混在一起更容易设计和治理。

### Q4. Do we need to build everything before starting?
Suggested answer:
- 不需要。
- discovery 的目标就是先把场景、数字员工、ontology slice、skills、CLI 轮廓收敛出来，先知道要做什么，再决定实现优先级。

### Q5. What if our process is more complex than the demo?
Suggested answer:
- 这些 demo 只是演示收敛方式，不是限制你们的复杂度。
- 真正跑 live discovery 时，会按你们自己的规则和系统来展开。

---

## 6. How to transition from demo to live discovery

A good transition script:
- 你刚才看到的是一个标准样例。
- 如果换成你们自己的场景，我不会先问系统架构，而是先帮你把这四层收敛出来。
- 收敛清楚之后，我们再看哪些部分值得优先实现，哪些部分需要系统对接。

Good next question to ask the stakeholder:
- 如果我们今天只选一个最值得做的场景，你最想先梳理哪个？

---

## 7. What not to do in a presentation

Avoid these mistakes:
- do not start with orchestration, hooks, memory, sandbox, or lifecycle internals
- do not dump every demo at once
- do not explain ontology as an abstract theory lecture
- do not let CLI discussion turn into implementation detail too early
- do not skip directly to tooling before the business problem is clear

---

## 8. Short version for a 5-minute walkthrough

If you only have 5 minutes:
1. open `references/index.md`
2. pick the closest demo
3. show the final brief only
4. explain the four NCrew layers in under 60 seconds
5. ask whether they want to try their own scene

Suggested 60-second framing:
- 我们不是先讨论整个平台，而是先把一个具体场景收敛清楚。
- 收敛的结果固定是四层：哪个数字员工、需要什么 Ontology slice、需要哪些 Skills、要通过哪些 CLI 去接系统和数据。
- 如果这四层清楚了，这个场景值不值得做、优先做什么、要接哪些系统，都会清楚很多。

---

## 9. Suggested next asset

After this guide, the most natural next asset is:
- a short external-facing slide outline based on the same three demos

That would make the library easier to use in meetings without opening raw markdown files.
