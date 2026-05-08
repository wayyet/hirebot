---
name: conflict-risk-flagging
description: 主动识别并标注"未能解决的矛盾"，输出 Flagged Issues 清单
compatibility: BRU 1.0
metadata:
  category: discovery
  autonomy: 100
  trigger: ongoing-throughout-loop
  input: conflicting-statements, data-quality-issues, responsibility-gaps
  output: flagged-issues-list
---

# Conflict Risk Flagging

## 目的

主动识别并明确标注"未能解决的矛盾"，而非掩盖。确保所有未解问题透明化，为后续决策提供依据。

## 触发条件

贯穿整个三元推演循环（Context → Elicitation → Pain-Demand → Logic → Spec）

## 输入

- 各方说法不一致处
- 数据质量存疑处
- 职责分歧处
- 假设未经确认处

## 输出

Flagged Issues 清单（每个问题标注：类型/涉及方/状态）

## 问题类型

| 类型 | 说明 |
|---|---|
| Data Quality Risk | 数据来源不清或存在冲突 |
| Political Risk | 涉及跨部门利益博弈 |
| Scope Ambiguity | 范围边界不清晰 |
| Decision Gap | 关键决策人未参与或未拍板 |

## 自主性

100% — 完全自主标记，不试图自行解决

## 执行指南

1. 在任意环节发现矛盾时立即标记
2. 标注问题类型和涉及方
3. 记录当前状态（open/resolved/accepted）
4. 不尝试自行解决或妥协
5. 汇总输出 Flagged Issues 清单

## 状态定义

- **open**：矛盾已识别，待进一步确认
- **resolved**：已提出方案并得到确认
- **accepted**：涉及政治因素或高层决策，已被接受为已知风险
