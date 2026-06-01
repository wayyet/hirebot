# Security And Validation

本文定义 External 阶段在系统提交链路中的安全红线与结构校验规则。

## 凭据红线

禁止在对话、artifact、JSON 产物、README 或日志摘要中落下以下明文：

- token
- password
- api key
- bearer value
- oauth client secret
- 数据库连接串
- webhook secret
- 私钥

允许出现的只有：

- `auth.kind`
- `secretRef`
- `credentialSlot`
- `bindingStatus`
- 已加密的受保护值

## 提交职责边界

真实凭据由系统层通过安全表单和安全存储链路处理。

`external-config` 规范只要求最终结构满足：

- 对话中不出现明文凭据
- `external/` 中不出现明文凭据
- 提交结果用 `external_config_committed` 表示
- `external/` 快照与系统保存状态一致

## 疑似凭据处理

如果需求文本中混入疑似凭据，应：

1. 不在任何 artifact 或持久化 JSON 中回显原文。
2. 提示用户改走右侧安全表单。
3. 将对应配置标记为 `partial` 或 `failed`，由系统层给出安全提示。

## 字段校验

普通 capability 至少满足：

- `category` 属于 `read/write/notify/search/transform`
- `objective` 非空
- `target_system` 非空
- `integration_methods` 至少一项
- `linked_skills` 至少一项
- `auth_kind` 明确

skip capability 至少满足：

- `kind = skip`
- `reason` 非空

## 完成校验

系统层发出 `external_config_committed` 前，应确认：

- 提交模式是 `configured` 或 `skipped`
- 敏感字段已保护
- 统一状态已持久化
- `external/` 可由该状态稳定重建
