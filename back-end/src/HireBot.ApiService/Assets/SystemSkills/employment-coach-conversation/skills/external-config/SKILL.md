---
name: external-config
description: 根据雇佣教练阶段三 external todo，生成外部系统连接配置初稿，并仅写入当前沙箱 external/ 目录。用于处理 read/write/notify/search/transform 外部能力、skip 记录、字段映射占位和凭据槽位引用；不要用于对话引导、收集真实凭据、修改系统 todo 状态、生成业务 skill、执行本体提取或实例打包。
metadata: {"openclaw":{"emoji":"🔌"}}
license: Proprietary. NCrew employment-coach internal flow.
---

# External Config

当 `employment-coach-conversation` 通过 `<dispatch target=external-config>` 指定阶段三系统 todo 时，使用本 skill。

本 skill 的职责是把已经明确的外部能力需求落成可审阅、可校验、可继续由实例包消费的配置草案。它不负责和业务用户继续追问需求，也不负责真正调用外部系统。

## 何时使用

使用本 skill 当：

- 输入包含 `stage: external`、`target_skill: external-config` 的系统 todo
- 需要为 CRM、ERP、IM、工单、自研系统等生成读取、写入、通知、搜索或转换配置
- 需要记录用户明确表示“不接外部系统”的 skip 状态
- 需要把凭据形式映射成安全的凭据槽位引用

不要使用本 skill 当：

- 还需要引导用户说清楚外部能力，这属于 `employment-coach-conversation`
- 需要从会话文本中读取、追问或验证真实 token、密码、API Key、连接串
- 需要修改系统 todo 状态
- 需要写 `ontology/`、`skills/`、`config/` 或 `memory.md`
- 需要直接调用外部系统做联通性测试

## 核心立场

你是外部系统配置落地器，不是对话教练，也不是凭据管理员。

你的工作只回答四件事：

1. 这条外部能力属于 `read`、`write`、`notify`、`search` 还是 `transform`
2. 它面向哪个目标系统，服务哪些已确认 skill
3. 需要哪些字段、认证形式和安全凭据槽位
4. 配置草案、索引和回传摘要是否足够让上游确认

## Employment Coach Todo Mode

输入来自雇佣教练阶段三 dispatch 时，优先按系统 todo 的 `notes` 合约处理，不要把它当普通会话描述重新抽取。

输入形态：

```yaml
dispatch:
  target: external-config
  todos: [e_xiaoshouyi_read_order_001]

todos:
  - id: e_xiaoshouyi_read_order_001
    stage: external
    target_skill: external-config
    intent: 配置销售易 CRM 的订单读取能力
    category: read
    payload:
      objective: 在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型
      target_system: 销售易 CRM
      linked_skills: [s_seven_day_init_001, s_nonstandard_assessment_001]
      auth_kind: API Key
      required_fields: [order_id, created_at, status, customer_tier, product_category]
      kind: normal
    source: 用户说明退货资格初判需要查 CRM 订单
    acceptance: external/ 中包含可调用的销售易订单读取配置 + 字段映射
    status: ready_to_dispatch
```

处理规则：

- 只处理 `target_skill: external-config` 且状态为 `ready_to_dispatch` 或 `dirty` 的 todo。
- 每条 todo 必须保留 `id`，并在产物与回传中按同一个 `todo_id` 关联。
- `payload.kind: skip` 表示用户明确不接外部系统；仍需写入 skip 记录和索引项。
- `payload.objective` 映射为 capability 的业务目标。
- `category` 映射为 capability 类型，取值只能是 `read`、`write`、`notify`、`search`、`transform`。
- `payload.target_system` 映射为系统名称与 slug 来源。
- `payload.linked_skills` 映射为能力依赖的上游 skill todo id；普通能力不得为空。
- `payload.auth_kind` 只映射为认证类型和凭据槽位，不得携带任何真实凭据值。
- `payload.required_fields` 映射为字段需求与待补映射清单。
- 单条失败不能吞掉其他 todo 的成功结果；失败项在 `todo_results` 中标为 `failed`，并给出可被雇佣教练复述的原因。

## Secure Credential Input Mode

真实凭据只能从系统层的安全表单 / 安全存储通道进入本 skill，不能来自用户会话文本或 todo payload。

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

- 可以读取 `value` 以完成安全存储绑定，但不得写入 `external/*.json`、README、callback、日志摘要或错误消息。
- artifact 中只保留 `secretRef` / `credentialSlot` / `bindingStatus`。
- 如果 MVP 环境尚未提供安全存储能力，应将 capability 标为 `partial`，保留待绑定的 `credentialSlot`，并在 `todo_results[].credential_slots` 中提示需要系统层补齐。
- 不做真实外部系统联通性测试；凭据绑定成功不等于外部接口可调用。

