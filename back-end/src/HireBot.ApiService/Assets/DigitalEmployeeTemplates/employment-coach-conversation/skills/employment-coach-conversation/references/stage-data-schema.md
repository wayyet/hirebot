# 阶段产物 data 字段结构

本文件定义 `emit_artifact` 调用中各 `artifactType` 的 `data` 字段结构，供 `employment-coach-conversation` skill 构造 artifact payload 时参考。

---

## ⛔ 禁止字段与禁止模式

以下内容**绝对不能**出现在任何 `data` 字段中：

| 禁止的字段名 / 值 | 来源说明 | 应改用 |
|-----------------|---------|-------|
| 顶层 `status` 字段（除打包相关 artifact） | 旧 handoff 状态机残留 | 非打包 artifact 不需要顶层 status；各 item 内部有自己的 `status: pending/ready` |
| `status: "ready_to_dispatch"` | 旧 dispatch 协议 | 用 `isTerminal: true` 表示阶段完成 |
| `status: "dispatched"` / `"confirmed"` / `"needs_review"` / `"dirty"` | 旧 handoff 状态机 | 同上 |
| `capabilities` 字段 | 旧格式 | 改用 `items[]` |
| `materials` 字段（顶层） | 旧格式 | 改用 `items[]` |
| `scene_hint` 字段 | 旧格式 | 不需要，schema 中无此字段 |
| `dispatch_payload` / `handoff_todos` / `dispatch_target` | 旧 dispatch 协议 | 全部删除 |

**data 字段的合法顶层 key 只有下方各 artifactType 示例中明确列出的字段。任何不在示例中的 key 均视为错误。**

**唯一例外**：`stage4_packaging` 的打包相关 artifact（`packaging_progress`、`packaging_testcases_progress`、`packaging_testcases_done`）允许顶层 `status`；其中 `packaging_progress.status` 仅允许 `waiting_downstream` / `packing`。

**对话回复中同样禁止出现以下词语**：`dispatch 闭环`、`dispatch 信号`、`handoff 工单`、`ready_to_dispatch`、`dispatch 给下游`。

---

## 阶段 1：资料（stage1_material）

