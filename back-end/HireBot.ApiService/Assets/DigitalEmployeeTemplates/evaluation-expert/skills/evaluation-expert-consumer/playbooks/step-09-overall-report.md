# STEP 9 — buildOverallReport（LLM 综合，双格式输出）

**类型**：LLM 综合（仅生成叙述性文字，精确执行一次）
**依据**：工作流合同 `S9` + K4 + K6 + K7 + K11 + 评分判断 K5（`AllIssuesMustBeReported`）
**输入**：`evaluation_context`、STEP 5/6/7 产物、所有 ScenarioReport 文件、`evaluation_report.schema.json`
**输出**：两个文件（见下文）

## 两个输出文件

| 文件 | 路径 | 用途 |
|---|---|---|
| JSON | `./runs/<eval_id>/reports/evaluation_report.json` | 机器可读，根据 `evaluation_report.schema.json` 验证 |
| HTML | `./runs/<eval_id>/reports/evaluation_report.html` | 人类可读，自包含单文件报告 |

## 数值字段为字节拷贝（K7）

`dimension_scores` / `overall_score` / `red_line` / `passed` 必须与 STEP 5 / 6 / 7 输出字节完全一致：

| EvaluationReport 字段 | 来源文件 |
|---|---|
| `per_metric_final_scores` | `aggregated_metric_scores.json` |
| `dimension_scores` | `dimension_scores.json` |
| `red_line`（含 `triggered`、`evidence`） | `red_line_check.json` |
| `overall_score` | `dimension_scores.json`（加权） |
| `passed` | 确定性推导（见下文通过标准） |

The LLM is allowed to author **only**:

- `executive_summary`
- `strengths`
- `weaknesses`
- `cross_scenario_patterns`
- `improvement_plan`
- `open_questions`

任何与字节拷贝数值相矛盾的 LLM 生成值均为 K7 违规；报告**必须**重新生成。

## 通过/未通过推导

```
passed = (red_line.triggered == false)
         AND (overall_score >= 70)
         AND (∀d ∈ dimension_scores: d.value >= 60)
```

这些阈值是 customer-service-ecommerce 的默认值；逐模板覆盖可在相关工作流合同投影中声明。

## 开放问题呈现（K11 + K16）

`EvaluationReport.open_questions[]` **必须**包含以下条目：

- 每个 Tier-2 用例（`provenance.reliability == "low"`） → caveat `synthesized_from_sop_only_no_user_grounding`（K11）
- 每个污染运行的 K 规则违规（K8 / K9 / K10 / K12 / K13 / K14 / K16）→ severity `critical`
- STEP 5 输入门发现的每对重复 `scored_at`（K16）→ severity `critical`
- 每个被拒绝的 trace（K14）→ 列出受影响的 `tc_id`
- `test_case_status == "missing"` 命中时缺少用户咨询（K11）

Tier-2 / 污染发现的措辞**必须**降级：使用"指示性"/"初步"，而非"确定性"。

## scenario_report 包含（K6）

STEP 9 **必须**链接到 `./runs/<eval_id>/reports/scenarios/<tc_id>.report.json` 文件。**不得内联它们。** STEP 9 也**不得**在每个适用场景都有 ScenarioReport 文件之前开始。

## HTML 生成流程（K17——仅限模板，禁止自由编写 HTML）

**K17（硬性）**：STEP 9 **必须**通过原文加载 `./runtime-schemas/report-template.html` 并仅替换三个合同占位符来渲染 HTML。Agent **不得**手工编写 HTML / CSS / `<script>`。任何未先逐字节读取模板就生成的 HTML 均为 K17 违规，运行被污染；报告**必须**从模板重新生成。

