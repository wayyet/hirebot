---
name: bridge-to-forge
description: BRU 与 prd-forge 之间的"交接棒"，传递业务底线指标和约束条件，防止过度设计
compatibility: BRU 1.0
metadata:
  category: output
  autonomy: 100
  trigger: business-spec-complete
  input: pain-demand-pairs, flagged-issues, baseline-metrics, constraints
  output: context-seed-for-prd-forge
---

# Bridge to Forge

## 目的

BRU → prd-forge 的"交接棒"，防止过度设计和强行 AI 化。把"业务方能接受的底线"和"绝对不能踩的坑"显式传递下去。

## 触发条件

Business Spec 生成完毕后

## 输入

- Pain-Demand Pairs（真实痛点）
- Flagged Issues（未解决问题）
- 底线指标（必须达成的业务目标）
- 约束条件（技术/资源/政策限制）

## 输出

"Context Seed" for prd-forge — 一段结构化的上下文摘要

## Context Seed 结构

1. **业务底线** — 不可妥协的核心指标
2. **高风险区** — Flagged Issues 中的 Political/Scope 风险
3. **数据红线** — 数据质量存疑、不可依赖的数据源
4. **决策链** — 关键决策人及其立场摘要
5. **推荐路径** — 基于 Pain-Demand 的优先级建议

## 核心价值

- 防止 prd-forge 在不了解业务底线的情况下生成不切实际的 PRD
- 确保高风险问题在进入设计阶段前被显式传递
- 保护业务方的真实需求不被过度工程化

## 自主性

100%
