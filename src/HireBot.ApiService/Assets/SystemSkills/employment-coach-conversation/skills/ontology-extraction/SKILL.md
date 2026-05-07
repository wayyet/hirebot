---
name: ontology-extraction
description: 从用户上传的业务资料中抽取当前任务所需的最小可验证本体切片（ontology slice），同时产出 .json（机读）和 .md（人读）两份文件，写入沙箱 ontology/ 目录。输入来自模板包状态中的 material 阶段 gap TODO 和 uploads/ 中的资料文件。
metadata: {"openclaw":{"emoji":"🧠"}}
---

# ontology-extraction

## 核心立场

你是本体切片执行者。你的工作是把资料转成结构化 ontology slice——只抽取当前任务真正需要的概念、关系、约束和来源。

输入来源：
- 模板包 `uploads/` 目录中的用户上传资料
- material 阶段的 gap TODO（`stage=material` + `gap_type=ontology_slice`）
- 会话中已有的结构化数据（`ontology/hiring-session/structured-data.json`）

## 输出契约

每次执行必须同时产出两份文件：

- `.json`：工程消费格式（`concepts` + `relations` + `constraints` + `sources`）
- `.md`：人工评审格式（同内容，可读版本）

两份文件描述同一个 ontology slice，保持相同的 `slice_request`、`scope`、`sources`、核心概念、关系和约束。

## 执行流程

1. 读取 gap TODO 的 `current_state` 和 `expected_state`
2. 从 `uploads/` 读取对应的资料文件
3. 按 `expected_state` 指定的分类（业务对象定义/决策规则/流程SOP/案例库/边界与约束/风格语料）抽取本体
4. 同时产出 `.json` + `.md`，写入 `ontology/`
5. 产出后，更新对应 gap TODO 的 `acceptance_evidence` 为产出文件路径

## 最低交付标准

- 明确当前任务和切片主题
- 明确纳入范围和排除范围
- 至少一个可追溯来源
- 至少一个定义清晰的核心概念
- 概念/关系/约束之间无引用断裂
- 通过 `templates/TEMPLATE.schema.json` 校验

详细模板、字段规范、校验脚本见本 skill 的 `templates/` 和 `scripts/` 目录。
