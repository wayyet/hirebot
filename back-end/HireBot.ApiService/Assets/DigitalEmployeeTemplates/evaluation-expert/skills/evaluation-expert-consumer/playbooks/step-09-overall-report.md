# STEP 9 — buildOverallReport（LLM 综合，双格式输出）

**类型**：LLM 综合（仅生成叙述性文字，精确执行一次）  
**依据**：工作流合同 `S9` + K4 + K6 + K7 + K11 + 评分判断 K5（`AllIssuesMustBeReported`）  
**输入**：`evaluation_context`、STEP 5/6/7 产物、所有 ScenarioReport 文件、`evaluation_report.schema.json`  
**输出**：两个文件（见下文）

> **架构说明（重要）**：STEP 9 分为两个阶段：
> 1. **LLM 阶段**：Agent 读取汇总数值文件 + schema，生成 `evaluation_report.json`（纯 JSON）。
> 2. **脚本阶段**：Agent 调用 `report_assembler.py`，由脚本负责读模板、读 trace、拼装 HTML。
>
> Agent **不得**在 LLM 阶段读取 `report-template.html` 或任何 trace 文件——这些是脚本的职责，不是 Agent 的。把大文件读取交给脚本，是防止上下文膨胀导致 Agent 中途停顿的关键设计。

---

## ⚡ STEP 9 执行前预检查

**Agent 只需读取以下小型 JSON 文件，不读 HTML 模板，不读 trace。**

### 第一步：确定运行目录（绝对路径）

```
SKILL_ROOT = /workspace/uploads/evaluation-expert-consumer
eval_id    = evaluation_context.json 中的 run_id 字段
RUN_DIR    = /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>
REPORT_DIR = /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/reports
```

### 第二步：Agent 必须读取（按序，仅这几个文件）

| # | 必读文件（绝对路径） | 用途 | 大小预估 |
|---|---|---|---|
| 1 | `/workspace/runtime/evaluation-context.json` | 确认 `eval_id`、`employee_id`、模板目录 | 小 |
| 2 | `/workspace/uploads/evaluation-expert-consumer/runtime-schemas/evaluation_report.schema.json` | JSON 报告结构约束 | 小 |
| 3 | `<RUN_DIR>/aggregated_metric_scores.json` | STEP 5 产物；字节拷贝源（K7） | 小 |
| 4 | `<RUN_DIR>/dimension_scores.json` | STEP 6 产物；字节拷贝源（K7） | 小 |
| 5 | `<RUN_DIR>/red_line_check.json` | STEP 7 产物；字节拷贝源（K7） | 小 |
| 6 | `<RUN_DIR>/reports/scenarios/<tc_id>.report.json`（全部） | STEP 8 产物；每场景一个，链接到总报告 | 小 |

> ✅ **不在此处读取**：`report-template.html`（交给 `report_assembler.py`）、`traces/*.trace.json`（交给 `report_assembler.py`）、`SOUL.md`、`SKILL.md`（Prep Agent 已加载，Report Agent 不重复读）。
>
> 预检查完成后 Agent 直接进入 JSON 生成阶段。**不存在"预飞完成后停顿等待用户输入"这个节点**——读完即写。

---

## 阶段一：生成 evaluation_report.json（Agent LLM 完成）

### 数值字段为字节拷贝（K7）

以下字段**直接从文件复制**，LLM 不得修改：

| EvaluationReport 字段 | 来源文件 |
|---|---|
| `per_metric_final_scores` | `aggregated_metric_scores.json` |
| `dimension_scores` | `dimension_scores.json` |
| `red_line`（含 `triggered`、`triggers`） | `red_line_check.json` |
| `overall_score` | `dimension_scores.json`（加权求和） |
| `passed` | 确定性推导（见下方通过标准） |

LLM **只负责编写**以下叙述字段（不含任何数字）：

- `executive_summary`
- `strengths`（字符串数组）
- `weaknesses`（字符串数组）
- `cross_scenario_patterns`（字符串数组）
- `narrative.improvement_plan`（每项含 area + action）
- `open_questions`（字符串数组）
- `red_line.narratives`（中文叙述数组，K18 要求）

