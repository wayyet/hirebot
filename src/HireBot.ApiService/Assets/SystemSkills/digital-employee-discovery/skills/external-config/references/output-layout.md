# Output Layout

`external-config` 只写当前沙箱的 `external/` 目录。

## 目录结构

```text
external/
  external-config.index.json
  systems/
    <system-slug>.json
  capabilities/
    <todo-id>.json
  README.md
```

## capability 文件

每条 external todo 对应一个 `external/capabilities/<todo-id>.json`。普通能力使用 `kind: normal`，跳过外部系统使用 `kind: skip`。

建议字段：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_capability",
  "todoId": "e_xiaoshouyi_read_order_001",
  "kind": "normal",
  "category": "read",
  "objective": "在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型",
  "targetSystem": {
    "name": "销售易 CRM",
    "slug": "xiaoshouyi-crm"
  },
  "linkedSkills": ["s_seven_day_init_001"],
  "auth": {
    "kind": "API Key",
    "secretRef": "EXTERNAL_XIAOSHOUYI_CRM_API_KEY",
    "credentialSlot": "xiaoshouyi-crm-api-key",
    "bindingStatus": "pending",
    "value": null
  },
  "fields": {
    "required": ["order_id", "created_at", "status"],
    "mapping": []
  },
  "acceptance": "external/ 中包含可调用的销售易订单读取配置 + 字段映射",
  "sourceDigest": "用户说明退货资格初判需要查 CRM 订单",
  "status": "draft",
  "validation": {
    "passed": true,
    "warnings": []
  }
}
```

`auth.value` 必须固定为 `null` 或完全省略；不得写真实凭据。

skip artifact 使用同一路径，建议字段：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_capability",
  "todoId": "e_external_skip_001",
  "kind": "skip",
  "reason": "用户明确表示不需要外部系统",
  "sourceDigest": "用户说明本阶段先不接外部系统",
  "status": "recorded",
  "validation": {
    "passed": true,
    "warnings": []
  }
}
```

## system 文件

同一 `target_system` 的多条 capability 应合并进一个 `external/systems/<system-slug>.json`。

建议字段：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_system",
  "name": "销售易 CRM",
  "slug": "xiaoshouyi-crm",
  "authKinds": ["API Key"],
  "credentialSlots": ["xiaoshouyi-crm-api-key"],
  "capabilities": [
    {
      "todoId": "e_xiaoshouyi_read_order_001",
      "category": "read",
      "path": "external/capabilities/e_xiaoshouyi_read_order_001.json"
    }
  ],
  "securityNotes": [
    "真实凭据必须通过安全表单和安全存储通道提供。"
  ]
}
```

## index 文件

`external/external-config.index.json` 是诊断和实例打包的主入口。

它应列出：

- 本次配置覆盖的 todo id
- 所有 target system
- 所有 capability artifact path
- skip 记录，至少包含 `todoId`、`reason`、`path`、`status`
- validation 摘要
- 是否发现安全阻断

路径必须使用沙箱内相对路径，不要写绝对路径。

## README

`external/README.md` 面向人工审阅，内容保持短：

- 当前接入了哪些系统
- 每个系统有哪些能力
- 哪些字段还需要接口映射或表单凭据
- 安全提醒

README 不得包含真实 endpoint token、密码、API Key 或连接串。
