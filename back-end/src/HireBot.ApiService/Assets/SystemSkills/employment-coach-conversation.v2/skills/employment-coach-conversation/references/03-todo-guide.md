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

## gap_type 枚举

创建 gap todo 时，`gap_type` 必须是以下枚举值之一：

| 阶段 | gap_type | 含义 |
|---|---|---|
| material | `missing_upload` | 缺少业务资料上传 |
| material | `unclassified_upload` | 已上传但未分类 |
| material | `ontology_slice` | 需要补充本体切片 |
| material | `insufficient_coverage` | 资料覆盖不足 |
| skill | `missing_skill_definition` | 缺失技能定义 |
| skill | `incomplete_skill_fields` | 技能字段不完整 |
| skill | `skill_ontology_gap` | 技能超出本体覆盖 |
| skill | `skill_boundary_conflict` | 技能边界冲突 |
| external | `missing_external_config` | 缺失外部连接配置 |
| external | `external_skip_declaration` | 声明跳过外部系统 |
| external | `unlinked_external` | 外部能力未关联技能 |
| external | `missing_credential_slot` | 缺少凭据槽位 |

## 分阶段规则

### 资料阶段

- 不用 todo 代表阶段完成
- 如需记录资料处理动作，可以建辅助 todo，但不能把”资料阶段 required gap todo”当成推进条件

### 技能阶段

- 只有待补充项才建 `stage=skill` + `priority=required` 的 gap todo
- `gap_type` 从 `missing_skill_definition` / `incomplete_skill_fields` / `skill_ontology_gap` / `skill_boundary_conflict` 中选择最匹配的

### 外部阶段

- 一条 todo 对应一个连接能力单元
- `payload` 必须符合外部阶段契约
- `gap_type` 从 `missing_external_config` / `external_skip_declaration` / `unlinked_external` / `missing_credential_slot` 中选择

### 打包阶段

- 不新增业务 gap todo
- 只记录诊断或复核项
