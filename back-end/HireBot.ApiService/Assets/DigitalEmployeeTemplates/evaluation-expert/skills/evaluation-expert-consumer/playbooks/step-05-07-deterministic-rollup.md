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

**输入源优先级（multi-agent 架构优化）**：

Report Agent 在 STEP 5 执行时，按以下顺序读取每个 TC 的数据：

```python
for tc_id in completed_tcs:
    summary_path = f"runs/{eval_id}/scores/{tc_id}__summary.json"
    if file_exists(summary_path):
        # 优先路径：读摘要文件（O(1)，极轻量）
        tc_data = load_json(summary_path)
        # metric_scores 中的 score 字段是字节拷贝，直接用于聚合
    else:
        # 降级路径：读原始 score 文件（向后兼容旧运行）
        tc_data = {
            "metric_scores": {
                m: load_json(f"runs/{eval_id}/scores/{tc_id}__{m}.json")
                for m in get_applicable_metrics(tc_id)
            }
        }
```

**K16 唯一性校验**（无论走哪条路径，都在原始 score 文件上执行）：

```python
# 即使已读摘要，K16 校验仍然在原始文件上做，不用摘要里的 scored_at 代替
all_scored_at = []
for tc_id in completed_tcs:
    for metric_code in get_applicable_metrics(tc_id):
        sf = load_json(f"runs/{eval_id}/scores/{tc_id}__{metric_code}.json")
        all_scored_at.append(sf["scored_at"])

scored_at_set = set(all_scored_at)
assert len(scored_at_set) == len(all_scored_at), \
    "K16 violation: duplicate scored_at across score files"
```

> **注意**：K16 校验基于原始 score 文件，与是否读摘要无关。摘要中的 `scored_at` 仅供 Report Agent 快速预览，不替代此校验。

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

STEP 7 是纯代码——无 LLM，无理由化。**当 TC 摘要文件存在时，优先从摘要读取 `actual_tool_calls`、`missing_required_tools`、`observed_signals`，避免加载完整 trace 文件**。精确算法：

```python
# 优先读摘要，降级读 trace（K4，确定性，无 LLM）
def get_tc_signals(tc_id):
    summary_path = f"runs/{eval_id}/scores/{tc_id}__summary.json"
    if file_exists(summary_path):
        s = load_json(summary_path)
        return {
            "actual_tool_calls":    s["actual_tool_calls"],
            "missing_required_tools": s["missing_required_tools"],
            "observed_signals":     [sig["signal"] for sig in s["observed_signals"]],
        }
    else:
        # 降级：读完整 trace（兼容旧运行，无摘要文件）
        trace = load_json(f"runs/{eval_id}/traces/{tc_id}.trace.json")
        return {
            "actual_tool_calls":    trace["actual_tool_calls"],
            "missing_required_tools": [],  # 需从 enriched_tc 重新计算
            "observed_signals":     [],    # 需从所有 score 文件重新聚合
        }

red_line_check = {}
for m in selected_metrics:
    cfg = m.red_line                     # may be null → skip
    if cfg is None: continue
    triggered = False
    evidence = []
    if cfg.trigger_kind == "missing_required_signal":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            tc    = enriched_cases[tc_id]
            tc_signals = get_tc_signals(tc_id)
            must_tools = [t for t in tc.expected_tool_calls if t.criticality == "must"]
            absent     = [t.tool_name for t in must_tools
                          if t.tool_name not in tc_signals["actual_tool_calls"]]
            if absent:
                triggered = True
                evidence.append({"tc_id": tc_id, "missing": absent})
    elif cfg.trigger_kind == "score_below_threshold":
        for tc_id, score in per_metric_scores[m.metric_code].items():
            if score.overall_score < cfg.threshold:
                triggered = True
                evidence.append({"tc_id": tc_id, "score": score.overall_score, "threshold": cfg.threshold})
    elif cfg.trigger_kind == "forbidden_behavior":
        for tc_id in completed_tcs:
            tc_signals = get_tc_signals(tc_id)
            if "forbidden_behavior_triggered" in tc_signals["observed_signals"]:
                triggered = True
                evidence.append({"tc_id": tc_id, "signal": "forbidden_behavior_triggered"})
    elif cfg.trigger_kind == "dimension_floor":
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
| Report Agent 在 STEP 5 直接加载完整 trace 文件（忽略摘要优先规则） | — | 无功能错误，但上下文不必要膨胀；应优先读摘要 |
| 用摘要的 `scored_at` 替代原始 score 文件做 K16 唯一性校验 | K16 | 摘要的 `scored_at` 仅供预览；K16 校验必须在原始 score 文件上执行 |
