# STEP 4 — scoreScenario（LLM 并行扇出）

**类型**：LLM 并行扇出
**依据**：工作流合同 `S4` + K3 + K16 + scoring-judgement 提示约束 K1–K4（per_metric_fanout_prompt 层）
**输入**：每场景 ExecutionTrace、丰富测试用例、指标定义、scoring-judgement 规则切片
**输出**：每 (用例, 指标) 对应 `./runs/<eval_id>/scores/<tc_id>__<metric_code>.json`

## 为什么扇出（而非批处理）

将"所有指标 + 所有评分标准 + 完整轨迹 + 输出模式"打包到单个提示词会导致 token 用量爆炸并分散注意力。STEP 4 改为**每个 `(测试用例, 指标)` 对运行一次轻量 LLM 调用**，每次提示词由以下内容构建：

- `scoring-judgement.prompt-constraint.projection.json` 中的相关切片（仅 `applies_to_layer = per_metric_fanout_prompt` 的约束）
- 单个指标的 `scoring_rubric` 和 `runtime_slice_selector`
- 通过该选择器过滤的运行时数据（通常为：该测试用例的预期输出 + 该场景的轨迹，并按指标进一步范围化）
- 严格的响应模式 `metric_score.schema.json`

K3 强制要求：每个 `(测试用例, 指标)` 对恰好一次 LLM 调用，其中 `metric_code ∈ enriched_test_cases[tc].applicable_metrics`。禁止将多个指标或场景批处理到单次调用中。

## 为什么红线判断是确定性的而非 LLM（K4）

LLM 可能在社交/同理心压力下低估红线权重。STEP 4 的 LLM 调用只能**触发 `observed_signals`**（如 `missing_required_tool_call`）。最终通过/失败决策在 STEP 7 由确定性代码计算，使用每个指标声明的 `red_line` 配置。LLM 看不到 `red_line_passed`，也不能返回该字段。

注意：`metric_score.schema.json` 中故意不包含 `red_line_passed` 或 `pass_fail` 字段。

## 硬性规则（K16）

1. **禁止批量伪造。** Agent 不得从自己对轨迹和指标定义的认知中计算分数，然后一次性以统一时间戳输出所有评分文件。每个提示词由（i）该轨迹 +（ii）该指标定义 +（iii）评分标准/红线配置 +（iv）每用例 `stop_conditions` 构建，并独立提交给评估 LLM。

2. **真实的 `scored_at`。** `MetricScore.scored_at` 必须是 LLM 响应接收时的真实 ISO8601 时间戳，精确到至少秒级，且**不同 LLM 调用之间值不同**（预期有毫秒/微秒漂移）。

3. **重复时间戳污染。** 如果同一运行中**超过一个**评分文件具有相同的 `scored_at` 值（字符串相等），则运行被标记为污染，STEP 9 必须在 `open_questions` 中以严重级别 `critical` 列出每对重复时间戳。这用于捕获 **`runs/eval-soul-001/`** 中所有 10 个评分文件都带有 `scored_at = "2026-05-29T14:30:00Z"` 的模式。

4. **推理必须引用证据。** `MetricScore.scoring_reasoning` 必须引用被评分轨迹的 `dialog_turns` 或 `actual_tool_calls` 中至少一个具体的子字符串。仅由通用短语（"based on standards"、"reasonable demonstration result"、"as a typical case"、"基于评估标准生成"）组成而没有可观察证据的推理被视为伪造；评分文件必须重新生成。

5. **禁止的捷径（K14 的镜像）。** Agent 不得以"演示"、"预览"、"示例运行"、"说明性评分"、"时间压力"或任何其他理由跳过每 (用例, 指标) 的 LLM 调用。没有演示模式——每个用例的每个指标都需要真实的评估 LLM 调用。

## 验证伪代码（在 STEP 5 输入门处应用）

```
scored_at_set = { read(f).scored_at for f in scores/*.json }
assert len(scored_at_set) == count(scores/*.json), \
    "K16 violation: duplicate scored_at across score files — evaluator LLM was not invoked per (case, metric)"

for f in scores/*.json:
    score = read(f)
    assert score.scoring_reasoning quotes at least one substring of \
           traces[score.test_case_id].dialog_turns OR actual_tool_calls
```

## 注入每指标扇出提示词的 scoring-judgement K 规则

每指标扇出提示词必须注入 `scoring-judgement.prompt-constraint.projection.json` 中 `applies_to_layer == "per_metric_fanout_prompt"` 的约束：

| scoring-judgement K# | 规则 | 对评分的影响 |
|---|---|---|
| K1 | `BaselineIsFiftyAndEvidenceDriven` | 每个维度从 50 分开始；只有具体证据才能加分；只有可引用的问题才能减分；禁止"感觉"评分 |
| K3 | `HighScoresMustBeRare` | 分数 ≥ 80 必须是例外；大多数合格员工得分在 70–75 范围内 |
| K4 | `EveryAdjustmentNeedsEvidence` | 每次调整引用支持它的对话片段或工具调用；无证据的调整被移除 |

scoring-judgement K2（`RedLineTriggersAreNonNegotiable`）和 K5（`AllIssuesMustBeReported`）在其他地方——参见 `step-05-07-deterministic-rollup.md` 和 `step-09-overall-report.md`。

## 反模式

| 反模式 | K 规则 | 失败模式 |
|---|---|---|
| 将一个轨迹的所有指标打包到单次 LLM 调用 | K3 | 运行在 STEP 4 被污染 |
| 所有 `<tc>__<metric>.json` 文件使用相同的 `scored_at` 时间戳 | K16 | 运行被污染；STEP 5 输入门拒绝 |
| 不调用 LLM 而根据自己的分析计算分数 | K16 | 运行被污染；推理缺乏轨迹引用 |
| 在 MetricScore 中设置 `red_line_passed` 或 `pass_fail` | K4 | 模式拒绝该字段 |
| 将通用样板（"基于评分标准"）用作 `scoring_reasoning` | K16 | 评分文件必须重新生成 |
| 以"该指标明显不适用"为由跳过某 (用例, 指标) 对 | K3 / K16 | 所有 `applicable_metrics` 在 STEP 4 均为强制项 |
