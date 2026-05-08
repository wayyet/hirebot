---
name: bru-memory
description: >
  BRU 记忆管理：三层记忆架构（L1会话/L2项目/L3知识）及调用策略。
---

# BRU — Memory

> 记忆管理 | NCrew 配置层

---

## L1 — Session Memory（会话层）

当前会话期间的上下文信息，会话结束即清理。

| 字段 | 类型 | 内容 |
|------|------|------|
| `sessionId` | string | 会话唯一标识 |
| `phase` | SessionPhase | 当前阶段（intake/priming/elicitation/disambiguation/reconstruction/flagging/spec/bridge）|
| `rawInput` | string | 原始输入 |
| `contextPriming` | ContextPrimingOutput | 检测到的领域 + Stakeholder 画像 + 痛点信号 |
| `elicitationRecords` | ElicitationRecord[] | 结构化访谈记录（stakeholder × goal × pain × constraint）|
| `painDemandPairs` | PainDemandPair[] | Pain-Demand Pair 对照表 |
| `asIsToBe` | AsIsToBeFlow | As-Is/To-Be 流程 + Gap 分析 |
| `flaggedIssues` | FlaggedIssue[] | 冲突点和待决事项清单 |
| `businessSpec` | BusinessSpec | 最终输出的 Business Spec |
| `contextSeed` | ContextSeed | Bridge to Forge 的 Context Seed |
| `createdAt` | Date | 创建时间 |
| `updatedAt` | Date | 最后更新时间 |

**调用策略**：全量加载，主动检索，实时更新。

---

## L1.5 — Ontology Slice（本体切片）

Session 内生成的完整本体结构，可独立持久化供下游使用。

| 字段 | 类型 | 内容 |
|------|------|------|
| `stakeholders` | Stakeholder[] | 利益相关者 |
| `businessObjectives` | BusinessObjective[] | 业务北极星指标 |
| `expressedNeeds` | string[] | 表达出来的需求（往往是技术解法）|
| `underlyingPains` | string[] | 还原后的真实痛点 |
| `gaps` | Gap[] | As-Is vs To-Be 差距 |
| `scopeBoundary` | ScopeBoundary | 范围边界 |
| `constraints` | SystemConstraint[] | 系统性约束 |
| `dataQuality` | DataQuality[] | 数据质量评估 |
| `stakeholderMap` | StakeholderMap | 利益相关者矩阵 |
| `actionHistory` | ActionResult[] | BRU 分析动作记录 |
| `detectedDomain` | string | 检测到的业务领域 |
| `confidence` | number | 本体切片整体置信度（0-1）|

---

## L2 — Project Memory（项目层）⚡ 规划中

同一项目多轮会话的记忆，持续维护直到项目结束。

| 字段 | 内容 |
|------|------|
| `projectId` | 项目唯一标识 |
| `ontologySlice` | 跨会话聚合的本体切片 |
| `stakeholderConsensus` | 各方已达成的共识点 |
| `unresolvedIssues` | 尚未解决的冲突点 |
| `specVersions` | Business Spec 版本历史 |

**调用策略**：项目启动时加载，会话结束时回写。

---

## L3 — Knowledge Memory（知识层）⚡ 规划中

跨项目复用的业务分析知识库。

| 字段 | 内容 |
|------|------|
| `domainFrameworks` | BMAD 等业务分析框架 |
| `stakeholderPatterns` | 常见利益相关者类型及特征 |
| `painCatalog` | 跨行业痛点模式库 |
| `conflictPatterns` | 常见逻辑冲突模式 |
| `elicitationScripts` | 行业定制访谈脚本 |

**调用策略**：Context Priming 阶段自动检索，项目结束后按需补充。

---

## 记忆调用流程

```
Session 启动
    ↓
加载 L1 Session Memory（当前会话状态）
    ↓
Context Priming → 检索 L3 Knowledge（如有匹配领域）
    ↓
Elicitation Loop → 实时更新 L1
    ↓
Session 结束 → L1 持久化 + L2 更新（如有项目上下文）
    ↓
定期 → L3 补充（如发现新 Pain Pattern 或 Conflict Pattern）
```
