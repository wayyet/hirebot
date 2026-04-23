---
name: context-priming
description: 在业务方追问之前加载背景知识，降低"鸡同鸭讲"风险，生成初步疑点集和利益相关方画像
compatibility: BRU 1.0
metadata:
  category: discovery
  autonomy: 100
  trigger: raw-input
  input: business-domain-keywords, document-fragments
  output: initial-question-set, stakeholder-profile
---

# Context Priming

## 目的

在追问业务方之前，先加载背景知识，降低"鸡同鸭讲"的风险。通过预读业务领域关键词和相关文档片段，快速建立对该业务领域的基础理解。

## 触发条件

收到 Raw Input（聊天记录/录音/一句话需求）

## 输入

- 业务领域关键词
- 相关文档片段
- 历史访谈记录（若有）

## 输出

- 初步疑点集（5-10个关键问题）
- Stakeholder 初步画像（角色/部门/可能的决策影响力）

## 自主性

100% — 自主完成，人只需确认领域范围

## 执行指南

1. 解析输入材料，提取业务领域关键词
2. 检索相关背景知识或要求人提供参考文档
3. 生成初步疑点集
4. 基于输入推断 Stakeholder 画像
5. 呈现给业务方确认领域范围是否正确