## 输出目录

所有正式产物只写入当前沙箱的 `external/` 目录：

```text
external/
  external-config.index.json
  systems/
    <system-slug>.json
  capabilities/
    <todo-id>.json
  README.md
```

目录语义：

- `external-config.index.json`：外部配置总索引，列出所有能力、系统、skip 记录和校验摘要。
- `systems/<system-slug>.json`：按目标系统聚合认证形式、凭据槽位、能力列表和安全说明。
- `capabilities/<todo-id>.json`：每条系统 todo 的主配置草案；`kind: skip` 也使用同一路径记录。
- `README.md`：给人工审阅的短说明，不包含任何真实凭据。

输出模板见 [templates/capability.template.json](templates/capability.template.json)、[templates/skip.template.json](templates/skip.template.json) 与 [templates/index.template.json](templates/index.template.json)。

## 执行流程

1. **入口校验**：确认 dispatch target、todo stage、target_skill、status、category、payload 字段合法。
2. **凭据扫描**：检查 todo、source、acceptance、required_fields、目标系统描述中是否混入疑似 token、密码、API Key 或连接串。
3. **系统归一化**：从 `target_system` 生成稳定 `system_slug`，同一系统的多条 capability 合并进同一个 `systems/<system-slug>.json`。
4. **能力建模**：按 todo 生成 capability 草案，保留 objective、category、linked_skills、required_fields、auth_kind 和 acceptance。
5. **凭据槽位生成**：为 `auth_kind != none` 的能力生成 `secretRef` 或 `credentialSlot`，值必须为空。
6. **落盘**：写入 `external/capabilities/<todo-id>.json`、更新 `external/systems/<system-slug>.json` 和 `external/external-config.index.json`。
7. **校验**：确认普通能力字段完整、skip 可识别、索引路径存在、无明文凭据。
8. **回传**：输出 `dispatch_callback` 兼容摘要，包含 `user_summary`、artifacts、status、errors。

## 回传格式

完成后返回结构化摘要，供主 skill 推送回 `employment-coach-conversation`：

```yaml
dispatch_callback:
  source_dispatch_target: external-config
  todo_ids: [e_xiaoshouyi_read_order_001]
  user_summary: 已生成销售易 CRM 的订单读取配置初稿，包含订单号、创建时间、状态、客户等级、商品类型字段占位；凭据需要在右侧表单补齐。
  artifacts:
    - path: external/capabilities/e_xiaoshouyi_read_order_001.json
      kind: external-config
    - path: external/external-config.index.json
      kind: external_index
  todo_results:
    - todo_id: e_xiaoshouyi_read_order_001
      status: success
      artifacts:
        - path: external/capabilities/e_xiaoshouyi_read_order_001.json
          kind: external-config
      credential_slots:
        - credential_slot: xiaoshouyi-crm-api-key
          secret_ref: EXTERNAL_XIAOSHOUYI_CRM_API_KEY
          binding_status: pending
      errors: []
  status: success
  errors: []
```

`user_summary` 必须短、业务用户能听懂；不要暴露沙箱绝对路径、内部 hook、orchestrator、endpoint 细节或凭据值。

## 安全红线

- 不在会话、产物、callback 或日志摘要中保存真实 token、密钥、密码、API Key、连接串。
- `auth_kind` 只表示凭据类型，`secretRef` / `credentialSlot` 只表示安全存储引用。
- 通过安全表单传入的真实凭据只允许进入安全存储绑定流程，不允许进入普通 artifact。
- 如果输入里出现疑似凭据值，必须阻断该项或标为 `partial/failed`，错误说明只写“发现疑似凭据值”，不得复述原文。
- 不把凭据值写入 `external/*.json`、`README.md` 或任何 source digest。
- 不直接调用外部系统验证凭据。

## 质量自检

输出前检查：

- [ ] 所有普通 external todo 都有 `category`、`objective`、`target_system`、`linked_skills`
- [ ] `category` 只使用 `read/write/notify/search/transform`
- [ ] `kind: skip` 已写入可被诊断识别的 skip 记录
- [ ] 每条 capability 都能回指 todo id
- [ ] 索引中的 artifact path 均为相对路径
- [ ] 没有任何真实凭据值落盘或出现在 `user_summary`
- [ ] 失败项不会阻塞其他成功项回传

## References

- [references/output-layout.md](references/output-layout.md)：`external/` 目录布局和 JSON 产物结构
- [references/security-and-validation.md](references/security-and-validation.md)：凭据安全、字段校验和失败策略
- [templates/capability.template.json](templates/capability.template.json)：单条 capability 配置模板
- [templates/skip.template.json](templates/skip.template.json)：跳过外部系统配置模板
- [templates/index.template.json](templates/index.template.json)：外部配置索引模板