1. 加载 `./runtime-schemas/report-template.html` 处的模板。
2. 收集所有场景数据：对每个测试用例，收集 `{ report: <场景 .report.json>, trace: <.trace.json>, enriched: <enriched-case .json> }`。
3. 替换模板中的占位符：

   | 占位符 | 替换内容 | 说明 |
   |---|---|---|
   | `{{REPORT_DATA}}` | 完整 `evaluation_report.json` 内容作为 JSON 字符串 | 驱动雷达图和标题数值 |
   | `{{SCENARIOS_DATA}}` | 场景对象数组作为 JSON 字符串 | 每个场景一个 Tab |
   | `{{EMPLOYEE_NAME}}` | 员工显示名称 | 在 `<title>` 和页面标题中 |

4. 将最终 HTML 写入 `./runs/<eval_id>/reports/evaluation_report.html`。

这三个占位符**是合同**。如果修改模板，保持占位符名称稳定，或同时更新本操作手册 + `runtime-schemas/report-template.html`。

### K17 自检（STEP 9 返回前强制执行）

在交还运行前，Agent **必须**对生成的 HTML 验证以下所有条件；任何一行失败均意味着 K17 违规：

- the file's first 8 lines are byte-identical to the template's first 8 lines (after `{{EMPLOYEE_NAME}}` substitution);
- the file contains exactly one `<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js"></script>`;
- the file contains the `<canvas id="radarChart">` element and the `new Chart(...)` constructor call;
- the file contains zero occurrences of `{{REPORT_DATA}}` / `{{SCENARIOS_DATA}}` / `{{EMPLOYEE_NAME}}`;
- 内嵌的 `<script id="report-data" type="application/json">` 和 `<script id="scenarios-data" type="application/json">` 块解析为合法 JSON。

## 中文叙述呈现合同（K18——不向终端用户暴露原始英文标记）

**K18（硬性）**：HTML 中每个面向用户的字符串**必须**是中文叙述。Agent **不得**将原始英文 `metric_code`、`trigger_kind`、`stop_reason` 或信号标记字符串（如 `tool_call_correctness · missing_required_signal`、`missing_required_tool_call:query_order_status`）作为主要显示标签。英文代码**只可**作为中文标签后方的小括号技术提示出现。

强制呈现规则：

| 元素 | 错误（原始标记） | 正确（中文叙述） |
|---|---|---|
| 指标标签 | `tool_call_correctness` | `工具调用准确度`（将 `(tool_call_correctness)` 作为小灰色提示） |
| 维度标签 | `process_compliance` | `流程合规` |
| 红线标题 | `tool_call_correctness · missing_required_signal` | `工具调用准确度：得分 10/100，触发"必须工具调用缺失"红线` |
| 红线证据 | `tc-001: 缺失 query_order_status, query_logistics_tracking` | `物流催派 (tc-001)：未调用 查询订单状态 / 查询物流轨迹 / 提交催派工单` |
| 场景信号 | `missing_required_tool_call:query_product_info` | `必须工具未调用：查询商品信息` |

中文标签的真相来源（STEP 9 **必须**将它们全部注入 `REPORT_DATA`；模板故意**没有**内置指标/工具回退，这样新增的指标或工具就不会静默回退为原始英文代码）：

| `REPORT_DATA` 中的字段 | 真相来源 | 覆盖规则 |
|---|---|---|
| `metric_labels` | `metrics/<metric_code>.metric.json#display_name` | 必须包含 `aggregated_metric_scores`、`red_line.triggers` 以及任何场景报告的 `metric_results[].metric_code` 所引用的每个 `metric_code`。缺少条目 = K18 违规。 |
| `tool_labels` | 角色目录工具 `display_name`（例如 `role-catalog/<role>.role.json#tools[].display_name`）；仅当目录无条目时才回退到 2–6 字中文释义 | 必须包含运行中出现在 `expected_tool_calls`、`actual_tool_calls` 以及任何 `missing_required_tool_call:<tool>` 信号中的每个 `tool_name`。 |
| `dimension_labels` | `evaluation_context.dimension_meta[<dim>].display_name`（可选；回退到模板内置的 5 维 `DIM_CONFIG.label`） | 如果客户模板引入了非默认维度，必须在此提供。 |

