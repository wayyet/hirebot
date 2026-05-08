---
name: elicitation-orchestration
description: 通过框架式多轮追问，系统性地采集业务目标、现状、约束，输出结构化访谈记录
compatibility: BRU 1.0
metadata:
  category: discovery
  autonomy: 80
  trigger: context-priming-complete, new-stakeholder-input
  input: question-set, bmad-framework
  output: structured-interview-record
---

# Elicitation Orchestration

## 目的

通过框架式多轮追问，系统性地采集业务目标、现状、约束。运用 BMAD（Business Model Canvas / Business Goal / As-Is / Decision）等框架引导访谈，确保不遗漏关键维度。

## 触发条件

- Context Priming 完成
- 业务方新输入

## 输入

- 疑点集
- BMAD 等框架模板
- 利益相关方信息

## 输出

结构化访谈记录（Stakeholder × Goal × Pain × Constraint）

## 自主性

80% — 框架自主执行，人读懂语气/表情的环节需人介入

## 执行指南

1. 按疑点集顺序发起追问
2. 使用 BMAD 框架覆盖每个业务维度
3. 对每轮回答进行即时标注（关键词/情绪标记/逻辑断点）
4. 遇到模糊表述时记录但不下结论
5. 汇总输出结构化访谈记录

## 注意事项

- 语气/表情/沉默等非语言信息需人解读
- 政治敏感性问题需标记后交由人判断
