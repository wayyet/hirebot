# STEP 5 / 6 / 7 — 确定性汇总 + 红线检查

**类型**：确定性，禁止 LLM
**依据**：工作流合同 `S5` / `S6` / `S7` + K4 + K12 + K13
**输入**：STEP 4 生成的每 (用例, 指标) MetricScore 文件
**输出**：三个持久化的 JSON 产物（见下文）

这三个步骤是对数值输入的纯函数。Agent 以内联方式执行（读文件、计算、写 JSON）。不允许 LLM 调用。

## 必须持久化（K12）

每个步骤必须在下一步开始**之前**在 `./runs/<eval_id>/` 下持久化一个类型化 JSON 产物。STEP 9 从这些文件字节拷贝数值（K7），若任何文件缺失则不得运行。

| 步骤 | 产物 | 关键约束 |
|---|---|---|
| 5 | `aggregated_metric_scores.json` | 键集合 ⊇ `{ m.metric_code for m ∈ selected_metrics }` |
| 6 | `dimension_scores.json` | 键集合**等于** `{ m.parent_dimension for m ∈ selected_metrics }`（K13） |
| 7 | `red_line_check.json` | `red_line` 配置非空的每个指标各一条 |

## STEP 5 — aggregateAcrossScenarios

对每个指标 `m ∈ selected_metrics`：

1. 收集所有每用例分数：从 `./runs/<eval_id>/scores/<tc_id>__<m.metric_code>.json` 获取 `{ tc_id → MetricScore }`
2. 应用 `m.aggregation_strategy` 将矩阵行折叠为单个每指标分数：
   - `worst_case` → 取最低 `overall_score`
   - `simple_average` → 算术平均
   - `weighted_average_by_difficulty` → 使用 `test_case.difficulty` 作为权重
   - `pass_rate` → 达到或超过隐式通过阈值的用例比例
   - `coverage` → 实际观察到的必要信号/元素比例
3. 按 `metric_code` 为键持久化到 `aggregated_metric_scores.json`。

## STEP 6 — rollUpToDimensions（K13）

`dimension_scores.json` 的键集合必须等于 `{ m.parent_dimension for m ∈ selected_metrics }`。具体而言：

- **任何键**都不得出现在没有任何已选指标的 `parent_dimension` 中。为 STEP 1 丢弃的子指标的父维度伪造分数是被禁止的。
- **`selected_metrics` 贡献的每个 `parent_dimension` 必须出现。**
- 每个值是上游 MetricScore 值的确定性汇总。禁止 LLM（K4）。
- `EvaluationReport.dimension_scores` 是字节拷贝（K7），继承相同的键约束。

### 验证

```
expected_dims = { m.parent_dimension for m in selected_metrics }
assert set(dimension_scores.keys()) == expected_dims
```

### 不应做的事（`runs/eval-xiaofu-001/` 伪造 bug）

如果 `selected_metrics` 仅包含 `{interaction_empathy, order_refund_policy_accuracy, tool_call_correctness}`（例如 `customer-service-ecommerce` 经 STEP 1 过滤后），`dimension_scores.json` 必须恰好包含这三个指标汇入的父维度：

```
{
  "interaction_quality": ...,
  "functional_completeness": ...,
  "tool_call_correctness": ...
}
```

当没有已选指标汇入这些维度时，插入 `process_compliance=87`、`problem_resolution=82` 等，正是 **`runs/eval-xiaofu-001/` 中观察到的 K13 违规**——STEP 9 的 LLM 为没有上游证据的维度伪造了数值分数。K13 硬性阻断此行为；STEP 9 必须拒绝任何键集合为严格超集的 `dimension_scores.json`。

## STEP 7 — redLineCheck（K4）

STEP 7 是纯代码——无 LLM，无理由化。精确算法：

```
red_line_check = {}
for m in selected_metrics:
    cfg = m.red_line                     # may be null → skip
    if cfg is None: continue
    triggered = False
    evidence = []
    if cfg.trigger_kind == "missing_required_signal":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            tc    = enriched_cases[tc_id]
            trace = traces[tc_id]
            must_tools = [t for t in tc.expected_tool_calls if t.criticality == "must"]
            absent     = [t for t in must_tools if t.tool_name not in trace.actual_tool_calls]
            if absent:
                triggered = True
                evidence.append({"tc_id": tc_id, "missing": [t.tool_name for t in absent]})
    elif cfg.trigger_kind == "score_below_threshold":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            if score.overall_score < cfg.threshold:
                triggered = True
                evidence.append({"tc_id": tc_id, "score": score.overall_score, "threshold": cfg.threshold})
    elif cfg.trigger_kind == "forbidden_behavior":
        # observed_signals raised by STEP 4 LLM call must include
        # forbidden_behavior_observed; deterministic code only checks presence
        ...
    elif cfg.trigger_kind == "dimension_floor":
        # consult dimension_scores.json (already persisted at STEP 6)
        if dimension_scores[m.parent_dimension] <= cfg.threshold:
            triggered = True
            evidence.append({"dimension": m.parent_dimension, "score": dimension_scores[m.parent_dimension]})

    red_line_check[m.metric_code] = {
        "trigger_kind": cfg.trigger_kind,
        "triggered": triggered,
        "evidence": evidence,
    }
```

### LLM 不允许覆写 `triggered`

诸如*"tool_call_correctness 得 10/100，但由于 agent 有合理的替代行为，红线未触发"*这类叙述性理由，属于 **K4 违规**——即 `runs/eval-xiaofu-001/` 的 bug。STEP 9 的 LLM 可以在 `executive_summary` 散文中呈现已触发的红线，但 `red_line.triggered` 字段是从 `red_line_check.json` 字节拷贝的（K7）。

## 内置红线下限（customer-service-ecommerce 模板）

以下任一触发均自动导致失败，不论加权总分：

- `tool_call_correctness = 0`（`criticality = must` 的指标在轨迹中没有匹配的调用）
- `process_compliance ≤ 30`
- `interaction_quality ≤ 30`
- `functional_completeness ≤ 40`

`*.metric.json` 中声明的每指标 `red_line` 块在 STEP 7 与上述下限合并。

## 反模式

| 反模式 | K 规则 | 失败模式 |
|---|---|---|
| 跳过持久化 `aggregated_metric_scores.json`，让 STEP 9 的 LLM 计算 | K12 | 运行被污染；STEP 9 输入门拒绝 |
| 为 STEP 1 丢弃子指标的父维度伪造 `dimension_scores` | K13 | 运行在 STEP 6 被污染 |
| 调用 LLM "双重检查"红线触发，让其翻转 `triggered` | K4 | 运行在 STEP 7 被污染 |
| LLM 将已触发的红线合理化为"其实没触发"并写入 EvaluationReport | K4 + K7 | 报告必须重新生成 |
| STEP 9 在三个产物（`aggregated_metric_scores.json` / `dimension_scores.json` / `red_line_check.json`）全部存在之前开始 | K12 | STEP 9 拒绝运行 |
