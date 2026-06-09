# emit_artifact 协议

本文件描述 `employment-coach-conversation` skill 在各阶段调用 `emit_artifact` 工具的时机、字段规范和前端行为。

## 工具调用格式

```json
{
  "name": "emit_artifact",
  "parameters": {
    "kind": "<data | file>",
    "artifactType": "<见下方表格>",
    "label": "<对用户可读的一句话进度描述>",
    "skillName": "employment-coach-conversation",
    "stage": "<stage1_material | stage2_skill | stage3_external | stage4_packaging | ontology-projection>",
    "isTerminal": false,
    "displayHint": "<progress | tree | badge | file>",
    "data": { "<见 stage-data-schema.md>" },
    "fileUrl": "<kind=file 时必填>",
    "fileName": "<kind=file 时建议填写>"
  }
}
```

## 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `kind` | `"data" \| "file"` | `data` 表示结构化数据产物；`file` 表示文件产物（如实例包） |
| `artifactType` | string | 与 `contracts/artifacts.json` 中的 `type` 字段对应 |
| `label` | string | 前端胶囊显示的进度文本，用业务语言描述当前状态 |
| `skillName` | string | 固定为 `employment-coach-conversation` |
| `stage` | string | 当前阶段标识，决定前端哪个胶囊更新 |
| `isTerminal` | bool | `false` = 进度更新（胶囊置为 running）；`true` = 当前 artifact 所属步骤收口。对阶段 1/3 可直接视为阶段完成；对阶段 2 的 `skill_workorder_summary` 仅表示“技能定义”子步骤完成 |
| `displayHint` | string | 前端渲染提示：`progress` / `tree` / `badge` / `file` |
| `data` | object | 阶段产物的结构化内容（`kind=data` 时使用），详见 stage-data-schema.md |
| `fileUrl` | string | `kind=file` 时必填，填写打包工具真实返回值 |
| `fileName` | string | `kind=file` 时建议填写，通常为 `<template_slug>-artifacts.zip` |

## 前端行为

前端监听 WebSocket `type: 'artifact'` 消息：
- `isTerminal: false` → 将对应阶段胶囊置为 `running`（仅在尚未 completed 时生效）
- `isTerminal: true` → 默认将对应阶段胶囊置为 `completed`
- `stage2_skill` 是特例：`skill_workorder_summary` 只表示技能定义子步骤收口，主技能阶段要等 `skill_generation_done` 后才置为 `completed`

stage 与前端胶囊的对应关系：
- `stage1_material` → 资料收集胶囊
- `stage2_skill` → 技能配置胶囊
- `stage3_external` → 外部能力胶囊
- `stage4_packaging` → 实例打包胶囊
- `ontology-projection` → 本体 projection 子轨道（下游执行态）

补充说明：
- `stage2_skill` 是“技能”主阶段，其中固定先完成技能定义，再进入技能生成确认/执行子步骤。
- `skill_workorder_summary` 发出后，必须额外发出 `skill_generation_ready`；该 artifact 只驱动下游 `skill-generation` 轨为 `waiting_confirm`。前端应保留“技能定义已确认”子状态，但主 `stage2_skill` 胶囊在 `skill-generation` 完成前仍保持进行中。

## 各阶段发出时机

### 阶段 1：资料（stage1_material）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 用户上传文件或描述资料后，第一条资料被记录下来 | `material_collection_progress` | `false` | `progress` |
| 用户明确表达"先这些"，资料清单已整理完毕 | `material_handoff_summary` | `true` | `tree` |

中间每次有新资料加入清单时可多次发出 `material_collection_progress` 更新进度，不需要等到用户说完所有资料再发第一次。

### 阶段 2：技能（stage2_skill）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 技能阶段开始，收到第一条技能描述 | `skill_workorder_progress` | `false` | `progress` |
| 用户确认技能清单完整，技能定义子步骤收口 | `skill_workorder_summary` | `true` | `tree` |
| 技能定义已确认，等待用户确认是否开始技能生成 | `skill_generation_ready` | `false` | `badge` |

