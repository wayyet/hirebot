---
name: pain-demand-disambiguation
description: 识别并剥离"技术解法"背后的"真实业务痛点"，输出 Pain-Demand Pair 对照表
compatibility: BRU 1.0
metadata:
  category: discovery
  autonomy: 90
  trigger: expressed-need-detected
  input: raw-stakeholder-statement, interview-context
  output: pain-demand-pair-table
---

# Pain-Demand Disambiguation

## 目的

识别并剥离"技术解法"背后的"真实业务痛点"。业务方常以解决方案表述需求（如"要一个大屏"），本技能将其还原为真实痛点，避免过度设计。

## 触发条件

Elicitation 采集到 Expressed Need（如"要一个大屏"）

## 输入

- 业务方原话
- 访谈上下文
- 已有的疑点集

## 输出

Pain-Demand Pair（真实痛点 vs 表达需求 的对照表）

## 核心问题

"如果不解决 X，大屏还有价值吗？"

## 自主性

90% — 能识别，但最终判断依赖人的确认

## 执行指南

1. 识别 Expressed Need（技术解法类表述）
2. 追问"如果不解决 X，后果是什么"
3. 提取真实 Pain（业务痛点）
4. 生成 Pain-Demand Pair 对照表
5. 呈请人确认 Pain 识别是否准确

## 示例

| 表达需求 | 真实痛点 |
|---|---|
| 要一个大屏 | 管理层无法实时看到业务数据，决策滞后 |
| 要自动化报表 | 人工汇总数据耗时易错，每周占用 2 人天 |
