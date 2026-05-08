# Workflow Todo 规范

## 总原则

- 只创建新 schema 的 workflow todo
- 不创建旧 handoff todo
- todo 是业务缺口或诊断缺口，不是阶段占位物

## `notes` 字段

每条 todo 的 `notes` 必须是 JSON 对象。

### 通用字段

```json
{
  "stage": "skill",
  "kind": "gap",
  "status": "open",
  "source": "用户刚补充的业务信息",
  "related_todos": [],
  "related_files": [],
  "created_at": "2026-05-07T10:30:00Z",
  "updated_at": "2026-05-07T10:30:00Z"
}
```

### `gap` todo 必填字段

```json
{
  "stage": "external",
  "kind": "gap",
  "gap_type": "missing_external_config",
  "priority": "required",
  "current_state": "当前还没有 CRM 读订单配置",
  "expected_state": "已形成 CRM 读订单连接能力配置",
  "acceptance_criteria": "存在完整连接能力定义，并完成必要凭据绑定",
  "acceptance_evidence": null,
  "status": "open",
  "source": "用户说明该技能需要查询 CRM 订单",
  "fingerprint": "external:crm-read-order:missing_external_config-001",
  "payload": {
    "connector_type": "mcp",
    "connector_name": "crm-order-reader",
    "operation": "read_order",
    "objective": "读取订单详情与售后状态",
    "credential_slot": "crm-api-token",
    "auth_kind": "api_key",
    "linked_skills": ["skill:return-qualification"]
  },
  "related_todos": ["skill:return-qualification"],
  "related_files": []
}
```

### `diagnosis` todo 必填字段

```json
{
  "stage": "cross_stage",
  "kind": "diagnosis",
  "category": "config_governance",
  "level": "required",
  "question": "是否还有配置变更影响已完成工单？",
  "evidence": "config/AGENTS.md 发生变更",
  "suggested_action": "先完成受影响工单复核",
  "status": "needs_review",
  "source": "diagnostic",
  "related_todos": ["todo_external_001"],
  "related_files": ["config/AGENTS.md"]
}
```

## 状态机

- `open`
- `in_progress`
- `done`
- `needs_review`
- `dismissed`
- `resolved`

不要再使用以下旧状态：

- `drafting`
- `ready_to_dispatch`
- `dirty`
- `dispatched`
- `confirmed`

## 分阶段规则

### 资料阶段

- 不用 todo 代表阶段完成
- 如需记录资料处理动作，可以建辅助 todo，但不能把“资料阶段 required gap todo”当成推进条件

### 技能阶段

- 只有待补充项才建 `stage=skill` + `priority=required` 的 gap todo

### 外部阶段

- 一条 todo 对应一个连接能力单元
- `payload` 必须符合外部阶段契约

### 打包阶段

- 不新增业务 gap todo
- 只记录诊断或复核项
