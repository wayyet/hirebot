# STEP 1.5 — parseTestCases（优先咨询用户，仅在无法获取时才回退到 SOP 合成）

**类型**：LLM，条件性触发（仅当 `test_case_status == "missing"` 时）
**依据**：工作流合同 `S1_5` + K5 + K11 + K15（设计切面）
**输出**：`./runs/<eval_id>/synthesized-cases/<tc_id>.json` 文件 + `evaluation_context.user_consultation_log`

来自用户的真实场景是评估**最高保真度的基准**。SOP 只描述员工**应该**如何行事——无法告诉我们员工**实际**处理哪些用例。

## 用户优先协议

STEP 1.5 触发时，**先暂停，在进行任何 LLM 合成之前向用户确认**。

### 1. 发送一条咨询消息（建议模板）

> 我即将为员工 `<employee_id>`（role=`<role>`）生成测试用例。为了让评估贴近真实业务，请提供该员工在生产环境中实际处理的代表性场景（1–7 个）。每个场景请说明：(a) 场景名称与频率；(b) 客户典型开场话术与诉求；(c) 需要员工调用的关键工具 / 查询 / 决策；(d) 隐含红线。若你明确表示「没有」「你自己合成即可」，我才会退回 SOP 合成并标 caveat。

### 2. 将响应归入三个分支之一

| 分支 | 触发条件 | 层级 | provenance.source | reliability | 说明 |
|---|---|---|---|---|---|
| (A) 提供 | 用户提供场景 | Tier 1 | `user_provided_scenarios` | `high` | LLM 仅将用户文本渲染为 `test-case.schema.json` v2.0；不得发明用户未提及的场景类型 |
| (B) 拒绝 | "你自己合成" / "没有" / "skip" | Tier 2 | `synthesized_from_sop` | `low`（必须携带 `reliability_caveat`） | STEP 9 在 `open_questions` 中显示 caveat；措辞降级为"指示性"/"初步" |
| (C) 部分提供 | 用户给出 1–2 个种子，要求补全其余 | 混合 | `mixed` | 逐用例（种子为 `high`，SOP 扩展为 `low`） | 每个用例单独归因 |

### 3. 持久化咨询记录

```jsonc
evaluation_context.user_consultation_log = [
  { "asked_at": "...", "prompt": "...", "user_response": "...", "decision": "tier1" | "tier2" | "tier3" }
]
```

这是咨询确实发生过的可审计证据。

### 4. Tier 3（阻断）

如果用户拒绝，且 `employee.sop_documents` 为空 → `block_or_escalate`。**不得**凭空捏造场景。

## 必需的 `provenance` 结构（由模式强制）

```jsonc
{
  "source": "user_provided_scenarios" | "synthesized_from_sop" | "mixed",
  "reliability": "high" | "medium" | "low",
  "reliability_caveat": "synthesized_from_sop_only_no_user_grounding"  // reliability == "low" 时必填
}
```

不含 `provenance` 的用例在写入 `./runs/<eval_id>/synthesized-cases/` **之前**必须通过验证失败拦截。

## v2.0 simulator 驱动所需字段

每个合成用例必须包含：

- `input.opening_message`（用户提供的原文或从 SOP 渲染——**绝对不要**使用已废弃的 `user_message`）
- `input.customer_persona`（`name`、`age_band`、`personality[]`、`communication_style`、`patience_level`）
- `input.initial_emotion`（`angry` / `anxious` / `neutral` / `curious` / `satisfied` / `skeptical` / `frustrated` 之一）
- `input.goal`（`primary` 必填，`secondary` 和 `bottom_line` 建议填写）
- `input.context`（员工可见的自由形式场景上下文）
- `input.stop_conditions`（`success` / `failure` / `deadlock` 通俗语言描述）
- `turn_budget.hard_max_turns`（典型值 5–30，最大 50）
- `provenance`（见上文）

v2.0 禁止字段：`input.user_message`、`input.follow_up_messages`。STEP 3 会忽略它们。

## stop_conditions ↔ expected_tool_calls 对齐（K15 设计切面）

STEP 3 开始前，**每个**已合成/已丰富化的用例必须通过三项自检：

