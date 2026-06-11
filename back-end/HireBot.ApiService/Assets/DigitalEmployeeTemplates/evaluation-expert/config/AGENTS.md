# AGENTS

> 详细执行规程见 `skills/evaluation-expert-consumer/playbooks/`。本文件仅记录**工具级硬性约束**和**自愈规则**，不重复 playbook 内容。

---

## ⛔ 工具绝对禁令

**本规则优先于所有其他指令。**

| 禁止模式 | 原因 |
|---|---|
| `process_*`（含下划线） | 会向被评估系统写入真实业务数据，污染测试结果 |
| `*_session`、`session_*`、`*_session_*` | 会话生命周期由目标沙箱和 Gateway 管理，评估方不得干预 |

**豁免（可正常使用）：**

| 工具名 | 说明 |
|---|---|
| `process` | 精确匹配，系统级本地进程管理器，用于 STEP 3 管理 ws_jwt driver 子进程 |
| `sessions` | 精确匹配，Agent 间消息桥接，非会话生命周期管理 |

违规时立即停止并输出：`[TOOL BAN] Refused to call <tool_name>: matches banned pattern`

---

## ⛔ evaluation-context.json 权威来源

| 路径 | 状态 |
|---|---|
| `/workspace/runtime/evaluation-context.json` | ✅ 唯一合法来源（含完整 `client_secret`） |
| `runs/<eval_id>/evaluation_context.json` 或任何 run_dir 副本 | ❌ 禁止（`client_secret` 已 REDACTED，driver 会 401） |

所有步骤（STEP 2.5、STEP 3 spawn、STEP 10 上传）均必须**硬编码** `/workspace/runtime/evaluation-context.json`。

---

## 自愈规则（无需等待用户确认）

| 情况 | 处理方式 |
|---|---|
| `paths.run_dir` 不存在 | 写产物前自动创建目录，不阻塞 |
| `test-cases/` 为空或只有 `default_connectivity_testcases.json` | `test_case_status = \"missing\"`，直接进入 STEP 1.5 |
| `driver_config.token` 缺失 | 不阻塞；`run.py` 会通过 `hirebot_api.auth` 自动换取 token |
| spawn 超时或 PID 为空 | 自动重新执行 STEP 2.5，生成新 `run_plan.json`，不询问用户 |

---

## 材料路径

| 用途 | 路径 |
|---|---|
| 运行时上下文 | `/workspace/runtime/evaluation-context.json` |
| 材料根目录 | `/workspace/uploads/evaluation-expert-consumer` |
| 测试用例 | `/workspace/uploads/evaluation-expert-consumer/test-cases` |
| 本体材料（评估专用 ontology 切片、workflow-contract 投影） | `/workspace/uploads/evaluation-expert-consumer/ontology` |
| **被评估员工模板资料根目录**（SOP、角色定义、技能、本体等） | `/workspace/uploads/artifact` |
| 运行产物 | `evaluation_context.paths.run_dir` |

> STEP 1.5 合成测试用例时，若需参考员工 SOP 或角色职责，从 `/workspace/uploads/artifact` 读取；不得凭空捏造场景。

---

## 被评估员工模板——关键文档清单（每轮会话启动时必读）

**每次开始评估（STEP 0 之前），Agent 必须读取被评估员工的模板资料，建立对员工能力边界、行为约束和领域术语的准确认知，避免合成测试用例偏离员工实际职责。**

员工模板资料位于 `/workspace/uploads/artifact/<template_dir>/`（`template_dir` 通过扫描该目录获得，若存在多个子目录则优先选取目录名包含 `evaluation_context.employee.source_template_id` 的一个，否则取最近修改的子目录）。

