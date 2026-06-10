# STEP 1.2 — curateMetrics（LLM，有界 + 可审计）

**类型**：LLM 有界且可审计
**依据**：工作流合同 `S1_2` + K9（已重写）+ K18（位于 `metric-selection.workflow-contract.projection.json`）
**运行时机**：STEP 1（`candidate_metrics`）之后，STEP 1.5 / STEP 2 之前
**输入**：`candidate_metrics`、`metric_registry`、`employee.industry`、`employee.role.responsibility_tags`、`employee.job_responsibilities`、`metric_selection_policy`
**输出**：`evaluation_context.selected_metrics`、`evaluation_context.curate_log[]`、追加的 `user_consultation_log` 条目

STEP 1.2 通过推断员工的实际行业和职责，以及每个指标的语义字段，来细化确定性角色过滤结果。它可以**移除**误匹配的字符串匹配项，并**添加**语义上正确但被漏掉的指标。

> **K9 方程式。** `selected_metrics = (candidate_metrics − removed) ∪ added`。`candidate_metrics` 保持确定性且机器可验证；`removed` / `added` 由 LLM 生成，但每个决策均通过 `curate_log` 进行审计（K18）。

## 调用门限

```
mode == "never"   → 跳过；selected_metrics = candidate_metrics
mode == "always"  → 无条件运行（即使 size-trigger 评估出错）
mode == "auto"    → 当  len(candidate_metrics) < size_triggers.candidate_count_lower_bound (默认 3)
                          或 len(candidate_metrics) > size_triggers.candidate_count_upper_bound (默认 15)
                    时运行；否则跳过，selected_metrics = candidate_metrics
```

默认值（省略 `metric_selection_policy` 或任何字段时）：`mode=auto`、`max_metrics=8`、`min_dimensions_covered=1`、`auto_apply_threshold=0.7`、`size_triggers={3,15}`。

## 筛选算法

### 1. 构建筛选提示词（一次 LLM 调用）

输入给 LLM 的切片：
- `employee.{industry, role.responsibility_tags, job_responsibilities}`
- `candidate_metrics[*].{metric_code, description, tags, industry, responsibility_tags}` — the keep/remove pool
- `(metric_registry − candidate_metrics)[*].{same fields}` — the addable pool
- `metric_selection_policy`

### 2. LLM emits structured decisions

```jsonc
{
  "removed": [ { "metric_code": "...", "decision": "removed", "evidence": [...], "confidence": 0.0-1.0 } ],
  "added":   [ { "metric_code": "...", "decision": "added",   "evidence": [...], "confidence": 0.0-1.0 } ]
}
```

- `removed[] ⊆ candidate_metrics`（语义上不适合的字符串匹配项）
- `added[] ⊆ (metric_registry − candidate_metrics)`（字符串匹配遗漏的）
- 两个数组必须不相交
- `len(removed) + len(added) ≤ 2 × len(metric_registry)`

### 3. 确定性后处理（编排器，非 LLM）

1. 验证子集 + 不相交约束。违规 → 失败处理（见下文）。
2. 通过用户确认解决低置信度添加项（见置信度门限）。
3. 计算 `selected_metrics = (candidate_metrics − removed) ∪ confirmed_adds`。
4. 执行边界约束：
   - `len(selected_metrics) > max_metrics` → `block_or_escalate` + curate_log 条目，注明观察值与配置值。
   - 不同 `parent_dimension` 数 `< min_dimensions_covered` → `block_or_escalate`。
5. 持久化 `curate_log` 并验证 K18（每个决策都有证据引用）。

## 置信度门限（R13）

| 置信度 vs `auto_apply_threshold` | 需要用户确认？ | 是否纳入？ | `confirmed_by_user` |
|---|---|---|---|
| `>= threshold`（默认 0.7） | 否 | 是 | `"auto_applied"`（字符串） |
| `< threshold`，用户确认 | 是 | 是 | `true`（布尔值） |
| `< threshold`，用户拒绝 | 是 | 否 | `false`（布尔值） |
| `< threshold`，300秒超时 | 是 | 否 | `false`（布尔值）+ 超时已记录 |