### material_collection_progress（进度更新，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "summary": "已进入资料阶段，等待用户上传或描述业务资料",
  "requested_categories": [
    {
      "title": "历史工单",
      "description": "优先上传最近处理不顺的真实案例",
      "examples": ["投诉工单", "售后记录"]
    }
  ],
  "collected_count": 2,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "source_path": "uploads/非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件与处置路径",
      "status": "pending"
    },
    {
      "title": "客服话术风格",
      "source_hint": "用户描述",
      "source_path": null,
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
| `workspace_root` | 初始化时必填 | 当前会话工作区真实绝对路径，供下游 skill 读取上传文件与写入产物 |
| `template_slug` | 初始化时必填 | 当前模板 slug |
| `summary` | 否 | 当前进度摘要 |
| `collected_count` | 已有资料后必填 | 当前已收集的资料条目数；开场亮灯但尚无资料时可省略 |
| `requested_categories[]` | 否 | 开场阶段建议用户优先上传的资料分类，最多 3 项；仅用于右侧资料阶段展示，不代表已收集资料 |
| `requested_categories[].title` | 是 | 资料分类名称，对用户可读，如"历史工单""SOP""产品手册" |
| `requested_categories[].description` | 否 | 为什么需要这类资料，一句话即可 |
| `requested_categories[].examples[]` | 否 | 该分类下的示例文件或来源，最多 2 个 |
| `items[]` | 已有资料后必填 | 已整理的资料清单；开场亮灯但尚无资料时可省略 |
| `items[].title` | 是 | 资料标题，对用户可读 |
| `items[].source_hint` | 是 | 来源描述（对用户可读，如"用户上传：sales.csv"或"用户描述"） |
| `items[].source_path` | 否 | 可被下游直接读取的实际文件路径。工作区资料填相对路径（如 `uploads/<文件名>`）；Gateway 直传资料必须先读 `/app/memory/media-cache/{mediaId}.json`，再填元数据 `path` 字段中的真实路径；纯描述来源填 `null`。不要填 `[FILE_URL:...]`、`/media/{mediaId}` 或无扩展名的 `/app/memory/media-cache/{mediaId}` 标记。**只要是上传文件，这个字段就是事实上的必填项；缺失时该条不能进入 terminal handoff。若是刚上传后的短暂同步窗口，可先等待最多 5 秒再决定是否阻断。** |
| `items[].category` | 是 | 资料分类：决策规则 / 话术风格 / 业务流程 / 数据字段 / 其他 |
| `items[].objective` | 是 | 本条资料要抽取的目标 |
| `items[].status` | 是 | `pending`（待处理）/ `ready`（已就绪）。**上传文件若缺少 `source_path`、文件不存在或内容未读到，只能保持 `pending`。** |
| `notes` | 否 | 补充说明 |

---

### material_handoff_summary（阶段完成，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "total_items": 3,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "source_path": "uploads/非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件、处置档位和人工分流触发节点",
      "status": "ready"
    }
  ],
  "summary": "共整理 3 份业务资料，已确认抽取方向，准备进入技能定义阶段"
}
```

字段说明：与 `material_collection_progress` 相同；terminal 时顶层必须透传会话初始化阶段锁定的真实 `workspace_root` 与 `template_slug`，`status` 全部为 `ready`，`source_path` 必须尽可能补全（有上传文件的条目**必填**），并补充 `summary` 字段。`ontology-extraction` skill 将以 `workspace_root` 与 `source_path` 为准定位实际文件，`source_hint` 仅供人工阅读。**如果缺少 `workspace_root` / `template_slug`，或上传条目只有 `source_hint`、没有 `source_path`，或已经知道内容未能读取到，则不得发出 `material_handoff_summary`。**

---

## 阶段 2：技能（stage2_skill）

说明：阶段 2 是“技能”主阶段，内部固定拆成两个子步骤：
- 技能定义：通过 `skill_workorder_progress` / `skill_workorder_summary` 表达。
- 技能生成确认/执行：技能定义完成后必须先发 `skill_generation_ready`，再等待下游 `skill-generation` 处理。

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
      "expected_output": "输出退货资格判断、原因说明和下一步处理建议",
      "generation_action": "generate_new",
      "status": "pending"
    },
    {
      "name": "order-status-query",
      "display_name": "订单状态查询",
      "description": "根据订单号查询状态、物流进度和基础异常",
      "trigger": "用户询问订单状态 / 物流进度",
      "expected_output": "输出订单状态、物流节点和异常提示",
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
| `items[].expected_output` | 是 | 技能执行后的预期输出，用于下游生成实现与 projection 消费说明 |
| `items[].generation_action` | 是 | `generate_new`（新生成）/ `reuse_existing`（复用已有） |
| `items[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明 |

---

### skill_workorder_summary（技能定义子步骤完成，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "total_items": 4,
  "new_count": 2,
  "reuse_count": 2,
  "items": [ "... 同 progress items ..." ],
  "summary": "共规划 4 个技能：2 个新生成、2 个复用模板默认能力；技能定义已确认，等待确认是否开始技能生成"
}
```

字段说明（在 `skill_workorder_progress` 基础上新增 / 强化）：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 是 | 当前会话工作区真实绝对路径。Projection pass 启动时必须依赖该字段。 |
| `template_slug` | 是 | 当前模板 slug，用于下游链路一致性与日志定位。 |
| `total_items` | 是 | 本轮技能定义条目总数。 |
| `new_count` | 否 | 本轮 `generate_new` 技能数量。 |
| `reuse_count` | 否 | 本轮 `reuse_existing` 技能数量。 |
| `summary` | 是 | 对用户可读的技能定义收口说明。 |

约束：`workspace_root` 与 `template_slug` 必须直接来自会话初始化阶段锁定的真实值；缺少任一字段时不得发出 `skill_workorder_summary`，也不得依赖前端从其它 artifact 补齐。

---

### skill_generation_ready（技能生成确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "new_count": 2,
  "reuse_count": 2,
  "skill_names": ["refund-eligibility-check", "order-status-query", "refund-priority-routing", "return-progress-track"],
  "next_step": "等待用户确认开始生成技能实现"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 否 | 当前会话工作区真实绝对路径；若上游已拿到，建议透传给下游 |
| `template_slug` | 否 | 当前模板 slug；若上游已拿到，建议透传 |
| `pending_skill_count` | 是 | 当前等待进入技能生成子步骤的技能数量 |
| `new_count` | 否 | 本轮 `generate_new` 技能数量，便于下游区分真正新生成项 |
| `reuse_count` | 否 | 本轮 `reuse_existing` 技能数量，便于下游区分复用项 |
| `skill_names[]` | 是 | 本轮进入技能生成子步骤的技能名称或 slug 列表 |
| `next_step` | 是 | 固定描述下一步是等待用户确认开始技能生成 |

补充约束：
- 这个 artifact 只表示阶段 2 内部进入“技能生成确认/执行”子步骤；前端应保留“技能定义已确认”的子状态，但主 `stage2_skill` 在 `skill-generation` 完成前仍保持进行中。
- 发出该 artifact 后，若用户未明确同意，不得提前触发 `skill-generation`，也不得进入阶段 3。

### Projection Pass 子轨（ontology-projection，下游）

> 以下 artifact 由下游 `ontology-extraction` 发出，`stage` 固定为 `ontology-projection`。

#### ontology_projection_progress（进度更新，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "summary": "正在为技能生成准备本体投影视图"
}
```

