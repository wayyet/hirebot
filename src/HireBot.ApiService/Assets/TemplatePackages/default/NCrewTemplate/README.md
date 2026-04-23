# BRU — Business Requirements Uncoverer

> 数字员工包索引 | NCrew 框架

---

## 包结构

```
BRU/
├── README.md                         ← 本文件（索引）
├── agent.md                         ← 数字员工画像卡 + Day-1 Work Loop
├── ontology/                       ← Ontology 本体切片（权威版本）
│   └── ontology-slice.md
├── skills/                         ← Skill Package
│   ├── SKILL.md                    ← 顶层 Skill 入口
│   ├── context-priming/
│   │   └── SKILL.md
│   ├── elicitation-orchestration/
│   │   └── SKILL.md
│   ├── pain-demand-disambiguation/
│   │   └── SKILL.md
│   ├── logic-reconstruction/
│   │   └── SKILL.md
│   ├── conflict-risk-flagging/
│   │   └── SKILL.md
│   ├── spec-generation/
│   │   └── SKILL.md
│   └── bridge-to-forge/
│       └── SKILL.md
├── cli/                            ← CLI 接口定义
│   └── cli-reference.md
├── config/                         ← 身份/灵魂/记忆配置
│   ├── IDENTITY.md
│   ├── SouL.md
│   ├── AGENTS.md
│   └── MEMORY.md
├── references/                     ← Discovery 支撑文件
│   ├── index.md
│   ├── output-template.md
│   ├── conversation-flow.md
│   ├── ontology-slicing-guide.md
│   ├── experience-principles.md
│   ├── support-ticket-triage-demo.md
│   ├── sales-lead-routing-demo.md
│   └── expense-precheck-demo.md
└── doc/                            ← 发现过程文档
    ├── BRU-数字员工发现简报.md
    ├── Round0-Round1-术语对齐与问题抓取.md
    ├── Round2-数字员工画像.md
    ├── Round3-Day1工作循环.md
    ├── Round4-Ontology-Slice.md
    ├── Round5-Skills清单.md
    ├── Round6-CLI系统对接.md
    └── prd-forge对比分析.md
```

---

## 核心文件说明

| 文件 | 用途 |
|------|------|
| `agent.md` | 数字员工身份定义：名称、角色、使命、边界、人机分工、Day-1 Work Loop |
| `ontology/ontology-slice.md` | 该员工完成此场景所需的最小业务语境：Entities / Actions / Resources / Constraints |
| `skills/` | 该员工调用的每个 Skill 的完整 Skill Package |
| `cli/cli-reference.md` | 每个 Skill 所需系统接口的标准化描述 |
| `config/` | BRU 的身份声明、核心灵魂、记忆管理配置 |
| `references/` | Discovery 引导支撑文件（对话流程、模板、演示案例）|
| `doc/BRU-数字员工发现简报.md` | 完整发现简报（9模块，含完整案例）|

---

## NCrew 四层对照

| 层级 | BRU 对应内容 |
|------|-------------|
| **身份层** | `agent.md` — BRU 数字员工画像 |
| **上下文层** | `ontology/ontology-slice.md` — 业务分析本体切片 |
| **能力层** | `skills/` — 7 个 Skill |
| **工具层** | `cli/` — 5 个 CLI 接口 |

---

## 下游链路

```
BRU → Business Spec + Context Seed → [prd-forge] → PRD
```

---

## 使用说明

1. **初次了解 BRU**：从 `agent.md` 开始，理解 BRU 是谁、做什么
2. **理解业务语境**：阅读 `ontology/ontology-slice.md`，看它需要理解哪些业务对象
3. **理解能力单元**：查看 `skills/SKILL.md`，了解 7 个 Skill 如何协作
4. **理解系统对接**：查看 `cli/cli-reference.md`，了解需要哪些接口
5. **完整发现过程**：阅读 `doc/BRU-数字员工发现简报.md`
6. **运行 Discovery**：使用 `references/conversation-flow.md` 引导对话

---

## 相关文档

- [数字员工发现简报](./doc/BRU-数字员工发现简报.md)
- [Ontology Slice](./ontology/ontology-slice.md)
- [Skills 清单](./skills/SKILL.md)
- [CLI 参考](./cli/cli-reference.md)
- [Discovery 演示案例](./references/index.md)