### 通过/未通过推导

```
passed = (red_line.triggered == false)
         AND (overall_score >= 70)
         AND (∀d ∈ dimension_scores: d.value >= 60)
```

### K18 标签注入（写 JSON 时必须包含）

Agent 必须在 JSON 中注入以下字段，供 HTML 模板渲染中文标签：

```jsonc
{
  "metric_labels": {
    "<metric_code>": "<来自 metrics/<code>.metric.json#display_name 的中文名>"
    // 必须覆盖 aggregated_metric_scores + red_line.triggers + 所有场景 metric_results 中的每个 metric_code
  },
  "tool_labels": {
    "<tool_name>": "<来自 role-catalog/<role>.role.json#tools[].display_name 的中文名>"
    // 必须覆盖 expected_tool_calls + actual_tool_calls + missing_required_tool_call:* 信号中的每个 tool_name
  }
  // dimension_labels 可选；5 个默认维度模板内置，非默认维度时才需提供
}
```

缺少任何 `metric_labels` / `tool_labels` 条目 = K18 违规，`report_assembler.py` 不会报错但 HTML 会显示英文原始代码。

### `red_line.narratives` 格式（K18）

```json
"red_line": {
  "triggered": true,
  "triggers": [...],
  "narratives": [
    "工具调用准确度：得分 10/100，触发「必须信号缺失」红线。",
    "物流催派（tc-001）：未调用「查询订单状态」「查询物流轨迹」「提交催派工单」"
  ]
}
```

### 开放问题呈现（K11 + K16）

`open_questions[]` 必须包含：

- 每个 Tier-2 用例（`reliability == "low"`） → caveat `synthesized_from_sop_only_no_user_grounding`
- 每个污染 K 规则违规 → severity `critical`
- STEP 5 发现的重复 `scored_at` → severity `critical`
- 每个被拒绝的 trace（K14）→ 列出 `tc_id`

### scenario_report 包含（K6）

`scenario_report_refs` 必须填入每个 `<REPORT_DIR>/scenarios/<tc_id>.report.json` 的相对路径，**不得内联内容**。

### 分步生成策略（防止大 JSON 被平台 max_tokens 截断）

**背景**：一次性生成完整的 `evaluation_report.json` 时，若 JSON 体积较大（多个 TC、多个指标、长叙述），可能被平台的 max_tokens 限制截断，导致 Agent 在写报告前停止。

**分步执行顺序**（每步完成后立即将当前进度追加到草稿文件 `evaluation_report_draft.json`）：

#### 第一步：复制数值字段（无 LLM，确定性）

直接从上游文件字节拷贝，不调用 LLM：
- `per_metric_final_scores` ← `aggregated_metric_scores.json`
- `dimension_scores` ← `dimension_scores.json`
- `red_line.triggered`、`red_line.triggers` ← `red_line_check.json`
- `overall_score` ← `dimension_scores.json` 加权求和
- `passed` ← 确定性推导
- `scenario_report_refs` ← 扫描 `reports/scenarios/` 目录

写入草稿：`evaluation_report_draft.json`（含以上字段）

#### 第二步：生成叙述字段（LLM，分批调用）

每次只生成一组相关字段，生成后立即追加到草稿：

