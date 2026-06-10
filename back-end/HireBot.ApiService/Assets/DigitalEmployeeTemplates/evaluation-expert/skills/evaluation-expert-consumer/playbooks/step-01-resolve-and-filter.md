# STEP 1 — resolveEmployeeAndCheckTestCases（按角色过滤到 candidate_metrics）

**类型**：确定性
**依据**：工作流合同 `S1` + K9（已重写）+ K10（`metric-selection.workflow-contract.projection.json`）
**输入**：`employee`（来自 STEP 0，含规范化的 `role.role_id`）、`metric_registry`（来自 PRE）、`EVALUATION_TEST_CASES_DIR`
**输出**：`candidate_metrics`、`dropped_metrics`、`test_case_status`

> **因指标精选功能而更改。** 员工解析 + 角色规范化现在在 **STEP 0**（`resolveEmployee`）中进行。STEP 1 不再解析员工——它消费 STEP 0 已规范化的 `employee.role.role_id`。STEP 1 的角色过滤输出现在命名为 **`candidate_metrics`**（STEP 1.2 的确定性输入），而非 `selected_metrics`。STEP 1.2 生成 `selected_metrics = (candidate_metrics − removed) ∪ added`。

STEP 1 有两个职责；均为确定性且内联执行。Agent 在此处**不调用** LLM。

## 职责 A — 探查测试用例

通过检查 `./test-cases/`（或 `EVALUATION_TEST_CASES_DIR`）是否有任何匹配 `employee.role.role_id` + `employee.scenarios` 的用例，来设置 `test_case_status`（`ready` / `missing`）。这只决定 STEP 1.5 是否运行；不影响指标过滤。（员工对象本身——`role`、`scenarios`、`sop_documents`——已由 STEP 0 解析和持久化。）

## 职责 B — 按角色过滤 `metric_registry` → candidate_metrics

对 PRE 加载的每个指标 `m`：

- 若 `employee.role.role_id ∈ m.applicable_roles` 或 `"*" ∈ m.applicable_roles` → 推入 `candidate_metrics`
- 否则 → 推入 `dropped_metrics`，附 `{ metric_code, applicable_roles, drop_reason: "role_mismatch" }`

在 `evaluation_context.json` 中持久化**两个**列表。`candidate_metrics` 是 STEP 1.2 的确定性、机器可验证输入——它**不是**完整注册表，也**不是**最终的 `selected_metrics`。

## 继续前的自检（K9 不变式）

```
assert len(candidate_metrics) + len(dropped_metrics) == len(metric_registry)
assert set(candidate_metrics) ∩ set(dropped_metrics) == ∅
for m in candidate_metrics:
    assert employee.role.role_id in m.applicable_roles or "*" in m.applicable_roles
for m in dropped_metrics:
    assert employee.role.role_id not in m.applicable_roles and "*" not in m.applicable_roles
if len(candidate_metrics) == 0 and len(metric_registry) > 0:
    block_or_escalate("no metric applies to this employee role")  # do NOT proceed to STEP 1.2
```

## 工作示例

`employee.role = "customer-service-ecommerce"`。注册表有 15 个指标：7 个跨角色通用指标（每个角色都获得全部 7 个）+ 8 个角色专属指标。正确的 STEP 1 输出保留 **10** 个指标，丢弃 **5** 个：

| 已选（10 个） | 已丢弃（5 个） |
|---|---|
| `tool_call_correctness`（通过 `*`） | `attendance_rule_compliance` |
| `interaction_empathy` | `bid_clause_completeness` |
| `order_refund_policy_accuracy` | `legal_citation_accuracy` |
| `problem_resolution_completeness`（通过 `*`） | `code_change_risk_disclosure` |
| `response_clarity_and_structure`（通过 `*`） | `confidentiality_boundary_compliance` |
| `response_conciseness`（通过 `*`） |  |
| `factual_accuracy`（通过 `*`） |  |
| `proactive_clarification`（通过 `*`） |  |
| `safety_and_ethics_boundary`（通过 `*`） |  |
| `professional_tone_consistency`（通过 `*`） |  |

将全部 15 个指标复制到 `candidate_metrics` 是 **`runs/eval-xiaofu-001/` 中观察到的 K9 违规模式**——该运行已被污染。

## 跨步骤不变式（K10）

STEP 1.2 将 `candidate_metrics` 细化为 `selected_metrics`；STEP 2 再通过 `applicable_scenarios ∩ tc.scenarios` 进一步收窄。因此对每个丰富测试用例 `tc`：

```
tc.applicable_metrics ⊆ evaluation_context.selected_metrics   （STEP 1.2 的输出）
```

STEP 3 / STEP 4 必须以 `./runs/<eval_id>/enriched-cases/<tc_id>.json` 作为权威来源——**不得**使用嵌入在 `evaluation_context.enriched_test_cases[]` 中的内联副本。两者必须字节相同；任何差异都会污染运行。

## 反模式

| 反模式 | K 规则 | 失败模式 |
|---|---|---|
| 不经角色过滤将完整 `metric_registry` 复制到 `candidate_metrics` | K9 | 运行在 STEP 1 被污染 |
| 跳过持久化 `dropped_metrics`（可审计性漏洞） | K9 | 运行在 STEP 1 被污染 |
| 在 STEP 1 直接输出 `selected_metrics`（跳过 candidate→curate 拆分） | K9 | STEP 1 必须输出 `candidate_metrics`；`selected_metrics` 由 STEP 1.2 负责 |
| 允许 `tc.applicable_metrics` 包含不在 `selected_metrics` 中的指标 | K10 | 运行在 STEP 2 被污染 |
