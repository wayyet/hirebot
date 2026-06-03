# metrics/

热加载的评估指标库。

## 约定

- **一指标一文件**：`<metric_code>.metric.json`，文件名（不含 `.metric.json`）必须等于 `metric_code`
- **schema**：每个文件遵循 [`metric.schema.json`](../contracts/projections/metric-ontology/metric-library/schemas/metric.schema.json)
- **新增方式**：往本目录放新文件即可，不需要改契约或代码；评估器在 PRE 阶段（`loadMetricRegistry`）扫描本目录构建 registry
- **覆盖路径**：默认本目录；运行时可通过环境变量 `EVALUATION_METRICS_DIR` 指向其他路径

## 字段速览

| 字段 | 必填 | 说明 |
|---|---|---|
| `metric_code` | ✓ | 机器标识，与文件名一致 |
| `version` | ✓ | semver |
| `display_name` | ✓ | 人类可读名 |
| `parent_dimension` | ✓ | roll-up 到 5 个固定维度之一（见下） |
| `applicable_roles` | ✓ | 适用角色列表；`'*'` 为 match-all wildcard，**不是字面量字符串** |
| `applicable_scenarios` | ✓ | 适用场景列表；`'*'` 同上 |
| `runtime_slice_selector` | ✓ | 切片白/黑名单，决定 fan-out prompt 喂给 LLM 哪些 runtime 数据 |
| `scoring_rubric` | ✓ | `excellent_80_100` / `acceptable_60_79` / `poor_below_60` 三档判分准绳 |
| `aggregation_strategy` | ✓ | 跨场景聚合策略（见下） |
| `default_weight_within_dimension` | – | 在 parent_dimension 内部的默认权重（0–1，默认 1.0） |
| `red_line` | – | 可选；标记该指标为红线判定来源（`trigger_kind` ∈ {`dimension_floor`, `missing_required_signal`, `forbidden_behavior`}） |
| `evidence_signals` | – | 给评分 LLM 的 hint：runtime 数据中哪些片段含证据 |
| `tags` | – | 自由分类标签 |

### 5 个固定 parent_dimension（与 SKILL.md 同源）

`functional_completeness` · `interaction_quality` · `process_compliance` · `problem_resolution` · `tool_call_correctness`

新增 sub-metric 必须 roll-up 到上述五个之一。schema 在 `parent_dimension` 字段使用 `enum` 强约束，写错会被 PRE 阶段 drop。

### `aggregation_strategy` 完整枚举

| 值 | 行为 |
|---|---|
| `worst_case` | 取最低分；适合"红线类、一票否决"指标（如 `tool_call_correctness`） |
| `simple_average` | 算术平均 |
| `weighted_average_by_difficulty` | 用 `test_case.difficulty` 做权重 |
| `pass_rate` | 阈值之上算通过，输出通过率 |
| `coverage` | 必填要点覆盖率（典型用法：`bid_clause_completeness`） |

## 当前内置指标（15 个 = 7 通用 + 8 角色专属）

### 通用指标（7 个，`applicable_roles: ["*"]` + `applicable_scenarios: ["*"]`，覆盖所有角色）

| metric_code | parent_dimension | aggregation | red_line | 一句话定位 |
|---|---|---|---|---|
| `problem_resolution_completeness` | `problem_resolution` | `weighted_average_by_difficulty` | – | 问题是否解决到底，不留半截 |
| `response_clarity_and_structure` | `interaction_quality` | `weighted_average_by_difficulty` | – | 答复是否有条理 / 结构 / 易跟随 |
| `response_conciseness` | `interaction_quality` | `weighted_average_by_difficulty` | – | 不啰嗦不冗余不复读用户问题 |
| `factual_accuracy` | `functional_completeness` | `worst_case` | ✓ `forbidden_behavior` | 反幻觉 / 反编造 / 反自相矛盾 |
| `proactive_clarification` | `problem_resolution` | `weighted_average_by_difficulty` | – | 模糊场景主动澄清而非脑补 |
| `safety_and_ethics_boundary` | `process_compliance` | `worst_case` | ✓ `forbidden_behavior` | 安全 / 伦理 / 拒绝有害诉求 |
| `professional_tone_consistency` | `interaction_quality` | `weighted_average_by_difficulty` | – | 全程稳定专业语气，不掉人设 |

