---
name: external-config
description: 根据外部能力工单输入，生成外部系统连接配置初稿，并仅写入当前沙箱 external/ 目录。用于处理 read/write/notify/search/transform 外部能力、skip 记录、字段映射占位和凭据槽位引用；不要用于对话引导、收集真实凭据、修改工单状态、生成业务 skill、执行本体提取或实例打包。
compatibility: HireBot employment-coach-conversation v1.0
license: Proprietary. NCrew employment-coach internal flow.
metadata:
  openclaw:
    emoji: "🔌"
  category: generation
  autonomy: 85
  trigger: hiring-session-external, external-stage-active
  input: external-capability-workorder, secure-credential-context
  output: external-config-drafts, emit-artifact
---

# External Config

当雇佣流程进入阶段三（外部能力配置）时，使用本 skill 把已经明确的外部能力需求落成可审阅、可校验、可继续由实例包消费的配置草案。

本 skill 不负责和业务用户继续追问需求，也不负责真正调用外部系统。

## 何时使用

使用本 skill 当：

- 需要为 CRM、ERP、IM、工单、自研系统等生成读取、写入、通知、搜索或转换配置
- 需要记录用户明确表示"不接外部系统"的 skip 状态
- 需要把凭据形式映射成安全的凭据槽位引用

不要使用本 skill 当：

- 还需要引导用户说清楚外部能力，这属于 `employment-coach-conversation`
- 需要从会话文本中读取、追问或验证真实 token、密码、API Key、连接串
- 需要写 `ontology/`、`skills/`、`config/` 或 `memory.md`
- 需要直接调用外部系统做联通性测试

## 核心立场

你是外部系统配置落地器，不是对话教练，也不是凭据管理员。

你的工作只回答四件事：

1. 这条外部能力属于 `read`、`write`、`notify`、`search` 还是 `transform`
2. 它面向哪个目标系统，服务哪些已确认 skill
3. 需要哪些字段、认证形式和安全凭据槽位
4. 配置草案、索引是否足够让上游确认

## emit_artifact 使用规范

本 skill 执行期间须在两个关键节点调用 `emit_artifact`，推动前端外部能力阶段（External 胶囊）更新。

### 进度节点（isTerminal: false）

在开始处理第一条外部能力配置时调用：

```json
{
  "kind": "data",
  "artifactType": "external_config_progress",
  "label": "正在生成外部系统配置初稿，共 {N} 条能力待处理",
  "skillName": "external-config",
  "stage": "external-config",
  "isTerminal": false,
  "displayHint": "progress",
  "data": {
    "total_capabilities": 2,
    "completed_capabilities": 0,
    "status": "running"
  }
}
```

### 完成节点（isTerminal: true）

所有外部能力配置落盘并通过校验后调用：

```json
{
  "kind": "data",
  "artifactType": "external_config_done",
  "label": "外部能力配置初稿已完成，共 {N} 条能力，凭据槽位待表单填写",
  "skillName": "external-config",
  "stage": "external-config",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_capabilities": 2,
    "skip": false,
    "capability_ids": ["e_xiaoshouyi_read_order_001", "e_im_notify_001"],
    "pending_credential_slots": ["xiaoshouyi-crm-api-key"],
    "status": "done"
  }
}
```

如果用户明确跳过外部系统：

```json
{
  "kind": "data",
  "artifactType": "external_config_done",
  "label": "用户明确不需要外部系统接入，外部阶段已跳过",
  "skillName": "external-config",
  "stage": "external-config",
  "isTerminal": true,
  "displayHint": "tree",
  "data": {
    "total_capabilities": 0,
    "skip": true,
    "capability_ids": [],
    "pending_credential_slots": [],
    "status": "done"
  }
}
```

### 约束

- **先调用后输出**：识别到可推送事件时，先调用 `emit_artifact`，再继续配置生成或对话输出
- **data 禁止凭据**：data 字段中不得写入 token / 密钥 / 密码 / API Key / 连接串
- **label 用业务语言**：描述对用户有意义的进度，不暴露内部字段名

## Secure Credential Input Mode

真实凭据只能从系统层的安全表单 / 安全存储通道进入本 skill，不能来自用户会话文本或工单 payload。

当系统层传入安全凭据上下文时，输入形态应类似：

```yaml
secure_credential_context:
  credentials:
    - credential_slot: xiaoshouyi-crm-api-key
      secret_ref: EXTERNAL_XIAOSHOUYI_CRM_API_KEY
      value: <opaque secret value supplied out-of-band>
      source: secure_form
```

处理规则：

- 可以读取 `value` 以完成安全存储绑定，但不得写入 `external/*.json`、README、回传摘要或错误消息。
- artifact 中只保留 `secretRef` / `credentialSlot` / `bindingStatus`。
- 如果 MVP 环境尚未提供安全存储能力，应将 capability 标为 `partial`，保留待绑定的 `credentialSlot`，并在 `capability_ids` 中提示需要系统层补齐。
- 不做真实外部系统联通性测试；凭据绑定成功不等于外部接口可调用。

## 输入格式

工单输入包含外部能力清单，每条能力包含：

