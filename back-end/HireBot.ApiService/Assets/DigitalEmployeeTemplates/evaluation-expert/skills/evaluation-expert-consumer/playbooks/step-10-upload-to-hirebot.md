# STEP 10 — uploadToHireBot (verdict + trace 上传)

**Kind**: deterministic (直接调用 driver 目录下的上传脚本)
**Authority**: 本 playbook；不属于 workflow-contract K-rules 范畴（K-rules 覆盖 STEP 0–9）
**Runs**: STEP 9 输出 `evaluation_report.json` 后，作为最终收尾步骤
**Inputs**: `runs/<eval_id>/evaluation_context.json`、`runs/<eval_id>/reports/evaluation_report.json`、`runs/<eval_id>/traces/*.trace.json`
**Outputs**: `runs/<eval_id>/upload_verdict_result.json`、`runs/<eval_id>/upload_trace_result.json`

Compatibility: the upload scripts also tolerate legacy/ad-hoc names produced by older runs (`final_report.json`, `reports/final_report.json`, `evaluation_report_tainted.json`, `final_report_tainted.json`, and `traces/*.execution_trace.json`). New compliant runs MUST still write the standard paths above. Tainted runs are not valid for formal acceptance, but when the user explicitly asks to continue for reference, STEP 10 still syncs the trace bundle and syncs the tainted verdict as `FAIL` with a TAINTED summary so the right-side panel reflects what happened.

## 前提条件

在执行 STEP 10 前，以下条件必须全部满足：

| 条件 | 检查方式 |
|---|---|
| `evaluation_context.hirebot_api` 块存在 | stat `runs/<eval_id>/evaluation_context.json`，确认 `hirebot_api.base_url`、`hirebot_api.employee_id`、`hirebot_api.session_id` 均非空 |
| STEP 9 已完成 | `runs/<eval_id>/reports/evaluation_report.json` 存在且是合法 JSON |
| STEP 3 已完成 | `runs/<eval_id>/traces/` 目录存在且包含至少一个 `*.trace.json` |
| Python 解释器可用 | `python3` 可执行（沙箱内置，无需 venv）|

若 `evaluation_context.hirebot_api` 缺失，本步骤跳过（评估仍然完整，只是不同步到 HireBot）。提示用户在 `evaluation_context.json` 中补充 `hirebot_api` 配置后可手动重跑。

When `evaluation_context.hirebot_api` is present, STEP 10 is a completion gate for HireBot UI synchronization. The agent MUST NOT tell the user that the right-side report or trace panel is updated unless both output files exist and both contain `"status": "success"`. Raw `[FILE_URL:...]` attachments are not a substitute for STEP 10; they only prove files exist in the sandbox, not that HireBot has persisted the report and trace assets.

## Token / 鉴权说明

`verdict_uploader.py` 和 `trace_uploader.py` 均通过 `runtime-drivers/ws_jwt/auth_client.py` 的 `resolve_auth_from_eval_ctx()` 获取 Token。Token 来自 `evaluation_context.hirebot_api.auth`（`client_credentials` 模式），由 C# 在沙箱创建时注入 `OpenSandbox:KingCrab` 凭据，与 ws_jwt driver 换取 WebSocket Bearer Token 使用同一条路径。

配置示例（由 C# 自动注入，无需手动填写）：

```jsonc
"hirebot_api": {
  "base_url": "https://hire.example.com",
  "employee_id": "emp-soul-001",
  "session_id": "EVAL-SESSION-001",
  "auth": {
    "mode": "client_credentials",
    "token_url": "https://passport.example.com/realms/main/protocol/openid-connect/token",
    "client_id": "evaluation-agent",
    "client_secret": "xxxx"
  }
}
```

## 执行步骤

### 步骤 A — 上传评估结论（sync-verdict）

```bash
python3 runtime-drivers/ws_jwt/verdict_uploader.py \
  --evaluation-context runs/<eval_id>/evaluation_context.json \
  --evaluation-report  runs/<eval_id>/reports/evaluation_report.json \
  --output             runs/<eval_id>/upload_verdict_result.json
```

成功标志：脚本打印 `[成功] 评估结论已上传到 HireBot 后端`，输出文件中 `status == "success"`。

对应 HireBot API：`POST /api/v1/employees/{employeeId}/evaluation/sync-verdict`

请求体结构（`EvaluationVerdictSyncRequestDto`）：
```jsonc
{
  "sessionId": "<hirebot_api.session_id>",
  "verdict": {
    "verdict": "PASS" | "FAIL",
    "overallScore": <report.overall_score>,
    "summary": "<report.narrative.executive_summary>",
    "dimensionScores": [
      { "dimension": "functional_completeness", "score": 75.0, "comment": "子指标: ...", "evidenceRefs": [] },
      ...
    ]
  }
}
```

### 步骤 B — 上传执行轨迹（sync-trace）+ 合成用例

```bash
python3 runtime-drivers/ws_jwt/trace_uploader.py \
  --evaluation-context runs/<eval_id>/evaluation_context.json \
  --traces-dir         runs/<eval_id>/traces/ \
  --synthesized-dir    runs/<eval_id>/synthesized-cases/ \
  --output             runs/<eval_id>/upload_trace_result.json
```