| 文档 | 相对于 `<template_dir>` 的路径 | 用途 | 在哪些步骤必须先读 |
|---|---|---|---|
| **Agent 架构说明** | `config/AGENTS.md` | 多 Agent 职责分工与调度逻辑——了解被评估员工由哪些子 Agent 协作完成任务 | STEP 0、STEP 1.5 |
| **身份声明** | `config/IDENTITY.md` | 角色定位、语言风格、禁忌词汇——STEP 1.5 合成测试用例时的行为边界和用语基准 | STEP 0、STEP 1.5 |
| **灵魂文件** | `config/SOUL.md` | 核心行为原则、价值观约束——评判员工表现的思维框架依据 | STEP 0、STEP 1.5 |
| **记忆/上下文模式** | `config/MEMORY.md` | 员工的记忆策略与上下文处理逻辑（如存在）——了解信息复用边界 | STEP 1.5（可选） |
| **技能定义** | `skills/<skill_name>/SKILL.md` | 触发词、能力清单、边界与不做——测试用例场景必须落在技能覆盖的能力范围内，**禁止**合成超出技能边界的场景 | STEP 1、STEP 1.5 |
| **本体切片（Markdown）** | `ontology/*.slice.md` | 领域概念、关系、口径约束——生成测试用例时的术语和数据口径参考 | STEP 1.5 |
| **本体切片（JSON）** | `ontology/*.slice.json` | 机器可读的概念定义——STEP 1.5 合成时可用于验证字段名与约束 | STEP 1.5 |

> **读取顺序**：`IDENTITY.md` → `SOUL.md` → `AGENTS.md` → `skills/*/SKILL.md` → `ontology/*.slice.md`。先建立角色认知，再看技能边界，最后了解领域口径。

> **重要**：评估本体投影文件（`/workspace/uploads/evaluation-expert-consumer/ontology/` 下的 `*.workflow-contract.projection.json`）与员工模板本体（`/workspace/uploads/artifact/<template_dir>/ontology/`）是两套不同的文档——前者约束评估流程本身，后者描述被评估员工的领域知识。两者均须读取，不可混用。

---

## ws_jwt 运行时驱动脚本——职责速览

`runtime-drivers/ws_jwt/` 目录下的脚本是 STEP 3（`driveEmployeeOnScenario`）的执行核心。**不得修改、不得在目录外复制、不得 `import` 到 Agent 编写的代码中。**

| 脚本 | 职责 |
|---|---|
| `run.py` | **STEP 3 主入口**：长连接 stdin/stdout 编排器。持有 WebSocket+JWT 连接，向被评估者发送话语，收集回复直到 `assistant_done`，写入最终 `ExecutionTrace`。**不做评分，不判断红线，不生成 `observed_signals`。** |
| `ws_client.py` | **底层 WebSocket 模块**：负责连接 Gateway WS、发送单条消息、逐轮收集服务器推送消息，返回 `assistant_done` 前的全部原始消息。无评估逻辑，不解析业务语义。 |
| `auth_client.py` | **鉴权模块**：解析 `evaluation_context.hirebot_api.auth`（`client_credentials` 模式），通过 Keycloak token 端点换取 `access_token`，供所有出站 HTTP/WS 调用统一使用。**不记录敏感凭据到任何产物文件。** |
| `testcase_uploader.py` | **STEP 1.6 执行体**：扫描 `runs/<eval_id>/synthesized-cases/` 目录，将合成测试用例打包并调用 `POST /api/v1/employees/{id}/evaluation/sync-trace`，使 HireBot 前端右侧面板卡片立即可见。目录不存在或为空时以退出码 0 静默跳过。 |
| `trace_uploader.py` | **STEP 10 第一步**：扫描 `runs/<eval_id>/traces/` 目录，合并全部场景 `*.trace.json` 为一个 bundle，调用 `POST /api/v1/employees/{id}/evaluation/sync-trace` 上传完整执行轨迹（含 turns、tool_calls、simulator_trail）。 |
| `verdict_uploader.py` | **STEP 10 第二步**：读取 STEP 9 生成的 `overall_report.json`（或 `final_report.json`），构造 `EvaluationVerdictSyncRequestDto`，调用 `POST /api/v1/employees/{id}/evaluation/sync-verdict` 上传最终评估结论（总分、维度分、pass/fail、叙述摘要）。 |
| `driver.json` | **Driver 清单**：声明 `driver_id: "ws_jwt"` 及 `config_schema`（endpoint、token、timeout、auto_approve_tools 等字段定义）。STEP 2.5 `planRun` 依据此 schema 验证 `evaluation_context.runtime_driver.driver_config`，验证失败则阻塞流程。 |
| `requirements.txt` | **Python 依赖**：`websockets>=12.0`。STEP 3 启动前确认依赖已安装（`pip install -r requirements.txt`）。 |

