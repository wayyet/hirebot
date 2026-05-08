# Handoff tool 结构化交接合约

本文件定义 `employment-coach-conversation` 维护下游交接 todo 的唯一入口。所有要交给 `ontology-extraction`、`skill-generation` 或 `external-config` 的事项，都必须通过 Gateway 内置 Handoff tool 中由 `OpenClaw:Handoff` 配置声明的 `employment-coach` workflow 维护为当前会话 session 下的结构化 Handoff todo。

Handoff todo 是“交给谁、带什么输入、做到哪一步”的工作单元。它可以投影给 UI，但 canonical 状态、`session_id` 和 payload 以 Handoff tool 返回的数据为准。

## 目录

- [工具面](#工具面)
- [通用结构](#通用结构)
- [状态机](#状态机)
- [ID 与字段规范](#id-与字段规范)
- [阶段 1：material](#阶段-1material)
- [阶段 2：skill](#阶段-2skill)
- [阶段 3：external](#阶段-3external)
- [写入红线](#写入红线)

## 工具面

| action | 用途 | 关键输入 | 关键输出 |
| --- | --- | --- | --- |
| `upsert` | 新建或按 `fingerprint` 更新同一条 Handoff todo | `title`、`stage`、`target_skill`、`payload`、`fingerprint` | `session_id`、`handoff_id`、`revision`、完整 item |
| `patch` | 修改字段、payload 或来源摘要 | `handoff_id`、`patch`、可选 `expected_revision` | 更新后的 item |
| `transition` | 执行状态流转 | `handoff_id`、`status`、可选 `dispatch_id` / `callback_summary` | 更新后的 item |
| `list` | 读取当前 session 的结构化清单 | 可选 `stage`、`kind`、`target_skill`、`status`、`fingerprint` | Handoff todo 列表 |
| `remove` | 用户撤销且 UI 不需要继续展示时移除投影 | `handoff_id`、`reason` | 移除结果 |

参数 schema 建议保持扁平，方便模型稳定调用：

```json
{
  "type": "object",
  "properties": {
    "action": { "type": "string", "enum": ["list", "upsert", "patch", "transition", "remove"], "default": "list" },
    "workflow_id": { "type": "string", "const": "employment-coach" },
    "handoff_id": { "type": "string" },
    "title": { "type": "string" },
    "kind": { "type": "string", "const": "handoff_todo" },
    "stage": { "type": "string", "enum": ["material", "skill", "external", "cross_stage"] },
    "target_skill": { "type": "string", "enum": ["ontology-extraction", "skill-generation", "external-config"] },
    "intent": { "type": "string" },
    "category": { "type": "string" },
    "payload": { "type": "object" },
    "source": { "type": "string" },
    "acceptance": { "type": "string" },
    "status": { "type": "string", "enum": ["drafting", "ready_to_dispatch", "dispatched", "dirty", "confirmed", "needs_review", "dismissed"] },
    "fingerprint": { "type": "string" },
    "patch": { "type": "object" },
    "expected_revision": { "type": "integer" },
    "dispatch_id": { "type": "string" },
    "callback_summary": { "type": "string" },
    "reason": { "type": "string" }
  },
  "required": ["action"]
}
```

使用规则：

- 新建或合并同一意图：调用 `handoff`，传 `action = upsert`，必须传 `fingerprint`，避免同一意图换个说法就生成新条目。
- `workflow_id` 可省略；省略时 Gateway 内置工具使用配置的默认 workflow。Employment Coach 场景需要显式写出时只能写 `employment-coach`。
- `session_id` 由宿主从当前会话上下文注入，skill 不手写、不伪造、不跨 session 查询。
- 更新字段或 payload：调用 `handoff`，传 `action = patch`，保持同一个 `handoff_id`。
- 发 dispatch 前：调用 `handoff`，传 `action = list` 读取目标阶段，逐条确认 `status = ready_to_dispatch`。
- 发 dispatch 后：调用 `handoff`，传 `action = transition` 把本轮条目标为 `dispatched`，并写入 `dispatch_id`。
- 用户确认下游结果：调用 `handoff`，传 `action = transition` 把成功条目标为 `confirmed`，并写入回传摘要或 artifact 引用。
- 用户撤销：先把状态流转为 `dismissed`；只有 UI 不需要保留追溯时才调用 `handoff`，传 `action = remove`。

不要用对话正文、memory、临时文件或通用 todo 工具另维护一套清单。


## 通用结构

Handoff todo 至少包含以下字段。字段名使用 snake_case。

```json
{
  "session_id": "session_20260508_001",
  "workflow_id": "employment-coach",
  "handoff_id": "s_refund_init_001",
  "title": "技能：退货资格初判",
  "kind": "handoff_todo",
  "stage": "skill",
  "target_skill": "skill-generation",
  "intent": "生成退货资格初判技能",
  "category": "判定",
  "payload": {},
  "source": "用户描述退货咨询主线",
  "acceptance": "skill-generation 产出的 skill 文件能匹配该 Handoff todo 的 name + description",
  "status": "drafting",
  "fingerprint": "skill:return-qualification",
  "related_todos": [],
  "related_files": [],
  "revision": 1,
  "created_at": "2026-05-07T10:30:00Z",
  "updated_at": "2026-05-07T10:30:00Z"
}
```

字段说明：

- `session_id`：当前会话 session id，是 Handoff todo 的存储边界；由宿主注入并随 item 返回。
- `workflow_id`：当前交接流程作用域；本文件固定为 `employment-coach`，用于与其他通用 Handoff workflow 隔离。
- `handoff_id`：Handoff tool 返回的稳定 id，是 canonical id。
- `title`：给用户和 UI 看的短标题。
- `kind`：流程交接项固定使用 `handoff_todo`。
- `stage`：`material` / `skill` / `external` / `cross_stage`。
- `target_skill`：`ontology-extraction` / `skill-generation` / `external-config`。
- `intent`：一句话目标，给用户可读。
- `category`：阶段相关分类。
- `payload`：阶段相关结构化字段。
- `source`：来自对话、上传文件或系统回传的依据。
- `acceptance`：下游完成后如何判断可确认。
- `status`：见状态机。
- `fingerprint`：当前 session 范围内基于 `stage` + `target_skill` + 核心意图生成的稳定指纹。同一意图无论说法如何变化都应该保持一致，供 `upsert` 合并使用；skill 不需要把 `session_id` 写进 fingerprint。
- `revision`：工具维护的并发版本；多轮更新时递增。

## 状态机

| 状态 | 含义 |
| --- | --- |
| `drafting` | 还在引导中，明确度不够，不能 dispatch |
| `ready_to_dispatch` | 明确度达标，可在合适时机发 dispatch |
| `dispatched` | 已发 dispatch，等下游回传 |
| `dirty` | 在 `dispatched` 期间被用户改动，回传到达后需重发 |
| `confirmed` | 下游回传 + 用户确认通过 |
| `needs_review` | 上游配置文件改动后需要复核 |
| `dismissed` | 用户主动撤销，保留 id 不再 dispatch |

合法转移：

- `drafting` → `ready_to_dispatch` / `dismissed`
- `ready_to_dispatch` → `drafting` / `dispatched` / `dismissed`
- `dispatched` → `dirty` / `confirmed`
- `dirty` → `ready_to_dispatch`
- `confirmed` → `needs_review` / `dismissed`
- `needs_review` → `confirmed` / `ready_to_dispatch`

## ID 与字段规范

- Handoff tool 返回的 `session_id` + `handoff_id` 是存储主键；不要在对话里伪造不存在的 id。
- `<dispatch>` 和 `<dispatch_callback>` 只使用 `handoff_ids` / `handoff_id` 表达主键；系统层不得生成非 Handoff 主键字段，skill 文档、payload 与 artifact 一律以 Handoff id 为主键。
- 如果宿主仍把 Handoff todo 投影为 `WorkflowTodos`，只允许做投影，不允许重新把通用系统 todo 工具变成主存储。
- 下游 skill 必须消费 `handoff_todos` 数组里的完整结构，并校验每条 `session_id` 属于当前会话；不能只靠 id 重新猜 payload。


## 阶段 1：material

`target_skill = ontology-extraction`

**最低门槛**：至少 1 份资料被指认归类，且对应 Handoff todo 明确写出“要从中抽什么分类的本体 + 目标”。

**核心字段**：

- `category`: 资料类型（业务对象定义 / 决策规则 / 流程 SOP / 案例库 / 边界与约束 / 风格语料 / 其他）
- `payload.objective`: 一句话目标，例如“抽出退货场景里所有可能的退款节点和对应的判定规则”
- `payload.source_files`: 已上传的文件名列表
- `payload.scene_hint`: 场景类型（客服 / 销售 / 内勤 / ...）
- `payload.mode`: `incremental`（默认）/ `full_replace`

**明确度达标后**：状态可从 `drafting` 转为 `ready_to_dispatch`。

---

## 阶段 2：skill

`target_skill = skill-generation`

**最低门槛**：`payload.skills` 必须是 Skill 数组，且至少 1 个元素；数组必须覆盖初始数字员工模板包里已有的 skill，以及本轮用户新增、需要生成的 skill。每个 Skill 都必须用 `generation_action` 区分“已有复用”还是“需要新生成”，并具备明确的 `skill_name` + `skill_description`，能说清触发条件和期望输出。

**核心字段**：

- `payload.skills`: Skill 数组，`minItems = 1`
- `payload.skills[].origin`: `template_package` / `conversation` / `upload` 之一，且和真实来源一致
- `payload.skills[].generation_action`: `reuse_existing` / `generate_new` 之一；已有 skill 用 `reuse_existing`，本轮新增 skill 用 `generate_new`
- `payload.skills[].skill_name`: 技能名称，例如“退货资格初判”
- `payload.skills[].skill_description`
- `payload.skills[].trigger`
- `payload.skills[].expected_output`
- `payload.skills[].from_upload`
- `payload.skills[].existing_skill_slug`: `generation_action = reuse_existing` 时必填
- `payload.skills[].existing_artifact_path`: `generation_action = reuse_existing` 时必填，指向模板包已有 skill 产物
- `payload.skills[].template_package_id`: 来自模板包时填写模板包 id，例如 `customer-service-starter`；非模板来源可为空
- `payload.skills[].template_package_version`: 来自模板包时填写

`generation_action = reuse_existing` 的条目表示该 skill 已在初始数字员工模板包或上传的现成 skill 中存在，不要求 `skill-generation` 重新生成文件；`generation_action = generate_new` 的条目才是本轮需要生成的新业务 skill。

**payload 示例**：

```json
{
  "skills": [
    {
      "origin": "template_package",
      "generation_action": "reuse_existing",
      "skill_name": "订单状态查询",
      "skill_description": "根据订单号查询订单状态、物流进度和基础异常原因，并给出下一步指引",
      "trigger": "用户询问订单状态 / 物流进度 / 订单到哪了，且能匹配到订单号",
      "expected_output": "一条订单状态回复，以及必要时的人工转接建议",
      "from_upload": false,
      "existing_skill_slug": "order-status-query",
      "existing_artifact_path": "skills/order-status-query/SKILL.md",
      "template_package_id": "customer-service-starter",
      "template_package_version": "1.0.0"
    },
    {
      "origin": "conversation",
      "generation_action": "generate_new",
      "skill_name": "退货资格初判",
      "skill_description": "在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户",
      "trigger": "用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单",
      "expected_output": "一条回复消息（含结论 + 依据），以及一条工单流转建议（如需要人工介入）",
      "from_upload": false
    }
  ] 
}
```

**字段明确度对照**：

| 字段 | 不够明确的样子 | 够明确的样子 |
| --- | --- | --- |
| `payload.skills` | 缺失、不是数组，或只放本轮新能力、漏掉模板包已有 skill | Skill 数组，至少 1 项；同时覆盖初始数字员工模板包已有 skill 与本轮新增 skill |
| `origin` | 空、随手都写 `conversation`，无法判断来自模板、对话还是上传 | `template_package` / `conversation` / `upload` 之一，且和真实来源一致 |
| `generation_action` | 空、都写成 `generate_new`，导致已有 skill 被重复生成 | `reuse_existing` / `generate_new` 之一；已有 skill 用 `reuse_existing`，本轮新增 skill 用 `generate_new` |
| `skill_name` | “处理售后” | “退货资格初判” |
| `skill_description` | “用户问退货怎么办时回应一下” | “在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户” |
| `trigger` | “用户问起来” | “用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单” |
| `expected_output` | “回复用户” | “一条回复消息（含结论 + 依据），以及一条工单流转建议（如需要人工介入）” |
| `from_upload` | 缺失，或与 `origin` 冲突（例如 `origin = upload` 但 `from_upload = false`） | 布尔值；上传现成 skill 时为 `true`，非上传来源为 `false` |
| `existing_skill_slug` | `reuse_existing` 时为空，或写成中文显示名 | `reuse_existing` 时填写已有 skill slug，例如 `order-status-query`；`generate_new` 可为空 |
| `existing_artifact_path` | `reuse_existing` 时为空，或写成绝对路径 / 不存在路径 | `reuse_existing` 时填写模板包内相对路径，例如 `skills/order-status-query/SKILL.md`；`generate_new` 可为空 |
| `template_package_id` | `origin = template_package` 时为空，无法追溯来自哪个初始模板包 | 来自模板包时填写模板包 id，例如 `customer-service-starter`；非模板来源可为空 |
| `template_package_version` | `origin = template_package` 时为空，无法追溯版本 | 来自模板包时填写模板包版本，例如 `1.0.0`；非模板来源可为空 |

---

## 阶段 3：external

`target_skill = external-config`

**最低门槛**：`payload.external_capabilities` 必须是外部能力数组，且至少 1 个元素；每个普通外部能力都明确 `category` + `objective` + `target_system` + `integration_methods`；或者用户明确表达“不需要外部系统”（数组内写 1 个 `kind = skip` 的跳过项）。

**核心字段**：

| 字段 | 说明 |
| --- | --- |
| `payload.external_capabilities` | 外部能力数组，`minItems = 1` |
| `payload.external_capabilities[].kind` | `normal` / `skip` |
| `payload.external_capabilities[].category` | `read` / `write` / `notify` / `search` / `transform` 中之一；`kind = skip` 时可为空 |
| `payload.external_capabilities[].objective` | 一句话目标，例如“在用户咨询时，从 CRM 拉到该用户的最近 3 个订单” |
| `payload.external_capabilities[].target_system` | 目标系统名（CRM / ERP / 企微 / 钉钉 / 自有 OA 等） |
| `payload.external_capabilities[].integration_methods` | 对接方式数组，表示计划通过哪些接入通道实现该能力；建议值为 `mcp` / `cli` / `http_api` / `sdk` / `webhook` / `manual` / `unknown`，不写真实 endpoint、命令参数或凭据 |
| `payload.external_capabilities[].linked_skills` | 这个能力被哪条 skill 用到，使用 skill 阶段 Handoff id |
| `payload.external_capabilities[].auth_kind` | 凭据形式（OAuth / Bearer Token / 长期 Key 等），不含凭据值 |
| `payload.external_capabilities[].required_fields` | 需要读取、写入、通知或转换的字段列表 |

**payload 示例**：

```json
{
  "external_capabilities": [
    {
      "kind": "normal",
      "category": "read",
      "objective": "在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型",
      "target_system": "销售易 CRM",
      "integration_methods": ["mcp"],
      "linked_skills": ["s_seven_day_init_001", "s_nonstandard_assessment_001"],
      "auth_kind": "API Key",
      "required_fields": ["order_id", "created_at", "status", "customer_tier", "product_category"]
    }
  ]
}
```

## 写入红线

- 不在会话、Handoff payload、source、acceptance、callback 或 artifact 摘要中保存真实 token、密钥、密码、API Key、连接串。
- 不把 Handoff todo 当作阶段占位物；每条都必须有可交给下游执行的目标。
- 不绕过 Handoff tool 自建文件、内存清单或对话内清单。
- 不让下游 skill 修改 `employment-coach-conversation` 维护的流程 Handoff todo；下游只回传 callback，确认和状态合流由上游完成。
