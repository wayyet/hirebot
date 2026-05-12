# 阶段产物 data 字段结构

本文件定义 `emit_artifact` 调用中各 `artifactType` 的 `data` 字段结构，供 `employment-coach-conversation` skill 构造 artifact payload 时参考。

---

## ⛔ 禁止字段与禁止模式

以下内容**绝对不能**出现在任何 `data` 字段中：

| 禁止的字段名 / 值 | 来源说明 | 应改用 |
|-----------------|---------|-------|
| 顶层 `status` 字段 | 旧 handoff 状态机残留 | 不需要顶层 status；各 item 内部有自己的 `status: pending/ready` |
| `status: "ready_to_dispatch"` | 旧 dispatch 协议 | 用 `isTerminal: true` 表示阶段完成 |
| `status: "dispatched"` / `"confirmed"` / `"needs_review"` / `"dirty"` | 旧 handoff 状态机 | 同上 |
| `capabilities` 字段 | 旧格式 | 改用 `items[]` |
| `materials` 字段（顶层） | 旧格式 | 改用 `items[]` |
| `scene_hint` 字段 | 旧格式 | 不需要，schema 中无此字段 |
| `dispatch_payload` / `handoff_todos` / `dispatch_target` | 旧 dispatch 协议 | 全部删除 |

**data 字段的合法顶层 key 只有下方各 artifactType 示例中明确列出的字段。任何不在示例中的 key 均视为错误。**

**对话回复中同样禁止出现以下词语**：`dispatch 闭环`、`dispatch 信号`、`handoff 工单`、`ready_to_dispatch`、`dispatch 给下游`。

---

## 阶段 1：资料（stage1_material）

### material_collection_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 2,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件与处置路径",
      "status": "pending"
    },
    {
      "title": "客服话术风格",
      "source_hint": "用户描述",
      "category": "话术风格",
      "objective": "抽取标准化服务语言特征",
      "status": "pending"
    }
  ],
  "notes": "用户还在补充，尚未确认"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `collected_count` | 是 | 当前已收集的资料条目数 |
| `items[]` | 是 | 已整理的资料清单 |
| `items[].title` | 是 | 资料标题，对用户可读 |
| `items[].source_hint` | 是 | 来源描述（上传文件名或描述来源） |
| `items[].category` | 是 | 资料分类：决策规则 / 话术风格 / 业务流程 / 数据字段 / 其他 |
| `items[].objective` | 是 | 本条资料要抽取的目标 |
| `items[].status` | 是 | `pending`（待处理）/ `ready`（已就绪） |
| `notes` | 否 | 补充说明 |

---

### material_handoff_summary（阶段完成，isTerminal: true）

```json
{
  "total_items": 3,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件、处置档位和人工分流触发节点",
      "status": "ready"
    }
  ],
  "summary": "共整理 3 份业务资料，已确认抽取方向，准备进入技能定义阶段"
}
```

字段说明：与 `material_collection_progress` 相同，`status` 应全部为 `ready`，并补充 `summary` 字段。

---

## 阶段 2：技能（stage2_skill）

### skill_workorder_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 2,
  "items": [
    {
      "name": "refund-eligibility-check",
      "display_name": "退货资格初判",
      "description": "在用户提出退货请求时，根据订单状态、商品类型和时限判断是否符合退货条件",
      "trigger": "用户消息中出现退货 / 退款等关键词且能匹配到订单",
      "generation_action": "generate_new",
      "status": "pending"
    },
    {
      "name": "order-status-query",
      "display_name": "订单状态查询",
      "description": "根据订单号查询状态、物流进度和基础异常",
      "trigger": "用户询问订单状态 / 物流进度",
      "generation_action": "reuse_existing",
      "status": "ready"
    }
  ],
  "notes": "待用户确认是否还有其他技能"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `collected_count` | 是 | 当前已整理的 skill 数量 |
| `items[]` | 是 | skill 清单 |
| `items[].name` | 是 | skill slug（英文，下划线） |
| `items[].display_name` | 是 | 对用户可读的技能名称 |
| `items[].description` | 是 | 技能能力描述 |
| `items[].trigger` | 是 | 触发条件 |
| `items[].generation_action` | 是 | `generate_new`（新生成）/ `reuse_existing`（复用已有） |
| `items[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明 |

---

### skill_workorder_summary（阶段完成，isTerminal: true）

```json
{
  "total_items": 4,
  "new_count": 2,
  "reuse_count": 2,
  "items": [ "... 同 progress items ..." ],
  "summary": "共规划 4 个技能：2 个新生成、2 个复用模板默认能力，技能阶段已确认"
}
```

---

## 阶段 3：外部（stage3_external）

### external_workorder_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 1,
  "items": [
    {
      "name": "crm-order-read",
      "display_name": "CRM 订单查询",
      "category": "read",
      "objective": "根据订单号读取订单状态和物流信息",
      "target_system": "CRM 系统",
      "auth_kind": "bearer_token",
      "linked_skills": ["order-status-query"],
      "status": "pending"
    }
  ],
  "notes": "凭据由用户在右侧表单填写"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `collected_count` | 是 | 当前已整理的外部能力数量 |
| `items[]` | 是 | 外部能力清单 |
| `items[].name` | 是 | 外部能力 slug |
| `items[].display_name` | 是 | 对用户可读的名称 |
| `items[].category` | 是 | `read` / `write` / `notify` / `search` / `transform` / `skip` |
| `items[].objective` | 是 | 调用目的 |
| `items[].target_system` | 是 | 目标系统名称 |
| `items[].auth_kind` | 是 | `none` / `oauth2` / `bearer_token` / `api_key` / `basic` |
| `items[].linked_skills` | 是 | 关联的 skill name 列表（非空） |
| `items[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明（不得包含凭据值） |

---

### external_workorder_summary（阶段完成，isTerminal: true）

```json
{
  "total_items": 2,
  "skip": false,
  "items": [ "... 同 progress items ..." ],
  "summary": "共规划 2 项外部能力接入，凭据配置待表单填写，外部阶段已确认"
}
```

如果用户明确表示不需要外部系统：

```json
{
  "total_items": 0,
  "skip": true,
  "items": [],
  "summary": "用户明确声明不需要外部系统接入，外部阶段已跳过"
}
```

---

## 通用约束

- **data 中禁止写入凭据值**：token / 密钥 / 密码 / API Key / 连接串一律不得出现在 `data` 字段中
- **凭据形式可以描述**：`auth_kind` 描述鉴权方式（如 `oauth2`、`bearer_token`），不写具体凭据值
- **status 字段**：进度 artifact 中允许混合 `pending` / `ready`；terminal artifact 中应全部为 `ready`
- **summary 字段**：terminal artifact 必须包含对用户可读的 `summary`，进度 artifact 可选
