# emit_artifact 协议

本文件描述 `employment-coach-conversation` skill 在各阶段调用 `emit_artifact` 工具的时机、字段规范和前端行为。

## 工具调用格式

```json
{
  "name": "emit_artifact",
  "parameters": {
    "kind": "data",
    "artifactType": "<见下方表格>",
    "label": "<对用户可读的一句话进度描述>",
    "skillName": "employment-coach-conversation",
    "stage": "<stage1_material | stage2_skill | stage3_external>",
    "isTerminal": false,
    "displayHint": "<progress | tree>",
    "data": { "<见 stage-data-schema.md>" }
  }
}
```

## 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `kind` | `"data"` | 固定值，表示结构化数据产物（非文件） |
| `artifactType` | string | 与 `contracts/artifacts.json` 中的 `type` 字段对应 |
| `label` | string | 前端胶囊显示的进度文本，用业务语言描述当前状态 |
| `skillName` | string | 固定为 `employment-coach-conversation` |
| `stage` | string | 当前阶段标识，决定前端哪个胶囊更新 |
| `isTerminal` | bool | `false` = 进度更新（胶囊置为 running）；`true` = 阶段完成（胶囊置为 completed） |
| `displayHint` | string | 前端渲染提示：`progress` 用于进度条 / 列表，`tree` 用于最终树状摘要 |
| `data` | object | 阶段产物的结构化内容，详见 stage-data-schema.md |

## 前端行为

前端监听 WebSocket `type: 'artifact'` 消息：
- `isTerminal: false` → 将对应阶段胶囊置为 `running`（仅在尚未 completed 时生效）
- `isTerminal: true` → 将对应阶段胶囊置为 `completed`

stage 与前端胶囊的对应关系：
- `stage1_material` → 资料收集胶囊（skillName 含 'material' 时触发）
- `stage2_skill` → 技能配置胶囊（skillName 含 'skill' 时触发）
- `stage3_external` → 外部能力胶囊（skillName 含 'external' 时触发）

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
| 用户确认技能清单完整 | `skill_workorder_summary` | `true` | `tree` |

### 阶段 3：外部（stage3_external）

| 时机 | artifactType | isTerminal | displayHint |
|------|-------------|------------|-------------|
| 外部阶段开始，收到第一条能力描述 | `external_workorder_progress` | `false` | `progress` |
| 用户确认外部能力清单（或明确跳过） | `external_workorder_summary` | `true` | `tree` |

## 调用约束

- **调用优先于对话输出**：同一轮次识别到可推送的阶段事件时，先调用 `emit_artifact`，再给用户一句简短的业务反馈
- **不暴露字段值**：不在对话中展示 `artifactType`、`stage`、`isTerminal`、`data` 的原始 JSON 内容
- **凭据禁入 data**：`data` 字段中绝不写入 token / 密钥 / 密码 / API Key / 连接串；凭据形式（OAuth / Bearer / 长期 Key）可以写，凭据值不能写
- **label 必须是业务语言**：例如"已记录 3 份业务资料，等待你确认"，而不是技术字段名