```yaml
external_capabilities:
  - kind: normal
    category: read
    objective: 在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型
    target_system: 销售易 CRM
    integration_methods: [mcp]
    linked_skills: [refund-eligibility-check, order-status-query]
    auth_kind: API Key
    required_fields: [order_id, created_at, status, customer_tier, product_category]
```

字段说明：

| 字段 | 说明 |
|------|------|
| `kind` | `normal`（正常接入）或 `skip`（用户明确不接外部系统） |
| `category` | `read` / `write` / `notify` / `search` / `transform` |
| `objective` | 业务目标描述 |
| `target_system` | 目标系统名称 |
| `integration_methods` | 对接方式：`mcp` / `http_api` / `sdk` / `webhook` / `cli` / `manual` / `unknown` |
| `linked_skills` | 关联的业务 skill slug 列表 |
| `auth_kind` | 认证类型：`none` / `oauth2` / `bearer_token` / `api_key` / `basic` |
| `required_fields` | 需要的字段清单 |

## 输出目录

所有正式产物只写入当前沙箱的 `external/` 目录。具体绝对路径由上游 `employment-coach-conversation` 在 `external_workorder_summary` 的 `data.workspace_root` 字段中传入（雇佣教练会话初始化时由沙箱解压工具创建并锁定的真实绝对路径，运行时确定，本 skill 当作不透明字符串使用），实际写入路径为 `<workspace_root>/external/`（用 artifact 收到的真实路径替换 `<workspace_root>`）。若 `workspace_root` 字段缺失，停下来报错，不要靠 `ls /workspace` 推断或自行拼接 `/workspace/<slug>`。

```text
<workspace_root>/external/
  external-config.index.json
  systems/
    <system-slug>.json
  capabilities/
    <capability-id>.json
  README.md
```

目录语义：

- `external-config.index.json`：外部配置总索引，列出所有能力、系统、skip 记录和校验摘要。
- `systems/<system-slug>.json`：按目标系统聚合认证形式、凭据槽位、能力列表和安全说明。
- `capabilities/<capability-id>.json`：每条外部能力的主配置草案；`kind: skip` 也使用同一路径记录。
- `README.md`：给人工审阅的短说明，不包含任何真实凭据。

输出模板见 [templates/capability.template.json](templates/capability.template.json)、[templates/skip.template.json](templates/skip.template.json) 与 [templates/index.template.json](templates/index.template.json)。

## 执行流程

1. **入口校验**：确认外部能力工单字段合法，`external_capabilities[]` 至少 1 项。
2. **凭据扫描**：检查工单、objective、目标系统描述中是否混入疑似 token、密码、API Key 或连接串。
3. **系统归一化**：从 `external_capabilities[].target_system` 生成稳定 `system_slug`，同一系统的多条 capability 合并进同一个 `systems/<system-slug>.json`。
4. **能力建模**：按 `external_capabilities[]` 逐项生成 capability 草案，保留 objective、category、integration_methods、linked_skills、required_fields、auth_kind。
5. **凭据槽位生成**：为 `auth_kind != none` 的普通能力生成 `secretRef` 或 `credentialSlot`，值必须为空。
6. **落盘**：写入 `external/capabilities/<capability-id>.json`；更新 `external/systems/<system-slug>.json` 和 `external/external-config.index.json`。
7. **校验**：确认普通能力字段完整、skip 可识别、索引路径存在、无明文凭据。
8. **emit_artifact**：校验通过后调用 `emit_artifact`（isTerminal: true），推送阶段完成。
9. **用户摘要**：输出对业务用户可读的摘要，不暴露沙箱路径或凭据值。

## 安全红线

- 不在会话、产物或摘要中保存真实 token、密钥、密码、API Key、连接串。
- `auth_kind` 只表示凭据类型，`secretRef` / `credentialSlot` 只表示安全存储引用。
- 通过安全表单传入的真实凭据只允许进入安全存储绑定流程，不允许进入普通 artifact。
- 如果输入里出现疑似凭据值，必须阻断该项或标为 `partial/failed`，错误说明只写"发现疑似凭据值"，不得复述原文。
- 不把凭据值写入 `external/*.json`、`README.md` 或任何摘要。
- 不直接调用外部系统验证凭据。

## 质量自检

输出前检查：

- [ ] 所有普通 external capability 都有 `category`、`objective`、`target_system`、`linked_skills`
- [ ] `category` 只使用 `read/write/notify/search/transform`
- [ ] `kind: skip` 已写入可被诊断识别的 skip 记录
- [ ] 索引中的 artifact path 均为相对路径
- [ ] 没有任何真实凭据值落盘或出现在摘要
- [ ] 失败项不会阻塞其他成功项

## References

- [references/output-layout.md](references/output-layout.md)：`external/` 目录布局和 JSON 产物结构
- [references/security-and-validation.md](references/security-and-validation.md)：凭据安全、字段校验和失败策略
- [templates/capability.template.json](templates/capability.template.json)：单条 capability 配置模板
- [templates/skip.template.json](templates/skip.template.json)：跳过外部系统配置模板
- [templates/index.template.json](templates/index.template.json)：外部配置索引模板
