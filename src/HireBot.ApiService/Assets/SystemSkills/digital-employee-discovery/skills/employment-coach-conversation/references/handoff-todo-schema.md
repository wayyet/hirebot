# Todo 工单 notes schema

本文件定义 `employment-coach-conversation` 在雇佣流程里维护阶段工单时，必须写入 `todo.notes` 的机器可读 JSON 结构。

## 核心原则

- 雇佣流程里的阶段工单一律通过 `todo` 工具维护，不再通过回复文本里的 patch 回传。
- `todo.text` 只放简短的人类可读摘要，不参与流程判断。
- `todo.notes` 必须是严格 JSON，供 HireBot 后端直接读取。
- 会影响流程推进的结构化字段全部写在 `todo.notes` 中。
- `todo.complete` 仅在工单最终确认、或用户明确跳过外部系统时使用。
- 调用 `todo.complete` 时必须同时提交最新的 `todo.notes` JSON，把 `status` 写成 `confirmed`，并刷新 `updatedAtUtc`。
- `todo.remove` 用于用户主动撤销，不保留“dismissed 但仍显示”的额外文本记录。

## 工具动作

| 动作 | 何时使用 |
|---|---|
| `add` | 新建阶段工单 |
| `update` | 补全信息、修改字段、切换状态 |
| `complete` | 最终确认、或确认 `payloadJson.kind = skip` 的跳过工单 |
| `remove` | 用户撤销该工单 |
| `clear` | 仅在整轮重置时使用，普通推进不要调用 |

## `todo.notes` 字段

```json
{
  "stage": "material",
  "targetSkill": "ontology_extraction",
  "intent": "整理客服退货流程资料",
  "category": "流程 SOP",
  "status": "ready_to_dispatch",
  "source": "用户上传的客服退货流程资料",
  "acceptance": "能够抽出退货流程节点",
  "payloadJson": "{\"objective\":\"抽出退货流程节点\"}",
  "createdAtUtc": "2026-05-06T10:00:00Z",
  "updatedAtUtc": "2026-05-06T10:05:00Z"
}
```

字段要求：

- `stage`: `material` / `skill` / `external`
- `targetSkill`: `ontology_extraction` / `skill_generation` / `external_config`
- `intent`: 一句话说明这条工单要解决什么
- `category`: 阶段内的业务分类
- `status`: `drafting` / `ready_to_dispatch` / `dispatched` / `dirty` / `confirmed` / `needs_review`
- `source`: 这条工单来自哪段对话、哪份资料或哪次修改
- `acceptance`: 这条工单完成时应满足什么
- `payloadJson`: 阶段相关结构化字段，值本身仍然是 JSON 字符串；没有附加字段时可写 `null`
- `createdAtUtc`: 第一次建立该工单的 UTC 时间
- `updatedAtUtc`: 最近一次更新该工单的 UTC 时间

## 状态机

| 状态 | 含义 |
|---|---|
| `drafting` | 还在引导中，明确度不够 |
| `ready_to_dispatch` | 明确度已达标，可以交给下游 |
| `dispatched` | 已发起下游处理，等待回传 |
| `dirty` | 下游处理中又被用户改动，需要重新派发 |
| `confirmed` | 已有结果并被确认 |
| `needs_review` | 因配置治理或边界变化需要复核 |

## 阶段字段补充

### 阶段 1：material

- `targetSkill = ontology_extraction`
- `payloadJson` 至少应包含 `objective`
- 推荐补充 `source_files`、`scene_hint`、`mode`

### 阶段 2：skill

- `targetSkill = skill_generation`
- `payloadJson` 至少应包含 `skill_name`、`skill_description`、`trigger`、`expected_output`

### 阶段 3：external

- `targetSkill = external_config`
- `payloadJson` 至少应包含 `category`、`objective`、`target_system`
- 用户明确跳过外部系统时，写入 `{"kind":"skip"}`，把 `status` 写成 `confirmed`，然后使用 `todo.complete`

## 凭据红线

- token / 密钥 / 密码 / API Key 等真实凭据绝不写入 `todo.text`
- 真实凭据也绝不写入 `todo.notes`
- `payloadJson` 只描述凭据形式，例如 `OAuth`、`Bearer Token`、`长期 Key`