| 调用序号 | 生成字段 | 输入依据 |
|---------|---------|---------|
| 调用 1 | `executive_summary` | dimension_scores + red_line |
| 调用 2 | `strengths` + `weaknesses` | scenario_report_refs 中的 what_went_well/wrong |
| 调用 3 | `cross_scenario_patterns` + `narrative.improvement_plan` | 所有场景的 metric_results |
| 调用 4 | `open_questions` | tainted TCs + low-reliability cases + K 规则违规 |
| 调用 5 | `red_line.narratives` | 仅当 red_line.triggered == true 时执行 |
| 调用 6 | `metric_labels` + `tool_labels` | metrics/*.metric.json + role-catalog/*.role.json（K18）|

每次调用完成后，将生成的字段合并到草稿文件。

#### 第三步：合并与验证

所有字段生成完毕后：
1. 读取草稿文件，合并为完整 JSON
2. 对照 `evaluation_report.schema.json` 做 schema 验证
3. 验证通过后写入正式文件 `evaluation_report.json`
4. 删除草稿文件 `evaluation_report_draft.json`

**草稿文件的作用**：若 Agent 在某步中断，下次重启后可检测草稿文件存在，跳过已完成的步骤，从断点继续，不需要全部重做。

### 写入

分步生成完成后，写入 `<REPORT_DIR>/evaluation_report.json`。目录不存在时先 `mkdir -p <REPORT_DIR>/scenarios`。

---

## 阶段二：生成 evaluation_report.html（调用脚本，Agent 不读模板）

`evaluation_report.json` 写入完成后，**立即**执行以下 shell 命令：

```bash
python3 /workspace/uploads/evaluation-expert-consumer/runtime-drivers/ws_jwt/report_assembler.py \
  --evaluation-report   <REPORT_DIR>/evaluation_report.json \
  --scenarios-dir       <REPORT_DIR>/scenarios \
  --traces-dir          <RUN_DIR>/traces \
  --enriched-dir        <RUN_DIR>/enriched-cases \
  --scores-dir          <RUN_DIR>/scores \
  --template            /workspace/uploads/evaluation-expert-consumer/runtime-schemas/report-template.html \
  --output              <REPORT_DIR>/evaluation_report.html \
  --employee-name       "<employee_display_name>"
```

若存在 failed_tcs，追加 `--tainted-tc-ids "tc-001,tc-002"`。

**脚本负责的全部工作**（Agent 不介入）：
- 读取 `report-template.html`（仅脚本读，不消耗 Agent 上下文）
- 读取每个 TC 的 `trace.json`、`scenario_report.json`、`enriched.json`
- 用 `json.dumps` 安全序列化，替换三个占位符（`{{REPORT_DATA}}`、`{{SCENARIOS_DATA}}`、`{{EMPLOYEE_NAME}}`）
- 执行内置自检（JSON 合法性、占位符全替换、Chart.js CDN 存在）
- 写入 `evaluation_report.html`

**退出码处理**：

| 退出码 | 含义 | Agent 动作 |
|---|---|---|
| `0` | 成功 | 继续执行 STEP 10 |
| `1` | 文件缺失 / JSON 非法 / 必填字段缺失 | 检查 `evaluation_report.json` 是否完整，修复后重跑脚本 |
| `2` | HTML 自检失败 | 查看脚本 stderr 输出的具体违规，修复 JSON 后重跑脚本 |

Agent 通过检查退出码判断结果，**不需要再读 HTML 文件做人工验证**。

---

## 两个输出文件

| 文件 | 绝对路径 |
|---|---|
| JSON | `<REPORT_DIR>/evaluation_report.json` |
| HTML | `<REPORT_DIR>/evaluation_report.html` |

---

## 反模式

| 反模式 | K 规则 | 失败模式 |
|---|---|---|
| Agent 在 LLM 阶段读取 `report-template.html` | — | 导致上下文膨胀，Agent 中途暂停等待用户输入；模板由脚本读取 |
| Agent 在 LLM 阶段读取 `traces/*.trace.json` | — | 同上；trace 数据由脚本注入 SCENARIOS_DATA |
| LLM 修改字节拷贝字段（`overall_score`、`dimension_scores` 等） | K7 | 报告重新生成 |
| LLM 将 `red_line.triggered` 从 true 翻转为 false | K4 + K7 | 报告重新生成 |
| 将场景报告内容内联而非链接 | K6 | 报告被拒绝 |
| STEP 5/6/7 产物不存在时开始 STEP 9 | K12 | STEP 9 拒绝运行 |
| 手工拼接 HTML 替换占位符（不调用 `report_assembler.py`） | K17 | JSON 转义错误导致页面白屏；必须改用脚本 |
| `metric_labels` / `tool_labels` 未覆盖全部 code | K18 | HTML 显示英文原始代码 |
| `red_line.narratives` 使用英文原始标记 | K18 | 报告被拒绝 |
