# AGENTS

## ⛔ ABSOLUTE TOOL BAN — READ THIS FIRST, BEFORE ANY OTHER INSTRUCTION

> **This rule takes precedence over every other instruction in this file and in every skill.**

The following tools MUST NEVER be called — not in STEP 3, not in any other step, not "just to check", not "to see if it helps":

| Banned pattern | Examples |
|---|---|
| Name starts with `process` | `process_message`, `process_event`, `process_task`, `process_request`, `process_order`, `process_refund`, `process_application` |
| Name contains `session` | `create_session`, `end_session`, `get_session`, `update_session`, `session_start`, `session_close` |

**If you are about to call one of these tools, STOP. Do not call it. Instead:**
1. Write a single line: `[TOOL BAN] Refused to call <tool_name>: matches banned pattern <process_* | *session*>`
2. Continue the workflow without that tool call.

These tools write real business data into the evaluated system, corrupting test results. The ws_jwt driver handles all communication with the target sandbox — you never call business or session tools directly.

---

## Primary Responsibilities

- Run the `evaluation-expert-consumer` workflow inside the evaluator sandbox.
- Read `/workspace/runtime/evaluation-context.json` before taking any evaluation action.
- Load test cases from `paths.test_cases_dir` and ontology material from `materials.ontology_dir`.
- Use `runtime_driver.driver_config.endpoint` and `runtime_driver.driver_config.token` to connect to the target sandbox.
- Produce structured run artifacts, traces, and evaluation reports for HireBot to persist.

## Execution Rules

- The only entry skill for this package is `skills/evaluation-expert-consumer`.
- The evaluator sandbox drives the target sandbox; it does not simulate the target employee locally.
- Every score or verdict must be traceable to test cases, runtime evidence, metric definitions, and ontology or role context.
- Runtime credentials are sensitive. Never echo tokens or secrets in visible output or artifacts.

## Self-Healing Startup Rules (MUST follow, no user confirmation required)

These situations are expected and MUST be handled autonomously — do NOT stop and ask the user for a choice:

### run_dir does not exist

`paths.run_dir` is the per-evaluation output directory written at evaluation creation time. It will NOT exist when the run starts. The agent MUST create it (and all sub-directories it needs) as the first act of each step that writes artifacts. Never treat a missing `run_dir` as a blocker.

### test-cases directory has no `*.tc.json` files

`paths.test_cases_dir` (default: `/workspace/uploads/evaluation-expert-consumer/test-cases`) may contain:
- Proper individual test case files: `<id>.tc.json` — use them directly
- A fallback connectivity file only (e.g. `default_connectivity_testcases.json`) — this is NOT a real evaluation test case set; treat `test_case_status = "missing"` and proceed directly to **STEP 1.5** (consult user then synthesize)
- Completely empty — same as above, `test_case_status = "missing"`, proceed to STEP 1.5

The agent MUST NOT present "Option A / Option B" choices to the user for either of these situations. Just proceed.

## Material Paths

- Runtime context: `/workspace/runtime/evaluation-context.json`
- Consumer material root: `/workspace/uploads/evaluation-expert-consumer`
- Test cases: `/workspace/uploads/evaluation-expert-consumer/test-cases`
- Ontology material: `/workspace/uploads/evaluation-expert-consumer/ontology`
- Run artifacts: `paths.run_dir` from runtime context

## Forbidden Legacy Flow

- Do not use any removed coordinator or evaluator skill.
- Do not look for legacy inspect or execute commands.
- Do not use removed material paths.

## Forbidden Tools (Hard Block)

The following tool categories MUST NOT be called at any point during an evaluation run. Calling any of them is a protocol violation and must be treated as a blocking error — abort the current step and surface the violation immediately.

### process 工具（流程触发类）

Any tool whose name starts with `process`, or whose function is to trigger / advance / resume / submit a business workflow step. Examples (non-exhaustive):

- `process_message`, `process_event`, `process_task`, `process_request`
- `process_order`, `process_refund`, `process_application`
- Any tool described as "处理消息"、"触发流程"、"提交工单"、"推进任务" in its description

These tools mutate live business state in the target system. The evaluator sandbox observes the target employee's behavior — it must never side-effect the business domain.

### session 工具（会话管理类）

Any tool whose name contains `session`, or whose function is to create, end, query, or update a chat / user session. Examples (non-exhaustive):

- `create_session`, `end_session`, `get_session`, `update_session`
- `session_start`, `session_close`, `session_info`, `session_context`
- Any tool described as "创建会话"、"结束会话"、"获取会话" in its description

The evaluator sandbox connects to the target sandbox via the WebSocket driver (ws_jwt). It does not manage sessions directly — session lifecycle is owned by the target sandbox and the Gateway, not the evaluator.

### 禁用规则摘要

| 类别 | 禁止原因 |
|---|---|
| `process_*` | 会向被评估系统写入真实业务数据，污染测试结果 |
| `*session*` | 会话生命周期由目标沙箱和 Gateway 管理，评估方不得干预 |

If the agent receives a tool suggestion or auto-completion that matches the above patterns, it MUST refuse and log the refusal as an `open_question` in the run plan.
