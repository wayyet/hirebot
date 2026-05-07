# Security And Validation

本文定义 `external-config` 的安全红线、结构校验和失败策略。

## 凭据红线

禁止落盘或回显：

- token
- 密钥
- 密码
- API Key
- Bearer 值
- OAuth client secret
- 数据库连接串
- webhook secret
- 私钥块

允许记录：

- `auth.kind`: 凭据类型，例如 `OAuth`、`Bearer Token`、`API Key`、`应用凭据`、`内部 token`、`none`
- `secretRef`: 安全存储引用名
- `credentialSlot`: 表单或安全存储槽位名
- `bindingStatus`: `bound`、`pending`、`not_required`
- `required_fields`: 业务字段名，不是凭据值

## 安全表单凭据上下文

系统层可以通过安全表单 / 安全存储通道把真实凭据交给 `external-config`，但该值只允许用于绑定安全存储引用：

```yaml
secure_credential_context:
  credentials:
    - credential_slot: xiaoshouyi-crm-api-key
      secret_ref: EXTERNAL_XIAOSHOUYI_CRM_API_KEY
      value: <opaque secret value>
      source: secure_form
```

处理要求：

- `value` 不得写入任何 `external/` artifact、README、callback、日志摘要或错误消息。
- 绑定成功时只写 `bindingStatus: bound`。
- 当前环境没有安全存储能力时写 `bindingStatus: pending`，整体 callback 可为 `partial`。
- `auth.kind: none` 时写 `bindingStatus: not_required`，不生成 `secretRef`。

## 疑似凭据检测

输入、产物和 callback 摘要中出现以下形态时，应视为高风险：

- `password=...`、`pwd=...`、`secret=...`、`token=...`、`api_key=...`
- `Bearer <long-value>`
- 长度很长且高熵的连续字符串
- `-----BEGIN ... PRIVATE KEY-----`
- 常见连接串片段，如 `AccountKey=`、`SharedAccessKey=`、`DefaultEndpointsProtocol=`

处理方式：

1. 不复述原文。
2. 不写入 artifact。
3. 将对应 todo 标为 `failed`，或在可安全剔除时标为 `partial`。
4. `errors` 只写“发现疑似凭据值，请通过右侧表单重新提交”。

## 字段校验

普通能力必须满足：

- `category` 属于 `read/write/notify/search/transform`
- `payload.objective` 非空
- `payload.target_system` 非空
- `payload.linked_skills` 至少 1 个，且由系统层证明对应 skill todo 已 confirmed
- `auth.kind` 有值；缺失时优先返回 `partial`，只有系统层允许临时草案时才写 `unknown` 并给 warning
- artifact path 使用相对路径

skip 能力必须满足：

- `kind` 为 `skip`
- 有可读 reason
- 写入 index 的 `skips` 或等价列表
- 写入 `external/capabilities/<todo-id>.json`，便于 callback 和诊断用统一路径关联 artifact

## 分类口径

| category | 使用场景 | 示例 |
| --- | --- | --- |
| `read` | 按已知 id 或上下文读取实体 | 读取 CRM 订单详情 |
| `search` | 按条件筛选、检索或查重 | 搜索近 3 个月工单 |
| `write` | 创建、更新或流转系统记录 | 创建企微工单 |
| `notify` | 向人或群发送消息 | 通知经理复核 |
| `transform` | 格式转换、字段归一或数据整理 | 把表单字段转成工单 payload |

如果一条能力同时跨多类，应拆成多条 capability，并让它们引用同一组 `linkedSkills`。

## 失败策略

单条 todo 失败不影响其他 todo：

```json
{
  "todoId": "e_bad_secret_001",
  "status": "failed",
  "reason": "发现疑似凭据值，请通过右侧表单重新提交。"
}
```

整体 callback 状态：

- 全部成功：`success`
- 成功与失败混合：`partial`
- 全部失败或出现全局安全阻断：`failed`

## 回传摘要要求

`user_summary` 应说明：

- 配置了哪个系统
- 配置了哪类能力
- 字段或凭据是否还需在表单补齐

`user_summary` 不应说明：

- 绝对路径
- 内部 hook / orchestrator
- 真实凭据值
- 原始错误栈
