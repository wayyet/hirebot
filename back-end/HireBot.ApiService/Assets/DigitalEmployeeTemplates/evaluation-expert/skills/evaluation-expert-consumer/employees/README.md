# employees/

热加载的**员工权威档案**数据层（第 6 层，与 `./metrics/`、`./test-cases/`、`./runtime-drivers/`、`./simulators/`、`./role-catalog/` 并列）。

STEP 0 `resolveEmployee` 的**最高优先级**解析源。当 `./employees/<employee_id>.json` 存在时，STEP 0 直接用它，不再走用户对话或 LLM 推断（`employee_provenance.source = authoritative_file`, `reliability = high`）。

## 约定

- **一员工一文件**：`<employee_id>.json`，文件名（不含 `.json`）必须等于 `employee_id`
- **schema**：每个文件遵循 [`employee.schema.json`](../runtime-schemas/employee.schema.json)
- **新增方式**：往本目录放新文件即可生效，不需要改任何 `*.projection.json`（hot-plug rule，R15）
- **覆盖路径**：默认本目录；运行时可通过环境变量 `EVALUATION_EMPLOYEES_DIR` 指向其他路径

## 字段速览

| 字段 | 必填 | 说明 |
|---|---|---|
| `employee_id` | ✓ | 机器标识，与文件名一致 |
| `role_id` | ✓ | 规范化前的角色串；STEP 0 会拿它去 role-catalog 匹配 `role_id` + `aliases`，产出权威 `employee.role.role_id` |
| `industry` | ✓ | 行业；供 STEP 1.2 指标裁定 |
| `job_responsibilities` | ✓ | 自由文本，描述该员工实际日常职责；STEP 1.2 add/remove 决策的主要语义信号 |
| `scenarios` | ✓ | 场景标签数组（≥1）；驱动 STEP 2 场景匹配 |
| `level` | – | 资历（employee / supervisor / manager / ...） |
| `sop_documents` | – | SOP 引用，供 STEP 1.5 缺用例时合成 |

## 解析优先级（STEP 0）

```
employees/<id>.json 存在？
  ├─ 是 → 加载 + 校验 → source=authoritative_file, reliability=high
  │        └─ 解析/校验失败 → block_or_escalate（不静默回退）
  └─ 否 → 用户提供了描述？
           ├─ 是 → LLM 解析草稿 → 展示给用户确认 → source=user_dialog, reliability=high
           └─ 否 → LLM 推断兜底 → source=inferred_fallback, reliability=low
                                    + caveat=employee_inferred_no_authoritative_source
```

## Fail-soft 规则

| 情况 | 处理 |
|---|---|
| 文件 JSON 解析失败或 schema 校验失败 | `block_or_escalate`（员工档案是权威源，坏了不能静默猜测） |
| 多个文件（极少见）— 文件名即 id，天然唯一 | 不适用 |
| `employee_id` 含路径分隔符或为空 | `block_or_escalate`，cause=`employee_id_invalid` |

注意：与 role-catalog 的"坏文件跳过继续"不同，员工档案解析失败是 **block**，因为它是评估对象的权威身份来源，错了会让整次评估失去意义（K17 的精神）。

## 当前内置档案

- `emp-cs-demo-001.json`：演示用电商客服员工，`role_id` 故意写成中文别名"电商客服"以演示 STEP 0 规范化（→ `customer-service-ecommerce`）。

## 与运行时的关系

本目录是**数据层**，不属于任何 producer 契约（员工档案是部署方私有数据，不是可发布的本体）。schema 在 `runtime-schemas/employee.schema.json`，因为它描述的是单次运行消费的数据形状，而非跨 skill 的契约词汇。