`--synthesized-dir` 是可选的。当 STEP 1.5 合成了测试用例（落盘于 `runs/<eval_id>/synthesized-cases/`），传入此参数会将它们嵌入 trace bundle 的 `test_cases` 字段。后端 `SyncTraceAsync` → `EnsureQuestionCardsFromRuntimeTextAsync` 会通过 `CollectRuntimeTestcases` 递归扫描整个 JSON，自动发现 `test_cases` 数组并持久化为 Question Cards，展示在前端右侧面板。

成功标志：脚本打印 `[成功] 执行轨迹已上传到 HireBot 后端`，输出文件中 `status == "success"`。

对应 HireBot API：`POST /api/v1/employees/{employeeId}/evaluation/sync-trace`

请求体结构（`EvaluationTraceSyncRequestDto`）：
```jsonc
{
  "sessionId": "<hirebot_api.session_id>",
  "traceJson": "<JSON字符串，内含 evaluation_id + traces数组>"
}
```

`traceJson` 的内部结构：
```jsonc
{
  "evaluation_id": "<eval_id>",
  "session_id": "<session_id>",
  "status": "completed" | "failed",
  "meta": { "total_turns": N, "employee_name": "...", ... },
  "turns": [ /* 前端可读的对话轮次，由 dialog_turns + actual_tool_calls 转换 */ ],
  "trace_count": N,
  "traces": [
    { /* <tc_id_1>.trace.json 内容（STEP 3 ws_jwt driver 输出）*/ },
    { /* <tc_id_2>.trace.json 内容 */ },
    ...
  ],
  "test_cases": [     // 可选：仅在传入 --synthesized-dir 时出现
    { /* STEP 1.5 合成的用例，tc 格式，含 test_case_id / input.opening_message / ... */ }
  ],
  "test_case_count": N
}
```

后端 `EnsureQuestionCardsFromRuntimeTextAsync` → `CollectRuntimeTestcases` 会递归遍历整个 trace JSON，自动发现 `test_cases` 数组（通过 `IsExplicitTestcaseContainer` 检测）并解析为 `ParsedTestcase`，最终持久化为 `testcases-json` 资产并生成 `EvaluationQuestionCardDto`，展示在前端右侧面板。

## 错误处理

| 错误类型 | 表现 | 处理方式 |
|---|---|---|
| `hirebot_api` 配置缺失 | 解析失败，脚本退出码 1 | 在 `evaluation_context.json` 中补充 `hirebot_api` 配置后重跑 |
| HTTP 401 / 403 | response._error 含 "HTTP 401"/"HTTP 403" | Token 过期或权限不足；确认 `hirebot_api.auth` 中的 `client_secret` 有效后重跑 |
| HTTP 404 | response._error 含 "HTTP 404" | `employee_id` 或 `session_id` 在 HireBot 中不存在；确认会话是否已创建 |
| HTTP 5xx | response._error 含 "HTTP 5xx" | HireBot 后端内部错误；稍后重跑 |
| 网络超时 | response._error 含 "urlopen error" | 检查网络连通性后重跑 |

步骤 A 和步骤 B 相互独立。若步骤 A 失败，步骤 B 仍可继续执行。两个输出文件（`upload_verdict_result.json`、`upload_trace_result.json`）均记录了详细的请求/响应，可用于排查。

## 数据流（新版 vs 旧版）

| 字段 / 概念 | 旧版 live_evaluator | 新版 evaluation-expert-consumer |
|---|---|---|
| 员工 ID | `runtime_context.session.employee_id` | `evaluation_context.hirebot_api.employee_id` |
| 会话 ID | `runtime_context.session.session_id` | `evaluation_context.hirebot_api.session_id` |
| API 地址 | `runtime_context.ncrew_hire.base_url` | `evaluation_context.hirebot_api.base_url` |
| API 鉴权 | `runtime_context.target_sandbox.auth` → `auth_config.json` | `evaluation_context.hirebot_api.auth`（client_credentials） |
| 评估报告输入 | `evaluation_result.json`（evaluator skill 输出） | `evaluation_report.json`（STEP 9 输出） |
| 轨迹输入 | `trace_result.json`（单文件，含 `turns[*].execution_trace`） | `traces/*.trace.json`（每场景一文件，含 `dialog_turns` / `actual_tool_calls` / `simulator_trail`） |
| 维度分数来源 | evaluator 输出的 `dimension_scores`（含 `adjustments`） | STEP 6 byte-copy `dimension_scores`（扁平 dict） |
| 上传脚本位置 | `evaluation-expert/skills/live_evaluator/verdict_uploader.py` | `evaluation-expert-consumer/runtime-drivers/ws_jwt/verdict_uploader.py` |

## 幂等性说明

`sync-verdict` 和 `sync-trace` 均为覆盖写语义（HireBot 后端对同一 `sessionId` 的多次调用取最后一次）。若结果文件写入后发现上传失败，可直接重跑步骤 A / B 而不影响数据一致性。
