# Handoff Contract

本文定义 `employment-coach-conversation` 阶段三 external handoff todo 到 `external_config` 的输入、字段映射与回传合约。

## 输入过滤

`external_config` 只处理满足以下条件的 todo：

```yaml
stage: external
target_skill: external_config
status: ready_to_dispatch | dirty
```

不满足条件的 todo 不应落盘；应在 `todo_results` 中标为 `skipped` 或 `failed`，并说明原因。

## 字段映射

| handoff 字段 | external_config 产物字段 | 要求 |
| --- | --- | --- |
| `id` | `todoId` / capability 文件名 | 必须保留，作为主关联键 |
| `intent` | `summary.intent` | 面向用户的一句话目标 |
| `category` | `category` | `read/write/notify/search/transform` 之一 |
| `payload.objective` | `objective` | 普通能力必填 |
| `payload.target_system` | `targetSystem.name` | 普通能力必填 |
| `payload.linked_skills` | `linkedSkills` | 普通能力必填，必须指向已确认 skill todo |
| `payload.auth_kind` | `auth.kind` | 只记录凭据形式，不记录值 |
| `payload.required_fields` | `fields.required` | 可为空，但字段存在时必须保留原始语义 |
| `payload.kind` | `kind` | `normal` 或 `skip` |
| `source` | `sourceDigest` | 不得包含凭据值 |
| `acceptance` | `acceptance` | 作为配置校验目标 |

## 普通能力最小输入

`kind: normal` 或未显式声明 kind 时，至少需要：

```yaml
category: read | write | notify | search | transform
payload:
  objective: <non-empty>
  target_system: <non-empty>
  linked_skills: [<confirmed skill todo id>]
  auth_kind: <OAuth | Bearer Token | API Key | 应用凭据 | 内部 token | none>
```

`required_fields` 可以为空，但如果目标是 `read/search/write`，建议保留用户已经说出的关键字段，方便后续补接口映射。

`auth_kind` 缺失时不要猜测。处理口径：

- 如果目标系统明确不需要认证，归一为 `none`。
- 如果目标系统大概率需要认证但上游未给出形式，标为 `partial`，在 `todo_results[].credential_slots` 中提示需要补认证形式。
- 只有系统层明确允许临时草案时，才可在 artifact 中写 `auth.kind: "unknown"`，并必须给出 warning。

## Skip 输入

用户明确表示“不需要外部系统”时，上游会给出 skip todo：

```yaml
stage: external
target_skill: external_config
payload:
  kind: skip
```

skip todo 仍需写入 `external/`，否则诊断无法区分“缺失外部配置”和“用户明确跳过”。

标准落点：

- `external/capabilities/<todo-id>.json`：记录 skip artifact。
- `external/external-config.index.json` 的 `skips[]`：登记 `todoId`、`reason`、`path`、`status`。

skip 产物至少包含：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_capability",
  "kind": "skip",
  "todoId": "e_external_skip_001",
  "reason": "用户明确表示不需要外部系统",
  "status": "recorded"
}
```

## Callback

回传结构必须能被主 skill 推送回 `employment-coach-conversation`：

```yaml
dispatch_callback:
  source_dispatch_target: external_config
  todo_ids: [e_xiaoshouyi_read_order_001]
  user_summary: 已生成销售易 CRM 的订单读取配置初稿，凭据需要在右侧表单补齐。
  artifacts:
    - path: external/capabilities/e_xiaoshouyi_read_order_001.json
      kind: external_config
    - path: external/external-config.index.json
      kind: external_index
  todo_results:
    - todo_id: e_xiaoshouyi_read_order_001
      status: success | partial | failed | skipped
      artifacts:
        - path: external/capabilities/e_xiaoshouyi_read_order_001.json
          kind: external_config
      credential_slots:
        - credential_slot: xiaoshouyi-crm-api-key
          secret_ref: EXTERNAL_XIAOSHOUYI_CRM_API_KEY
          binding_status: bound | pending | not_required
      errors: []
  status: success | partial | failed
  errors: []
```

`status` 规则：

- `success`：本次 todo 全部成功落盘，且没有安全阻断。
- `partial`：部分 todo 成功，部分失败；或配置已落盘但存在需要补齐的非阻断警告。
- `failed`：本次 todo 全部失败，或发现必须阻断的凭据泄露。

`errors` 不得复述任何疑似凭据值。

`todo_results` 是批量 dispatch 的逐条结果；主 skill 可用它判断哪些 todo 已成功产出 artifact、哪些需要重发或补凭据。`todo_results[].errors` 同样不得复述任何疑似凭据值。
