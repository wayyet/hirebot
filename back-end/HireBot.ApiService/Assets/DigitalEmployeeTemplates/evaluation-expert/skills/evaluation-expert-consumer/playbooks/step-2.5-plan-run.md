# STEP 2.5 — planRun（将场景元数据落盘）

**类型**：确定性（无 LLM）
**输入**：`evaluation_context.json` + 所有 `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json`
**输出**：`runs/<eval_id>/run_plan.json`（场景元数据索引，STEP 3 读取路径和参数用）

## 目的

STEP 2 完成所有测试用例的丰富化之后，将每个场景所需的路径和参数集中写入 `run_plan.json`，让 STEP 3 只需读取这份索引，不必自己拼路径。

## 执行流程

```
1. 读取：
   - eval_id          ← evaluation_context.evaluation_id
   - global_cap       ← evaluation_context.global_turn_cap（默认 30）
   - scenarios_inputs ← 所有 enriched-cases/<tc_id>.enriched.json 文件

2. 对每个 enriched tc 计算：
   tc_id               ← tc.test_case_id
   scenario_id         ← tc.applicable_scenarios[0]（若为 ["*"] 则取 employee.display_name）
   enriched_tc_path    = /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/enriched-cases/<tc_id>.enriched.json
   trace_path          = /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/traces/<tc_id>.trace.json
   opening_message     ← tc.input.opening_message
   effective_max_turns = min(tc.turn_budget.hard_max_turns, global_cap)
   pad_dir             = /tmp/eval-driver/<eval_id>/<tc_id>

3. 写入 runs/<eval_id>/run_plan.json
```

## 输出格式（run_plan.json）

```jsonc
{
  "run_id": "<eval_id>",
  "run_dir": "/workspace/uploads/evaluation-expert-consumer/runs/<eval_id>",
  "scenarios": [
    {
      "scenario_id": "<applicable_scenarios[0] 或 employee.display_name>",
      "test_case_id": "<tc_id>",
      "test_case_path": "/workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/enriched-cases/<tc_id>.enriched.json",
      "trace_path": "/workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/traces/<tc_id>.trace.json",
      "opening_message": "<tc.input.opening_message 原文>",
      "effective_max_turns": 18,
      "pad": "/tmp/eval-driver/<eval_id>/<tc_id>"
    }
    // …每个 enriched tc 一条
  ]
}
```

**关键路径规则：**
- `test_case_path` 必须指向 `enriched-cases/` 下的 `.enriched.json`，不是 `test-cases/` 下的原始文件
- `trace_path` 必须在 workspace 内（`/workspace/…`），不是 `/tmp` 下
- `pad` 是临时工作目录路径（字符串），STEP 3 在此目录下创建 `in`、`out`、`err`、`pid` 文件

## STEP 3 如何使用此文件

STEP 3 读取 `run_plan.json`，对每个 scenario：

1. **`shell`**：`mkdir -p <pad> && touch <pad>/in <pad>/out`
2. **`shell`**：后台启动 driver，`stdout >> <pad>/out`，`stderr >> <pad>/err`（参见 AGENTS.md 中的 spawn 模板）
3. **`read_file`**：轮询 `<pad>/out`，Agent 内存跟踪已读行数
4. **`shell`**：`printf '%s\n' '<action_json>' >> <pad>/in` 写入动作
5. **`shell`**：清理 pad 目录

run_plan.json 中**没有** `commands` 字段。所有 shell 命令由 STEP 3 在执行时直接构造，参数取自本文件的路径字段。