补充约束：
- `skill_workorder_summary` 只表示“技能定义”子步骤完成，不代表可以直接进入阶段 3。
- `skill_workorder_summary.data` 必须包含会话初始化阶段锁定的真实 `workspace_root` 与 `template_slug`（缺一不可），供后续 projection pass 与 skill-generation 启动使用；缺少任一字段时不得发出 terminal summary。
- 只有 `skill-generation` 已完成且用户明确同意继续时，才允许发出 `external_workorder_progress`。

### Projection Pass 子轨（ontology-projection）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 用户确认开始技能生成后，projection pass 启动 | `ontology_projection_progress` | `false` | `progress` |
| projection pass 完成，可触发 skill-generation | `ontology_projection_done` | `true` | `tree` |

补充约束：
- 这两个 artifact 由下游 `ontology-extraction` 产出，`stage` 使用 `ontology-projection`。
- `ontology_projection_done` 到达前，不得触发 `skill-generation`。

### 阶段 3：外部（stage3_external）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 外部阶段开始，收到第一条能力描述 | `external_workorder_progress` | `false` | `progress` |
| 用户确认外部能力清单（或明确跳过） | `external_workorder_summary` | `true` | `tree` |

进入前提：
- 只有在 `skill_generation_done` 已到达后，才允许真正进入外部阶段。

### 阶段 4：实例打包（stage4_packaging）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 外部配置已保存或跳过，等待用户确认是否生成评估测试用例 | `packaging_testcases_ready` | `false` | `badge` |
| 用户确认生成后，测试用例生成中 | `packaging_testcases_progress` | `false` | `progress` |
| 测试用例已生成并回写工作区 | `packaging_testcases_done` | `true` | `tree` |
| Manifest 同步完成，等待用户确认是否进行完整性审查 | `review_readiness` | `false` | `badge` |
| 用户确认审查后，审查脚本执行中 | `review_progress` | `false` | `progress` |
| 审查报告完成（含 P0/P1/P2 摘要与修复建议） | `review_report` | `true` | `tree` |
| 打包请求已收到，等待下游或正在打包 | `packaging_progress` | `false` | `progress` |
| 打包工具成功返回 fileUrl，实例包可导入 | `template_package` | `true` | `file` |

补充约束：
- `template_package` 必须使用 `kind: "file"`，且 `fileUrl` 必须是打包工具真实返回值。
- `packaging_progress.data.status` 仅允许 `waiting_downstream` / `packing`。
- `review_readiness` 在 Manifest 同步完成后发出，用户可选择审查或跳过。
- `review_report.data` 必须包含 `status`（PASS / PASS_WITH_CONCERNS / FAIL）、`release_readiness`、`p0_blockers`、`p1_warnings`、`score_average`、`summary` 字段。
- 审查结果不影响打包执行权：即使 `review_report.data.status == "FAIL"`，用户仍可选择强制继续打包。

## 调用约束

- **调用优先于对话输出**：同一轮次识别到可推送的阶段事件时，先调用 `emit_artifact`，再给用户一句简短的业务反馈
- **data 字段必须严格遵循 schema**：`data` 内容必须完全符合 [stage-data-schema.md](stage-data-schema.md) 中对应 `artifactType` 的示例结构；不得添加任何 schema 中未列出的字段（如 `capabilities`、`materials`、`scene_hint` 等）
- **顶层 status 语义**：仅打包相关 artifact（`packaging_progress`、`packaging_testcases_progress`、`packaging_testcases_done`）允许 `data.status`；其他 artifact 禁止顶层 `data.status`
- **禁止旧 dispatch 字段**：`data` 中绝不出现 `status: "ready_to_dispatch"`、`dispatch_payload`、`handoff_todos` 等旧状态机字段；阶段完成用 `isTerminal: true` 表达
- **不暴露字段值**：不在对话中展示 `artifactType`、`stage`、`isTerminal`、`data` 的原始 JSON 内容
- **凭据禁入 data**：`data` 字段中绝不写入 token / 密钥 / 密码 / API Key / 连接串；凭据形式（OAuth / Bearer / 长期 Key）可以写，凭据值不能写
- **label 必须是业务语言**：例如"已记录 3 份业务资料，等待你确认"，而不是技术字段名
- **对话文案禁用旧词**：对话回复中禁止出现"dispatch 闭环"、"dispatch 信号"、"handoff 工单"、"ready_to_dispatch"、"dispatch 给下游"等词语
