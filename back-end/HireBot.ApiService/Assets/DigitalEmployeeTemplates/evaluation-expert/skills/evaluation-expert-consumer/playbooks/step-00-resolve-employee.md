# STEP 0 — resolveEmployee（+ PRE.A loadRoleCatalog）

**类型**：PRE.A 确定性（角色目录加载）+ STEP 0 LLM+强制确认（员工解析）
**依据**：工作流合同 `PRE_A` + `S0` + K17（`metric-selection.workflow-contract.projection.json`），role-catalog 投影 K1–K4
**运行时机**：PRE（loadMetricRegistry）之前
**输出**：内存中的 `role_catalog` 映射 + `evaluation_context.employee`（对象形式）+ `employee.employee_provenance` + `evaluation_context.employee_resolution_log`

STEP 0 是解析-规范化的前门。通过将所有角色规范化汇入以权威 Role_Catalog 为支撑的单一步骤，消除了"因角色拼写不同导致评估阻塞"这类失败场景。

## PRE.A — loadRoleCatalog（确定性，内联，无 LLM）

优先运行，以便 STEP 0 可进行规范化。

1. 扫描 `EVALUATION_ROLES_DIR`（默认 `./role-catalog/`）中的 `*.role.json`。
2. 根据 `role-catalog-entry.schema.json` 验证每个文件。构建以 `role_id` 为键的映射。
3. 解析具有非空 `parent_role` 的条目的继承关系：
   - `industry`：子条目覆盖（若子条目声明则子条目胜出；否则继承父条目）。
   - `responsibility_tags`：集合并集去重（子 ∪ 父）。
   - 继承链深度上限 8。
4. 每个错误软失败（永远不阻断运行）：

| 错误 | 处理方式 |
|---|---|
| 文件 JSON 解析/模式验证失败 | 跳过文件 + `open_question` + 继续 |
| `parent_role` 缺失 / 循环 / 深度 > 8 | 加载条目但不继承 + `open_question` + 继续 |
| 两个文件中 `role_id` 重复 | 保留字母顺序最先的文件 + `open_question` + 继续 |

> 只有**被评估者自身角色**的匹配缺失才会降级为说明（STEP 0 规范化未命中），而不是阻断。其他角色的错误目录文件与本次运行无关。

## STEP 0 — resolveEmployee（LLM+强制确认）

### 解析优先级（三个来源，固定顺序）

```
employee_id valid?  (non-empty, no path separator)
  └─ no → block_or_escalate (cause = employee_id_invalid)
  └─ yes ▼
employees/<employee_id>.json exists?
  ├─ YES → load + validate (employee.schema.json)
  │        ├─ ok   → source=authoritative_file, reliability=high
  │        └─ fail → block_or_escalate (parse/schema fail; DO NOT fall through)
  └─ NO  → user supplied a 1..10000-char description?
           ├─ YES → LLM parse → draft {role_id, industry, job_responsibilities, scenarios}
           │        → DISPLAY draft → request confirm | correct | decline
           │            ├─ confirm                      → source=user_dialog, reliability=high
           │            ├─ correct (≤5 rounds)          → apply + re-display + re-ask
           │            └─ decline | 120s timeout | 5-round-exhaust → inferred_fallback
           └─ NO  → inferred_fallback
                     → LLM best-guess (each field a value or "unknown")
                     → source=inferred_fallback, reliability=low,
                       caveat=employee_inferred_no_authoritative_source
                     → open_question listing unknown fields + absent sources
```

备注：
- **权威文件解析失败为阻断，不可降级。** 文件存在但损坏，意味着有人试图提供权威答案但文件已损坏——绕过并猜测比直接停止更糟糕。
- **用户对话需要明确确认。** 绝不静默接受 LLM 对口述描述的解析。展示全部四个字段；只有 `confirm` 才能继续。
- **每轮均需持久化**到 `evaluation_context.employee_resolution_log`（每轮一条：展示的草稿、响应类型 `confirm|correct|decline|timeout`、修正内容、最终确认草稿）。

### 来源对象（K17）

```jsonc
{
  "source":      "authoritative_file" | "user_dialog" | "inferred_fallback",   // required
  "reliability": "high" | "low",                                                // required
  "caveat":      "employee_inferred_no_authoritative_source"                    // required when reliability=low
                 // may also contain / append "role_id_no_catalog_entry" on canonicalization miss
}
```

### 角色规范化（R6）

1. 修剪已解析的自由格式角色字符串。空或空白 → `block_or_escalate`（cause = `role_string_empty`）。
2. 按目录迭代顺序，对每个条目的 `role_id`（不区分大小写精确匹配）及其 `aliases` 进行首次匹配。
3. **命中** → `employee.role.role_id = 匹配到的 role_id`；从条目中将 `industry` + `responsibility_tags` 复制到 `employee.role`。
4. **未命中** → `employee.role.role_id =` 修剪后的自由格式字符串；在 `employee_provenance.caveat` 中追加 `role_id_no_catalog_entry`（去重已有值）。

### 单一写入规则（R6.5）

STEP 0 是唯一允许写入 `employee.role.role_id` 的步骤。如果任何后续步骤的输出会更改该值，则属于 `unauthorized_role_id_mutation` 违规：拒绝写入，保留先前值，并上报错误。Agent 以与 K9/K13 相同的方式进行自我检查。

### employee.role 对象结构（R7）

```jsonc
"role": {
  "role_id":             "customer-service-ecommerce",  // required
  "industry":            "ecommerce",                     // required (empty string allowed = unset)
  "responsibility_tags": ["customer_facing", "tool_use"], // required (empty array allowed)
  "level":               "employee"                        // optional
}
```

### 向后兼容性（R16.2/16.3）

- **纯字符串** `evaluation_context.employee.role`（遗留格式）→ 封装为对象形式：`role_id` = 修剪后的字符串，`industry` = ""，`responsibility_tags` = []，追加 `role_id_no_catalog_entry` 说明。
- `employee.role` **已为有效对象形式** → 保持不变，不追加说明。
- 既非字符串也非有效对象 → `block_or_escalate`（cause = `employee_role_invalid_form`）。
- 封装失败 → `block_or_escalate`（cause = `legacy_role_wrap_failed`），保留遗留文件，需人工迁移。

## 工作示例（演示员工）

`./employees/emp-cs-demo-001.json` 的 `role_id: "电商客服"`（中文别名）。STEP 0：

1. 文件存在 → `source=authoritative_file, reliability=high`
2. 规范化"电商客服" → 匹配 `customer-service-ecommerce.role.json` 的别名 → `employee.role.role_id = "customer-service-ecommerce"`，复制 `industry=ecommerce`，`responsibility_tags=[customer_facing, tool_use, policy_application, complaint_handling, order_management]`
3. 无说明（干净匹配）；STEP 1 按规范化的 `customer-service-ecommerce` 进行角色过滤

## 反模式

| 反模式 | K 规则 | 失败模式 |
|---|---|---|
| LLM 静默接受其对口述描述的解析，不展示给用户 | R2.2 | 用户从未确认；误解传播 |
| 员工文件存在但解析失败时降级到推断回退 | R1.5 | 应为 `block_or_escalate` |
| 后续步骤重写 `employee.role.role_id` | K17 / R6.5 | `unauthorized_role_id_mutation`；污染 |
| 未持久化 `employee_provenance` | K17 | 污染（原子失败） |
| `reliability=low` 但 `caveat` 为空/缺失 | K17 / R4.7 | 污染 |
| 将损坏的角色目录文件视为硬错误 | role-catalog K3 | 应为软失败跳过 + open_question |
