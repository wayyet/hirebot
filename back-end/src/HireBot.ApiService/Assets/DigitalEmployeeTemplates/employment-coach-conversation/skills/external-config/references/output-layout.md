# Output Layout

本文定义系统层生成的 `external/` 目录布局。

`external-config` 负责定义结构，不负责真实落盘；真实写入由系统层在提交成功后执行。

## 目录结构

```text
external/
  external-config.index.json
  systems/
    <system-slug>.json
  capabilities/
    <capability-id>.json
  README.md
```

## capability 文件

每条外部能力对应一个 `external/capabilities/<capability-id>.json`。

建议字段：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_capability",
  "capabilityId": "e_example_read_order_001",
  "kind": "normal",
  "category": "read",
  "objective": "在业务处理中读取 CRM 订单详情",
  "targetSystem": {
    "name": "Example CRM",
    "slug": "example-crm"
  },
  "integrationMethods": ["mcp"],
  "linkedSkills": ["order-status-query"],
  "auth": {
    "kind": "api_key",
    "secretRef": "EXTERNAL_EXAMPLE_CRM_API_KEY",
    "credentialSlot": "example-crm-api-key",
    "bindingStatus": "bound"
  },
  "fields": {
    "required": ["order_id", "status"],
    "mapping": []
  },
  "status": "configured"
}
```

要求：

- 不写明文凭据。
- `auth` 里只保留受保护值或安全引用。
- skip 场景仍使用同一路径，只是 `kind = skip` 并带上原因。

## system 文件

同一外部系统的多条 capability 聚合到 `external/systems/<system-slug>.json`。

建议字段：

```json
{
  "schemaVersion": "1.0.0",
  "artifactType": "external_system",
  "name": "Example CRM",
  "slug": "example-crm",
  "integrationMethods": ["mcp"],
  "authKinds": ["api_key"],
  "credentialSlots": ["example-crm-api-key"],
  "capabilities": [
    {
      "capabilityId": "e_example_read_order_001",
      "category": "read",
      "path": "external/capabilities/e_example_read_order_001.json"
    }
  ]
}
```

## index 文件

`external/external-config.index.json` 是诊断、共享和最终打包的主入口。

它应至少列出：

- 本次配置的 `submissionMode`
- 对应的 `external_config_committed` 来源信息
- 所有 `system` 路径
- 所有 `capability` 路径
- 所有 skip 记录
- 校验摘要

所有路径都必须使用工作区内相对路径。

## README

`external/README.md` 面向人工审核，应只说明：

- 配置了哪些系统
- 每个系统有哪些能力
- 哪些字段或凭据槽位仍待补齐
- 安全提醒

不得写明文 endpoint、Token、密码、API Key 或连接串。
