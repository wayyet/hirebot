---
name: logic-reconstruction
description: 将碎片化、描述不一致的信息还原为清晰的 As-Is vs To-Be 流程，输出流程图和 Gap 分析
compatibility: BRU 1.0
metadata:
  category: discovery
  autonomy: 90
  trigger: pain-demand-resolved, logic-breakpoint-detected
  input: interview-records, conflict-markers, data-quality-annotations
  output: as-is-flowchart, to-be-flowchart, gap-analysis
---

# Logic Reconstruction

## 目的

把碎片化、描述不一致的信息还原为清晰的 As-Is vs To-Be 流程。识别逻辑断点（数据流断点、业务规则冲突），为 prd-forge 提供原材料。

## 触发条件

- Pain-Demand 解耦完成
- 识别出逻辑断点

## 输入

- 访谈记录
- 冲突点标记
- 数据质量标注

## 输出

- As-Is 流程图
- To-Be 流程图
- Gap 分析（聚焦数据流断点和业务规则冲突）

## 自主性

90% — 自动还原，但数据真实性依赖人的确认

## 执行指南

1. 提取所有业务动作和决策点
2. 识别跨 Stakeholder 的数据流
3. 标注冲突点和数据质量存疑处
4. 生成 As-Is 流程图（现状）
5. 生成 To-Be 流程图（目标状态）
6. 输出 Gap 分析（待补充/待确认/存疑）

## Gap 类型

- Data Flow Gap（数据在何处分叉或中断）
- Rule Conflict（不同部门对同一规则理解不一致）
- Assumption（未经确认的假设）
