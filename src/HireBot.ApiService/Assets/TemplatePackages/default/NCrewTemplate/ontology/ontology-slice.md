# BRU Ontology Slice

> 业务分析本体切片 | NCrew 上下文层

BRU 理解的不是某个行业的业务知识，而是一套**跨行业通用的业务分析本体**。

---

## 4.1 Entities（实体）

| 实体 | 定义 | 案例对应 |
|---|---|---|
| Stakeholder | 利益相关者（发起人/决策者/实际用户/影响者）| 部长=决策者，组长=实际用户 |
| Business Objective | 业务北极星指标 | "不被老板骂，能做准决策" |
| Expressed Need | 表达出来的需求（往往是技术解法）| "要一个大屏" |
| Underlying Pain | 真实痛点（被掩盖的业务问题）| 交付延期率无法可视化 |
| Process (As-Is) | 现状流程 | Excel+白板记录，ERP 滞后 24h |
| Process (To-Be) | 目标流程 | 实时异常预警→快速决策 |
| Gap | As-Is 与 To-Be 之间的差距 | 数据实时性差距 + 录入责任分歧 |
| Constraint | 系统性约束（数据/政策/资源）| ERP 数据滞后，录入工作量分摊分歧 |

---

## 4.2 Actions（动作）

| 动作 | 定义 |
|---|---|
| Elicit | 采集：从利益相关者处获取信息 |
| Map | 映射：将模糊需求映射到业务架构 |
| Detect | 探测：识别真假需求、逻辑断点、数据风险 |
| Validate | 验证：假设验证，确认业务方容忍度 |
| Specify | 规格化：将人话转化为无歧义的结构化描述 |
| Flag | 标记：明确标注冲突点和待决事项 |

---

## 4.3 Resources（资源）

| 资源 | 用途 |
|---|---|
| Domain Knowledge Base | 行业/领域背景知识（用于预诊断）|
| Stakeholder Map | 利益相关者矩阵（职能+立场+影响力）|
| Business Frameworks | BMAD 等业务分析框架 |
| Output: Spec Document | Business Spec（结构化输出物）|

---

## 4.4 Constraints（约束）

| 约束 | 说明 |
|---|---|
| Scope Boundary | 范围边界（不做什么）|
| Data Quality | 数据质量风险（已知/未知）|
| Decision Authority | 最终决策权归属 |
| Political Risk | 跨部门博弈风险（谁担责）|
