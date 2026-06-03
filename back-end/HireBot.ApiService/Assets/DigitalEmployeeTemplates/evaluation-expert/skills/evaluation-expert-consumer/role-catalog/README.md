# role-catalog/

热加载的**角色权威目录**（第 5 层数据层，与 `./metrics/`、`./test-cases/`、`./runtime-drivers/`、`./simulators/`、`./employees/` 并列）。

由新的 producer 契约 `contracts/projections/role-ontology/role-catalog/` 治理。STEP 0 `resolveEmployee` 用它把用户口述的自由角色串规范化成权威 `role_id`，并把 `industry` / `responsibility_tags` 注入 `employee.role`，供 STEP 1.2 指标裁定使用。

## 约定

- **一角色一文件**：`<role_id>.role.json`，文件名（不含 `.role.json`）必须等于 `role_id`
- **schema**：每个文件遵循 [`role-catalog-entry.schema.json`](../contracts/projections/role-ontology/role-catalog/schemas/role-catalog-entry.schema.json)
- **新增方式**：往本目录放新文件即可生效，不需要改任何 `*.projection.json`（hot-plug rule，R15）
- **覆盖路径**：默认本目录；运行时可通过环境变量 `EVALUATION_ROLES_DIR` 指向其他路径
- **加载时机**：PRE.A `loadRoleCatalog`（STEP 0 之前），确定性、内联

## 字段速览

| 字段 | 必填 | 说明 |
|---|---|---|
| `role_id` | ✓ | 机器标识，与文件名一致；`^[a-z0-9-]{1,64}$` |
| `industry` | ✓ | 行业标签，`^[a-z0-9_]{1,64}$` |
| `responsibility_tags` | ✓ | 职责标签数组（1–32 去重），与 `metric.responsibility_tags` 同词表 |
| `parent_role` | – | 父角色 `role_id` 或 `null`，触发继承 |
| `aliases` | – | 别名（中英文变体），STEP 0 大小写不敏感匹配用 |
| `display_names` | – | 展示名变体 |
| `recognized_levels` | – | 该角色额外认可的 `employee.role.level` 取值 |

## 继承（parent_role）

声明 `parent_role` 时，loader 按以下规则继承：

- `industry`：子覆盖父（子声明则子赢，否则取父值）
- `responsibility_tags`：集合并集去重（子 ∪ 父）

内置示例：`after-sales-agent` 的 `parent_role = "customer-service-ecommerce"`，因此它在自己声明的 `after_sales` / `warranty_handling` / `escalation_management` 之外，还继承基础客服的 `customer_facing` / `tool_use` / `policy_application` / `complaint_handling` / `order_management`。

## Fail-soft 规则（坏数据不阻断整次评估）

| 情况 | 处理 |
|---|---|
| 父角色不存在 / 继承成环 / 继承链深度 > 8 | 该条目不继承，写 `open_question`，继续 |
| 两个文件声明同一 `role_id` | 保留文件名字典序靠前的，写 `open_question`，继续 |
| 文件 JSON 解析失败或 schema 校验失败 | 跳过该文件，写 `open_question`，继续 |

只有当**被评估员工自己的角色**在目录里匹配不到时，才降级为 caveat（`role_id_no_catalog_entry`，由 STEP 0 处理），而不是阻断。

## 当前内置角色（6 个）

| role_id | industry | parent_role |
|---|---|---|
| `customer-service-ecommerce` | `ecommerce` | – |
| `after-sales-agent` | `ecommerce` | `customer-service-ecommerce` |
| `hr-attendance` | `hr` | – |
| `bid-writer` | `procurement` | – |
| `legal-expert` | `legal` | – |
| `software-engineer` | `engineering` | – |

## 与契约的关系

本目录是**数据层**。**契约层**位于 [`contracts/projections/role-ontology/`](../contracts/projections/role-ontology/)，由它声明 schema、发现规则与治理规则；本目录的实例必须通过 schema 校验。
