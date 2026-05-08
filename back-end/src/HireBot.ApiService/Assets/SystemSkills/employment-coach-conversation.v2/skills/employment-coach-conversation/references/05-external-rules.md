# 外部阶段规则

## 1. 连接能力单元

外部阶段不再按旧 handoff 组织，而是按“连接能力单元”组织。

一条 required todo 只对应一个连接能力单元，例如：

- 一个 MCP 调用能力
- 一个 CLI 执行能力
- 一个 database 查询能力

## 2. 必填 payload

每条外部阶段 todo 的 `payload` 必须至少包含：

```json
{
  "connector_type": "mcp",
  "connector_name": "crm-order-reader",
  "operation": "read_order",
  "objective": "读取订单详情与售后状态",
  "credential_slot": "crm-api-token",
  "auth_kind": "api_key",
  "linked_skills": ["skill:return-qualification"]
}
```

字段说明：

- `connector_type`: `mcp` / `cli` / `database` / 其他已定义连接类型
- `connector_name`: 连接能力名称
- `operation`: 执行动作
- `objective`: 这条能力要解决什么问题
- `credential_slot`: 凭据槽位名
- `auth_kind`: 认证方式
- `linked_skills`: 受这条连接能力支撑的技能集合

## 3. 凭据安全

- 凭据值绝不进入聊天消息
- 凭据值绝不写进 todo notes
- 凭据值绝不写进 `external/` 配置文件

教练只描述：

- 需要什么认证方式
- 需要什么字段
- 对应哪个 `credential_slot`

## 4. 跳过规则

如果用户明确说当前流程不需要外部系统：

- 创建 `gap_type=external_skip_declaration`
- 等用户明确确认后再完成
- 一旦完成，外部阶段可标记为 `skipped`

## 5. 打包前检查

外部阶段被视为完成前，服务端会检查：

- 所有 required 外部 todo 已完成
- 每条 todo payload 字段完整
- 需要凭据的连接能力已经完成绑定

如果资料、技能、外部都完成，但还有复核阻塞项，当前阶段应进入 `ready_for_packaging`，而不是回退到业务阶段。