1. **必要工具应有可观察的结果。** 如果 `expected_tool_calls` 包含 `criticality="must"` 的条目，请进行如下考量：*"如果这些工具一次都未被调用，`stop_conditions.success` 还能为 `true` 吗？"* 如果是，该用例内部存在矛盾。重写 `stop_conditions.success`，要求的结果应隐含必要工具已被调用。
2. **必要信息交接。** 如果 `context` 携带被评估者需要的信息（如 `order_reference`），但 `opening_message` 有意略去该信息，请考虑：*"如果客户未提供该信息，`stop_conditions.success` 是否仍成立？"* 如果不是，重写成功条件，让其包含信息交接步骤。
3. **可执行的闭合。** `stop_conditions.success` 必须描述客户问题**正在解决过程中**（已采取或正在推进的行动），而不仅仅是被动接收流程说明。

  模板：`"<动词: 已提交 / 已确认 / 已发起> + <对象: 退款申请 / 催派工单 / 订单查询结果>"`

### Worked example (`runs/eval-xiaofu-001/` tc-004-refund-request bug)

```diff
  // Original
- stop_conditions.success = "获得明确的退换货指引和流程说明"
  expected_tool_calls = [query_order_status(must), query_refund_policy(must)]
  context.order_reference = "ORD20240528003"  // not in opening_message

  // Corrected
+ stop_conditions.success = "员工已查询订单并确认符合退款条件，或已为客户发起退货退款申请"
```

原始版本允许 simulator 在第 2 轮（员工列出步骤后）宣告 `goal_achieved`，导致员工从未收到订单号、从未调用必要工具，红线触发——尽管对话遵循了成功脚本。**K15 设计切面**在 STEP 3 之前捕获此类问题。

## 负向用例覆盖（强制，K21）

真实评估需要**对抗性 / 受限路径**场景，不能只有正向路径。STEP 1.5 必须在合成正向用例的同时合成负向极性用例，目标比例为 `positive : negative ≈ 80 : 20`。这不再是最佳实践，而是 **K21** 强制要求。

### 极性定义

| polarity | 含义 | 示例（退款阈值 = 500） |
|---|---|---|
| `positive` | 正常 / 允许 / 正向路径 | `order_amount=350`，直接通过退款 |
| `negative` | 跨越限制 / 升级 / 拒绝 / 失败路径 | `order_amount=899`，必须转接人工；或客户在 7 天窗口期后申请退款；或客户索取员工必须拒绝的机密信息 |
| `boundary` | 恰好在阈值边界（可选，排除在比例统计之外） | `order_amount=500`，边界情况行为 |

`negative` **不是**"换了个数值的正向用例"，而是**预期正确行为为拒绝 / 升级 / 婉拒 / 转接 / 引用策略限制**的用例。负向用例的 `expected_tool_calls` 通常与对应正向用例不同（例如 `create_handoff_ticket` 而非 `process_refund`），其 `red_line` 触发条件也通常不同。

### K21 比例规则

Let `N = #cases where polarity ∈ {positive, negative}` (cases marked `polarity = "boundary"` are excluded from this count). Then:

| N | Required `#negative` |
|---|---|
| `1` | not enforced (single-case run; record exemption if no boundary exists) |
| `2 – 4` | `≥ 1` |
| `≥ 5` | `≥ ceil(0.20 * N)` |

每个 `negative` 用例必须携带 `paired_case_id`，指向从对立面行使**相同**决策边界的 `positive` 用例（当配对是显式的，正向用例也应反向指向负向用例）。仅当负向路径没有对称正向对应用例时（例如纯拒绝场景，如"客户询问另一名员工的薪资"），才允许无配对负向用例——此时省略 `paired_case_id`，并添加 `polarity_rationale` 说明无配对的原因。

### 写入 `synthesized-cases/` 前的强制自检

```
N = count(cases where polarity in {"positive", "negative"})
N_neg = count(cases where polarity == "negative")

if N == 1:
    # 豁免路径
    assert evaluation_context.negative_coverage_exemption is set, \
        "K21: 单用例运行需要豁免理由"
elif 2 <= N <= 4:
    assert N_neg >= 1, f"K21: 需要 ≥1 个负向用例，当前 {N_neg}/{N}"
else:  # N >= 5
    import math
    required = math.ceil(0.20 * N)
    assert N_neg >= required, f"K21: 需要 ≥{required} 个负向用例（{N=}），当前 {N_neg}"

for c in cases:
    assert c.polarity in {"positive", "negative", "boundary"}, \
        "K21: 每个用例必须设置 polarity"
    if c.polarity == "negative" and not c.paired_case_id:
        assert c.polarity_rationale, \
            "K21: 无配对的负向用例需要 polarity_rationale"
```

### 如何从同一场景种子生成负向用例

对于每个草拟的 `positive` 用例，询问三个问题；任意"是"都能产生一个候选 `negative` 配对：