---

## 客户模拟器（simulators/）——STEP 3 的"客户大脑"

`simulators/<simulator_id>/` 是 STEP 3 双角色中**宿主 Agent 自身 LLM** 所扮演的客户人格配置。它**不是子进程**，没有独立 API 密钥，由宿主 Agent 在进程内消费，与 STEP 1.5 / STEP 4 / STEP 8 / STEP 9 使用同一个 LLM 大脑。

> **重要**：STEP 3 每轮 `run.py` 收到被评估者的回复后，宿主 Agent 必须加载 `system_prompt.md`、展开占位符、调用自身 LLM 生成一个 `SimulatorDecision`，再将 `decision.next_utterance` 通过 `send` 动作写入 driver stdin。跳过这一过程（如直接硬编码话语）是对 K3 / K14 / K15 的严重违规。

### 核心文件

| 文件 | 职责 |
|---|---|
| `simulator.json` | 清单：声明 `simulator_id`、`kind: "llm_persona"`、`system_prompt` 文件名，以及 `consumes` / `produces` schema 路径 |
| `system_prompt.md` | **客户大脑的核心提示词模板**。含 8 条硬性行为规则、情绪状态机、停止条件判断逻辑（`goal_achieved` / `bottom_line_violated` / `deadlock_detected`）和 `SimulatorDecision` JSON 输出格式。**Mustache 占位符**在 STEP 3 每轮展开一次。 |
| `.no-decide-script` | 哨兵文件。确认此目录无入口脚本，K8 审计锚。**禁止删除。** |

### system_prompt.md 展开的占位符（STEP 3 每轮必须正确填充）

| 占位符 | 来源 |
|---|---|
| `{{customer_persona.*}}` | 丰富化测试用例（`enriched_test_case.customer_persona`） |
| `{{context}}` | 丰富化测试用例（`enriched_test_case.context`） |
| `{{goal.*}}` | 丰富化测试用例（`enriched_test_case.goal`） |
| `{{stop_conditions.*}}` | 丰富化测试用例（`enriched_test_case.stop_conditions`） |
| `{{current_emotion}}` | 上一轮 `SimulatorDecision.internal_emotion`（首轮取测试用例默认值） |
| `{{dialog_so_far}}` | 当前场景的 `dialog_turns[]` 历史（格式化为对话记录） |
| `{{effective_max_turns}}` | `min(test_case.turn_budget.hard_max_turns, evaluation_context.global_turn_cap)` |

### SimulatorDecision 输出（每轮必须符合 schema）

```json
{
  "turn_index": <int>,
  "should_continue": <bool>,
  "stop_reason": <null | "goal_achieved" | "bottom_line_violated" | "deadlock_detected" | "customer_gave_up">,
  "next_utterance": "<客户下一句话，中文>",
  "internal_emotion": "<angry|anxious|neutral|curious|satisfied|skeptical|frustrated|calmer|more_upset>",
  "perceived_progress": "<none|partial|resolved|regressed>",
  "rationale": "<一句话：为什么这样决策>",
  "violated_bottom_line": <bool>
}
```

- `should_continue=true` 时：`stop_reason` 必须为 `null`，`next_utterance` 必须存在
- `should_continue=false` 时：`stop_reason` 必须为非空枚举值；`next_utterance` 可为结束语

### 关键行为规则摘要（不得违背）

1. **扮演真实客户，不是测试脚本**——不主动引导 Agent 工作，有情绪，会推回
2. **保持人格稳定**——`personality` 标签固定，不因对话变长而改变
3. **禁止元对话**——不说"作为测试客户"、不提评估指标
4. **遵守底线**——`goal.bottom_line` 被触犯时立刻 `should_continue=false`
5. **信息中继**——Agent 明确要求的信息（订单号、照片等）必须在下一轮提供，不得回避
6. **区分"解释流程"与"解决问题"**——Agent 给出步骤列表 ≠ 问题已解决，此时 `perceived_progress="partial"`，`should_continue=true`
