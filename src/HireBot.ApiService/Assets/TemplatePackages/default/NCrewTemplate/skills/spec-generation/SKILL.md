---
name: spec-generation
description: 将采集和推演结果输出为结构化业务规格书（5模块），输出可落地的规格文档
compatibility: BRU 1.0
metadata:
  category: output
  autonomy: 100
  trigger: logic-entropy-resolved
  input: context, pain-demand-pairs, as-is-to-be, flagged-issues
  output: structured-business-spec
---

# Spec Generation

## 目的

将所有采集和推演结果，输出为一份可落地的结构化业务规格书。规格书需闭环、无歧义、冲突已标记。

## 触发条件

"逻辑熵减"完成（闭环+无歧义+冲突已标记）

## 输入

- Context（背景上下文）
- Pain-Demand Pairs（痛点-需求对照表）
- As-Is / To-Be 流程图
- Flagged Issues 清单

## 输出

结构化业务规格书（5模块）

## 规格书结构

1. **业务背景** — Context Priming 结果摘要
2. **核心痛点** — Pain-Demand Pairs 汇总
3. **目标流程** — To-Be 流程图 + Gap 分析
4. **未解问题** — Flagged Issues 清单
5. **约束条件** — 技术/资源/政策约束

## 自主性

100% — 自动生成，人仅需一键确认或微调

## 执行指南

1. 汇总所有已采集信息
2. 按 5 模块结构组织
3. 生成规格书草稿
4. 自检：无歧义、无逻辑漏洞、Flagged Issues 已标注
5. 呈现给人确认