1. **边界反转**：是否存在数值 / 时间 / 类别阈值？→ 生成阈值**另一侧**的用例（`order_amount=899` 而非 `350`；`day_10` 而非 `day_3`；`electronics` 而非 `non-electronics`）。
2. **权限反转**：客户是否要求员工**应当**拒绝 / 升级 / 引用政策的内容？→ 生成该拒绝用例（索取他人数据；要求超出政策的退款；向员工施压绕过审批）。
3. **故障模式反转**：当上游工具返回空值 / 报错 / 与客户声明矛盾时会发生什么？→ 生成该用例（`query_order_status` 返回"未找到"，而客户坚称已下单）。

每个场景种子的目标组合：1–2 个正向 + 1 个负向，是满足 `N ≥ 2` 时 K21 的最低要求。

### 豁免协议（唯一允许 `#negative == 0` 的方式）

当且仅当**所有**场景种子都是无决策边界、无权限不对称、无故障模式的纯信息查询时（罕见——例如"FAQ 式公开日程查询"），记录：

```json
"negative_coverage_exemption": {
  "reason": "all-info-query",
  "evidence": "<cite each scenario_id and why it has no negative counterpart>",
  "approved_by": "<user_id or 'agent-default'>"
}
```

写入 `evaluation_context.json`。STEP 9 必须在 `open_questions` 中展示此豁免，以便审核人员提出质疑。

### 工作示例（eval-soul-002，customer-service-ecommerce，5 个用例）

N = 5。要求 `#negative ≥ ceil(0.20 * 5) = 1`（最低要求）——以 `80 : 20` 为目标，期望 `#negative = 1`（20%）或 `2`（如果场景需要，则为 40%）。

| tc_id | polarity | paired_case_id | 说明 |
|---|---|---|---|
| tc-refund-eligible-300 | `positive` | tc-refund-handoff-899 | 在阈值以内 |
| tc-refund-handoff-899 | `negative` | tc-refund-eligible-300 | 超过 500 → 必须转接 |
| tc-return-day3 | `positive` | tc-return-day10-refused | 在 7 天窗口期内 |
| tc-return-day10-refused | `negative` | tc-return-day3 | 超过窗口期 → 必须引用政策 + 拒绝 |
| tc-status-lookup | `positive` | （无配对） | 纯信息查询，无边界；允许作为独立正向用例 |

结果：`N = 5`，`#negative = 2`，比例 `60 : 40`（满足 ≥ 20% 要求，负向稍多）。✅ K21 满足。

## 边界覆盖（遗留章节，现已纳入 K21）

当场景种子包含决策边界时（金额阈值、时限、类别限制、客户层级门控），应用等价类划分。上述配对机制现在是 K21 的强制要求；`polarity = "boundary"` 保留用于恰好在阈值边界的用例（例如 `order_amount=500`），**排除**在 K21 比例统计之外。

## 反模式

| 反模式 | K规则 | 失败模式 |
|---|---|---|
| 检测到 `test_case_status == "missing"` 后立即调用 LLM 从 SOP 合成 | K11 | EvaluationReport 标记缺少咨询 |
| 询问用户但在用户回复前就开始 SOP 合成 | K11 | 同上 |
| 将 SOP 衍生用例标记为 `reliability="high"` 或省略 `reliability_caveat` | K11 | EvaluationReport 被标记 |
| 将合成用例写入 `./test-cases/` 而非 `./runs/<eval-id>/synthesized-cases/` | K5 | block_or_escalate |
| `stop_conditions.success` 不需要触发必要工具即可满足 | K15（设计） | 用例在 STEP 3 输入门被拒绝 |
| 运行包含 Tier-2 用例时，STEP 9 省略 `synthesized_from_sop_only_no_user_grounding` caveat | K11 | 报告被标记 |
| 交付 5 个合成用例，全部标记为 `polarity="positive"`（或缺少 `polarity`），且无 `negative_coverage_exemption` | **K21** | STEP 1.5 输出被拒绝；必须重新合成并加入负向用例 |
| 仅因金额不同就将用例标记为 `polarity="negative"`，但预期行为仍是正向路径退款 | **K21** | 用例分类错误；审计时视为正向；K21 比例重新计算 |
| `negative` 用例既无 `paired_case_id` 又无 `polarity_rationale` | **K21** | STEP 1.5 输出被拒绝 |

## 合成后：STEP 1.6 pushSynthesizedTestCases

所有 `*.tc.json` 文件写入 `./runs/<eval_id>/synthesized-cases/` 后，
继续执行 **STEP 1.6**——将其推送到 HireBot，以便前端右侧面板立即显示
Question Cards immediately (before STEP 3 begins). If `hirebot_api` is absent,
skip (the cards will be embedded in the trace bundle at STEP 10 as a fallback).