`TRIGGER_KIND_LABEL`（`missing_required_signal` / `forbidden_behavior` / `threshold_breach`）是工作流合同拥有的枚举级词汇，存在于模板中；新触发类型必须在同一变更中同时添加到模板枚举和本操作手册。

`evaluation_report.json` 中的 `red_line.narratives` 字段必须已经是中文叙述句子列表（K7 字节拷贝规则仍适用于 `triggered` / `triggers`——叙述是 STEP 9 额外编写的 K18 表面，而非对数值的复述）。推荐格式：

```json
"red_line": {
  "triggered": true,
  "triggers": [...],
  "narratives": [
    "工具调用准确度：得分 10/100，触发「必须信号缺失」红线。原因：4 个用例下 must-criticality 必调工具全部未触发。",
    "物流催派（tc-001）：未调用「查询订单状态」「查询物流轨迹」「提交催派工单」"
  ]
}
```

K18 自检（强制）：在 STEP 9 返回前，在生成的 HTML 中搜索以下原始标记——找到任何一个均为 K18 违规：

- `missing_required_signal`, `forbidden_behavior`, `missing_required_tool_call:`
- any `metric_code` shown as a primary label without a Chinese counterpart on the same line
- any of the 5 dimension codes (`tool_call_correctness`, `interaction_quality`, `functional_completeness`, `problem_resolution`, `process_compliance`) shown without a Chinese label
- `aggregated_metric_scores` / `red_line.triggers` / 场景 `metric_results[]` 中任何引用的 `metric_code` 不在 `report.metric_labels` 中（将呈现为原始英文代码）
- `expected_tool_calls` / `actual_tool_calls` / `missing_required_tool_call:<tool>` 中任何引用的 `tool_name` 不在 `report.tool_labels` 中（将呈现为原始英文代码）

## HTML 报告功能

- **能力雷达图**: 5 维度能力覆盖范围，同心圆参考线（0/20/40/60/80/100），灰色虚线目标值（85分），维度标签外置并注明权重
- **场景 Tab 切换**: 每个用例一个 Tab，展示会话聊天历史、模拟器决策过程、工具调用（工具名 + 参数 + 结果）、指标得分、叙述分析
- **自包含**: 单个 HTML 文件，仅依赖 Chart.js CDN，可直接用浏览器打开
- **污染运行横幅**: 当 `EvaluationReport.open_questions` 包含 `critical` 条目时，HTML **必须**在雷达图上方渲染红色横幅，说明运行已被污染

## 反模式

| 反模式 | K规则 | 失败模式 |
|---|---|---|
| LLM 根据上下文判断"改进" `overall_score` | K7 | 报告重新生成 |
| LLM 将 `red_line.triggered` 从 true 翻转为 false | K4 + K7 | 报告重新生成 |
| 将场景报告内容内联到总体报告中而非链接 | K6 | 报告被拒绝 |
| STEP 5 / 6 / 7 产物不存在时开始 STEP 9 | K12 | STEP 9 拒绝运行 |
| 运行包含 `reliability=low` 用例时省略 `open_questions` 中的 Tier-2 caveat | K11 | 报告被标记 |
| STEP 5 输入门发现重复 `scored_at` 时省略 `open_questions` 条目 | K16 | 报告被标记 |
| 手工编写 HTML/CSS/JS 而非通过 `runtime-schemas/report-template.html` 渲染 | K17 | 报告被拒绝，运行被污染 |
| 向终端用户暴露原始英文 `metric_code` / `trigger_kind` / 信号标记 | K18 | 报告被拒绝 |
| 构建每次运行的辅助脚本（如 `scripts/rebuild-eval-report.py`）绕过 STEP 9 | K17 | 脚本被移除，STEP 9 在操作手册下重新执行 |