#### ontology_projection_done（子步骤完成，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "projected_count": 3,
  "skipped_count": 1,
  "summary": "projection pass 已完成，可触发技能生成"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 否 | 当前会话工作区路径，便于下游链路透传 |
| `template_slug` | 否 | 当前模板 slug |
| `pending_skill_count` | progress 时建议填写 | 参与 projection pass 的技能数量 |
| `projected_count` | done 时建议填写 | 成功生成 projection 视图的技能数 |
| `skipped_count` | done 时可选 | 未生成 projection 视图的技能数 |
| `summary` | 否 | 对用户可读的一句话状态 |

---

## 阶段 3：外部（stage3_external）

### external_workorder_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 1,
  "external_capabilities": [
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
| `external_capabilities[]` | 是 | 外部能力清单 |
| `external_capabilities[].name` | 是 | 外部能力 slug |
| `external_capabilities[].display_name` | 是 | 对用户可读的名称 |
| `external_capabilities[].category` | 是 | `read` / `write` / `notify` / `search` / `transform` / `skip` |
| `external_capabilities[].objective` | 是 | 调用目的 |
| `external_capabilities[].target_system` | 是 | 目标系统名称 |
| `external_capabilities[].auth_kind` | 是 | `none` / `oauth2` / `bearer_token` / `api_key` / `basic` |
| `external_capabilities[].linked_skills` | 是 | 关联的 skill name 列表（非空） |
| `external_capabilities[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明（不得包含凭据值） |

---

### external_workorder_summary（阶段完成，isTerminal: true）

```json
{
  "total_capabilities": 2,
  "skip": false,
  "external_capabilities": [ "... 同 progress external_capabilities ..." ],
  "summary": "共规划 2 项外部能力接入，凭据配置待表单填写，外部阶段已确认"
}
```

如果用户明确表示不需要外部系统：

```json
{
  "total_capabilities": 0,
  "skip": true,
  "external_capabilities": [],
  "summary": "用户明确声明不需要外部系统接入，外部阶段已跳过"
}
```

---

## 通用约束

- **data 中禁止写入凭据值**：token / 密钥 / 密码 / API Key / 连接串一律不得出现在 `data` 字段中
- **凭据形式可以描述**：`auth_kind` 描述鉴权方式（如 `oauth2`、`bearer_token`），不写具体凭据值
- **status 字段**：除打包相关 artifact（`packaging_progress`、`packaging_testcases_progress`、`packaging_testcases_done`）外，不允许 `data` 顶层 `status`。条目级 `items[].status` / `external_capabilities[].status` 仍按 `pending` / `ready` 使用；terminal artifact 中条目状态应全部为 `ready`
- **summary 字段**：terminal artifact 必须包含对用户可读的 `summary`，进度 artifact 可选

---

## 阶段 4：实例打包（stage4_packaging）

### packaging_testcases_ready（确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "next_step": "等待用户确认是否生成评估测试用例"
}
```

### packaging_testcases_progress（进度更新，isTerminal: false）

```json
{
  "status": "generating_testcases",
  "target_path": "testcases/evaluation-test-cases.json",
  "summary": "正在生成评估测试用例"
}
```

> 说明：`packaging_testcases_progress` 的 `status` 为打包相关 artifact 的允许例外，用于前端展示子步骤进度。

### packaging_testcases_done（子步骤完成，isTerminal: true）

```json
{
  "status": "testcases_ready",
  "target_path": "testcases/evaluation-test-cases.json",
  "summary": "评估测试用例已生成并写入工作区"
}
```

### packaging_progress（打包进度，isTerminal: false）

等待下游时：

```json
{
  "status": "waiting_downstream",
  "pending_downstream_skills": ["skill-generation"],
  "included": ["ontology/", "skills/", "external/", "config/", "manifest.json"]
}
```

真正打包时：

```json
{
  "status": "packing",
  "included": ["ontology/", "skills/", "external/", "config/", "manifest.json"]
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `status` | 是 | 仅允许 `waiting_downstream` / `packing` |
| `pending_downstream_skills[]` | `waiting_downstream` 时必填 | 尚未完成的下游 skill，仅允许 `ontology-extraction` / `skill-generation` |
| `included[]` | 否 | 计划打包包含的目录白名单提示 |

### template_package（文件产物，isTerminal: true）

`template_package` 的 `kind` 必须为 `file`，关键字段 `fileUrl` / `fileName` 位于 artifact 顶层参数，不在 `data` 内。
