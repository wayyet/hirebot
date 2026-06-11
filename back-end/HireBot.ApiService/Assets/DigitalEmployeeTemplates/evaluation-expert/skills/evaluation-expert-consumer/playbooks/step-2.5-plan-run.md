# STEP 2.5 — planRun（生成执行计划记录）

**类型**：确定性（无 LLM）
**输入**：`/workspace/runtime/evaluation_context.json` + 所有 `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json`
**输出**：`runs/<eval_id>/run_plan.json`（只读计划记录，供人工审阅和追溯，STEP 3 不依赖此文件执行）

## 目的

STEP 2 完成所有测试用例丰富化之后，将本次评估将要运行的**用例列表**和**各用例的预计轮次**落盘，作为轻量的计划记录：

- 人工确认"跑哪些用例、每个跑多少轮"，无需打开 enriched-cases 逐个查看
- 评估结束后可与实际 trace 对比（计划轮次 vs 实际 turns_used）
- 出错时快速定位是哪个用例/轮次失败

> STEP 3 **直接读取** `evaluation_context.json` 和 `enriched-cases/*.enriched.json` 执行，  
> `run_plan.json` 不参与 STEP 3 的运行流程，仅作记录。

## 执行流程

```
1. 读取：
   - eval_id        ← evaluation_context.evaluation_id
   - global_cap     ← evaluation_context.global_turn_cap（默认 30）
   - enriched_tcs   ← glob("runs/<eval_id>/enriched-cases/*.enriched.json")

   > evaluation_context.json 统一从 `/workspace/runtime/evaluation_context.json` 读取（含完整凭据），
   > 不使用 `runs/<eval_id>/` 下可能已脱敏的副本。

2. 对每个 enriched tc 计算：
   tc_id               ← tc.test_case_id
   opening_message     ← tc.input.opening_message
   effective_max_turns = min(tc.turn_budget.hard_max_turns ?? global_cap, global_cap)
   enriched_tc_path    = runs/<eval_id>/enriched-cases/<tc_id>.enriched.json
   trace_path          = runs/<eval_id>/traces/<tc_id>.trace.json

3. 写入 runs/<eval_id>/run_plan.json
```

## 输出格式（run_plan.json）

```jsonc
{
  "eval_id": "<eval_id>",
  "generated_at": "<ISO 8601>",
  "global_turn_cap": 30,
  "scenarios": [
    {
      "tc_id": "<test_case_id>",
      "opening_message": "<tc.input.opening_message 原文>",
      "effective_max_turns": 18,
      "enriched_tc_path": "runs/<eval_id>/enriched-cases/<tc_id>.enriched.json",
      "trace_path": "runs/<eval_id>/traces/<tc_id>.trace.json"
    }
    // …每个 enriched tc 一条
  ]
}
```

**字段说明：**

| 字段 | 来源 | 用途 |
|---|---|---|
| `tc_id` | `enriched_test_case.test_case_id` | 用例标识，与文件名对应 |
| `opening_message` | `enriched_test_case.input.opening_message` | 预览用例第一句话，便于人工审阅 |
| `effective_max_turns` | `min(tc.turn_budget.hard_max_turns, global_cap)` | 计划本用例最多跑几轮 |
| `enriched_tc_path` | 计算得出 | 指向 enriched-cases/ 下的实际输入文件 |
| `trace_path` | 计算得出 | 指向 traces/ 下的预期输出文件 |

## run.py 的参数边界（ws_client.py 歧义的终点）

`run.py` 对外只接受三个路径参数，内部自主完成所有从结构化输入文件到 `ws_client.py` 所需参数的转换：

```
python -u runtime-drivers/ws_jwt/run.py \
  --evaluation-context  /workspace/runtime/evaluation_context.json \
  --enriched-test-case  runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output              runs/<eval_id>/traces/<tc_id>.trace.json
```

> `--evaluation-context` **必须**指向 `/workspace/runtime/evaluation_context.json`（含 `hirebot_api.auth` 凭据的原始文件）。
> `runs/<eval_id>/` 下若存在 evaluation_context 副本，可能已脱敏，**不得**用于此参数。

| 来源文件 | 字段路径 | 内部用途 |
|---|---|---|
| `evaluation_context.json` | `runtime_driver.driver_config.endpoint` | `WsCollector(endpoint, ...)` |
| `evaluation_context.json` | `hirebot_api.auth` | `resolve_auth()` → token → `WsCollector(..., token)` |
| `evaluation_context.json` | `global_turn_cap` | `_resolve_effective_max_turns()` |
| `enriched_test_case.json` | `turn_budget.hard_max_turns` | `_resolve_effective_max_turns()` 内部计算 |

外层（包括单元测试 fixture）只需关心 `enriched_test_case.schema.json` + `evaluation_context.schema.json`，不感知 `ws_client.py` 的底层参数格式。