### 角色专属指标（8 个）

| metric_code | parent_dimension | applicable_roles | aggregation | red_line |
|---|---|---|---|---|
| `tool_call_correctness` | `tool_call_correctness` | `customer-service-ecommerce`, `after-sales-agent`, `*` | `worst_case` | ✓ `missing_required_signal` |
| `interaction_empathy` | `interaction_quality` | `customer-service-ecommerce`, `after-sales-agent` | `weighted_average_by_difficulty` | – |
| `order_refund_policy_accuracy` | `functional_completeness` | `customer-service-ecommerce` | `weighted_average_by_difficulty` | – |
| `attendance_rule_compliance` | `process_compliance` | `hr-attendance` | `weighted_average_by_difficulty` | – |
| `bid_clause_completeness` | `functional_completeness` | `bid-writer` | `coverage` | ✓ `missing_required_signal` |
| `legal_citation_accuracy` | `functional_completeness` | `legal-expert` | `weighted_average_by_difficulty` | – |
| `code_change_risk_disclosure` | `problem_resolution` | `software-engineer` | `weighted_average_by_difficulty` | – |
| `confidentiality_boundary_compliance` | `process_compliance` | `legal-expert`, `bid-writer`, `hr-attendance`, `software-engineer` | `worst_case` | ✓ `forbidden_behavior` |

### 角色覆盖矩阵

每个角色都自动获得全部 7 条通用指标 + 该角色专属指标。下表只列**专属**或通过 `tool_call_correctness` 通配命中的指标，**通用 7 条不重复列出**（每行隐含 +7）。

| 角色 | 专属 / 通配命中的指标 | 该角色合计 |
|---|---|---|
| `customer-service-ecommerce` | `tool_call_correctness`, `interaction_empathy`, `order_refund_policy_accuracy` | 7 + 3 = 10 |
| `after-sales-agent` | `tool_call_correctness`, `interaction_empathy` | 7 + 2 = 9 |
| `hr-attendance` | `tool_call_correctness`*, `attendance_rule_compliance`, `confidentiality_boundary_compliance` | 7 + 3 = 10 |
| `bid-writer` | `tool_call_correctness`*, `bid_clause_completeness`, `confidentiality_boundary_compliance` | 7 + 3 = 10 |
| `legal-expert` | `tool_call_correctness`*, `legal_citation_accuracy`, `confidentiality_boundary_compliance` | 7 + 3 = 10 |
| `software-engineer` | `tool_call_correctness`*, `code_change_risk_disclosure`, `confidentiality_boundary_compliance` | 7 + 3 = 10 |

`*` 表示通过 `applicable_roles: ["*"]` 的通配命中，不是显式列出。

> 角色合计是 STEP 1 role-filter 后的 `candidate_metrics` 数量上限。若该角色所跑的某条用例 `applicable_scenarios` 与某条指标交集为空，STEP 2 还会再砍一刀；最终每条用例的 `applicable_metrics` 数量通常落在 6–10 之间。

> ⚠️ STEP 1 的 role-filter 会使用上述 `applicable_roles` 过滤出 `selected_metrics`；
> 拷贝完整 registry 而不过滤 = K9 violation = 整次评估打 tainted。

## 与契约的关系

本目录是**数据层**。**契约层**位于 [`contracts/projections/metric-ontology/`](../contracts/projections/metric-ontology/)，
由它声明 schema 与治理规则；本目录的实例必须通过 schema 校验。
新增 sub-metric 不需要修改任何 `*.projection.json`。