多个低置信度添加项按 **curate_log 顺序逐个进行提示**（R13.6）。每个提示 + 响应均持久化到 `evaluation_context.user_consultation_log`，使用与 K11 咋问日志相同的记录格式。

## 证据引用（K18）

每个 `removed` / `added` 决策必须携带 ≥1 条证据引用：

```jsonc
"evidence": [
  { "source_field": "employee.job_responsibilities", "quote": "handles refund disputes" }
]
```

- `source_field` ∈ { `employee.industry`, `employee.job_responsibilities`, `employee.role.responsibility_tags`, `metric.description`, `metric.tags`, `metric.industry`, `metric.responsibility_tags`, `metric.complementary_metrics`, `metric.exclusive_with` }
- `quote` is a verbatim (case-sensitive, contiguous), ≥1-char substring of that field's actual value in the run's data.
- `len(curate_log) == len(removed) + len(added)`; each `(removed ∪ added)` metric_code appears in exactly one entry.

A decision with empty evidence, a missing curate_log entry, or a citation that fails the source-field-and-substring check → **K18 taint** (see `tainted-run-lifecycle.md`).

## Failure handling — degrade to candidate (safety property)

以下任意情况：筛选器失败、输出格式错误、子集约束违规、输入缺失/null，或 30 秒超时 →
**回退至 `selected_metrics = candidate_metrics`** + 标明失败类别的 `open_question`；运行继续。

> **这是 STEP 1.2 最重要的属性（设计决策 #1 / 正确性属性 10）。** 最坏情况下，STEP 1.2 不起作用，评估在确定性角色过滤结果上运行——与当前行为完全相同。添加 STEP 1.2 只能精化或不起作用，永不会降级。

## 向后兼容（R16.4/16.5）

- 仅有 `selected_metrics`（无 `candidate_metrics`）的遗留上下文 → 将 `selected_metrics` 视为 `candidate_metrics` + `open_question`（`legacy_selected_metrics_treated_as_candidate_metrics`）。
- 同时包含 `selected_metrics` 和 `candidate_metrics` 的上下文 → `candidate_metrics` 优先 + `open_question`（`legacy_selected_metrics_ignored_in_favor_of_candidate_metrics`）。

## 工作示例

`employee.role.role_id = customer-service-ecommerce`，`industry = ecommerce`，`job_responsibilities = "处理售前咨询、退款、物流投诉，无需撰写正式文档"`。STEP 1 产生 10 个候选项（3 个角色特定 + 7 个通用）。假设未来的注册表也错误地将 `bid_clause_completeness` 字符串匹配到此角色（实际没有，仅举例说明）：

- **removed（移除）**：`bid_clause_completeness` — 证据 `{source_field: "employee.job_responsibilities", quote: "无需撰写正式文档"}`，置信度 0.9 → 自动应用。
- **added（添加）**：无（7 个通用项已涵盖跨角色关注点）。
- `selected_metrics` = 10 个候选（此处未变）；`curate_log` 包含该移除记录。

## 反模式

| 反模式 | K规则 | 失败模式 |
|---|---|---|
| 决策中 `evidence` 为空 | K18 | 污染 |
| Batch-fabricate decisions without per-decision evidence | K18 | taint |
| `selected_metrics` 超过 `max_metrics` | R12.8 | block_or_escalate |
| 未经用户确认静默纳入低置信度添加项 | R13.1 | 未审计注入 |
| 筛选器失败时阻断整个运行而非降级为候选 | R10.8 / CP10 | 违反安全属性 |
| STEP 1.2 写入 `employee.role.role_id` | K17 / R6.5 | unauthorized_role_id_mutation |
| 批量捏造决策而不提供逐条证据 | K18 | 污染 |
