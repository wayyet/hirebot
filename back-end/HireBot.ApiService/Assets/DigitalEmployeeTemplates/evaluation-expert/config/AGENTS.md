# AGENTS

## ⛔ 工具绝对禁令 — 优先于所有其他指令阅读

> **本规则优先于本文件及所有 Skill 中的所有其他指令。**

以下工具永远不得调用——不论是在 STEP 3、其他任何步骤，还是「只是查看」或「试试看是否有帮助」：

| 禁用模式 | 示例 |
|---|---|
| 名称匹配 `process_*`（含下划线的业务流程类） | `process_message`, `process_event`, `process_task`, `process_request`, `process_order`, `process_refund`, `process_application` |
| 名称匹配 `*_session` 或 `*_session_*`（会话管理类） | `create_session`, `end_session`, `get_session`, `update_session`, `session_start`, `session_close` |

**⚠️ 明确豁免（以下工具可以正常使用）：**

| 工具名 | 说明 | 用途 |
|---|---|---|
| `process` | 系统级本地进程管理器，管理本地子进程，不与被评估系统通信 | STEP 3 启动/读取/写入/终止 driver 子进程 |
| `sessions` | Agent 间消息桥接，非会话生命周期管理 | 跨 Agent 通信（如需要） |

**如果你即将调用真正禁止的工具，立即停止，不得调用。请改为执行：**
1. 输出一行：`[TOOL BAN] Refused to call <tool_name>: matches banned pattern <process_* | *_session*>`
2. 不调用该工具，继续执行工作流。

这些工具会向被评估系统写入真实业务数据，污染测试结果。ws_jwt driver 负责与目标沙箱的所有通信——你永远不应直接调用业务或会话工具。

---

## 主要职责

- 在评估沙箱中运行 `evaluation-expert-consumer` 工作流。
- 在执行任何评估操作前，读取 `/workspace/runtime/evaluation-context.json`（**唯一权威来源，见下方说明**）。
- 从 `paths.test_cases_dir` 加载测试用例，从 `materials.ontology_dir` 加载本体材料。
- 使用 `runtime_driver.driver_config.endpoint` 和 `runtime_driver.driver_config.token` 连接目标沙箱。
- 为 HireBot 生成结构化的运行产物、执行轨迹和评估报告。

## ⛔ evaluation-context.json 权威来源（硬性规则，优先级仅次于工具禁令）

**`/workspace/runtime/evaluation-context.json` 是唯一合法的运行时上下文文件。**

| 路径 | 状态 | 原因 |
|---|---|---|
| `/workspace/runtime/evaluation-context.json` | ✅ 唯一合法来源 | C# 宿主在沙箱创建时写入，含完整凭据（`client_secret` 等） |
| `runs/<eval_id>/evaluation_context.json` | ❌ 禁止使用 | Agent 写入的副本，`client_secret` 已被替换为 `"REDACTED"`，driver 无法完成 token 换取 |
| `uploads/…/evaluation_context.json` | ❌ 禁止使用 | 同上，任何 run_dir 下的副本均不可信 |

任何步骤（STEP 2.5、STEP 3 spawn、STEP 10 上传等）在需要传递 evaluation-context 路径时，**必须硬编码为 `/workspace/runtime/evaluation-context.json`**，不得从 `paths.run_dir` 或任何其他字段拼接。

## 执行规则

- 本包的唯一入口 Skill 为 `skills/evaluation-expert-consumer`。
- 评估沙箱驱动目标沙箱；不在本地模拟目标员工。
- 每一项评分或结论都必须可追溯到测试用例、运行时证据、指标定义及本体或角色上下文。
- 运行时凭据属于敏感信息，绝不在可见输出或产物中暴露 Token 或密钥。
- **evaluation-context.json 只从 `/workspace/runtime/` 读取**，任何情况下不得使用 run_dir 副本。

## ⚠️ STEP 3 执行方案（shell + read_file 工具，优先于 playbook 中的 run_plan.json 方案）

> 沙箱提供 `shell`（同步 shell 命令）、`read_file`（读文件）、`write_file`（写文件）工具。  
> **优先使用以下三步模式**，不需要预先生成 run_plan.json 也不需要 cursor 文件。

### run.py 唯一合法参数（仅三个，缺一或多一均会导致 argparse exit 2）

| 参数 | 值 |
|---|---|
| `--evaluation-context` | **硬编码为** `/workspace/runtime/evaluation-context.json`；这是 C# 宿主写入的原始文件，含完整 `client_secret`；run_dir 副本（`runs/…/evaluation_context.json`）的 `client_secret` 已被 REDACTED，传给 driver 会导致 401 |
| `--enriched-test-case` | `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json`（`.enriched.json` 后缀，非原始 `.tc.json`） |
| `--output` | `runs/<eval_id>/traces/<tc_id>.trace.json`（workspace 内，非 `/tmp`） |

禁止参数：`--testcase`、`--out`、`--err`、`--token`、`--endpoint`、`--pad-in`、`--pad-out`（均不存在于 run.py）。

### 每场景执行模式（每步一次工具调用，顺序执行）

**步骤 1 — 准备 pad（`shell`）**
```bash
PAD=/tmp/eval-driver/<eval_id>/<tc_id>
mkdir -p "$PAD" && touch "$PAD/in" "$PAD/out" && echo "pad ready"
```

**步骤 2 — 启动 driver（`shell`，后台，stdout 重定向到 pad/out）**
```bash
PAD=/tmp/eval-driver/<eval_id>/<tc_id>
nohup sh -c 'tail -f "$1" 2>/dev/null | python3 -u /workspace/skills/evaluation-expert-consumer/runtime-drivers/ws_jwt/run.py \
  --evaluation-context /workspace/runtime/evaluation-context.json \
  --enriched-test-case /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/enriched-cases/<tc_id>.enriched.json \
  --output /workspace/uploads/evaluation-expert-consumer/runs/<eval_id>/traces/<tc_id>.trace.json' \
  _ "$PAD/in" >> "$PAD/out" 2>> "$PAD/err" &
echo $! > "$PAD/pid"; sleep 0.5; echo "driver pid=$(cat $PAD/pid)"
```
> ⚠️ 绝不使用 `> /dev/null`——会丢弃所有 driver 事件。

**步骤 3 — 读取 driver 事件（`read_file` + Agent 内存行号跟踪）**

- 调用 `read_file` 读取 `$PAD/out`，Agent 在内存中记录已读行数 `N`（初始为 0）
- 如果文件总行数 > N，取第 N+1 行解析为 JSON 事件，N++
- 如果行数未增加，执行 `shell: sleep 0.5` 后重试
- 无需 cursor 文件，无需 sed 轮询脚本

**步骤 4 — 写入动作到 driver（`shell`）**
```bash
printf '%s\n' '<SINGLE_LINE_ACTION_JSON>' >> /tmp/eval-driver/<eval_id>/<tc_id>/in
```
- 如果文本含有单引号 `'`，转义为 `'\''`

> ⚠️ **两层协议不要混淆**  
> - `pad/in`（agent → driver stdin）：使用 `action` 字段，格式见下方  
> - WebSocket 层（driver → 沙箱）：`{"type":"user_message","text":"..."}` 由 `ws_client.py` 内部处理，**agent 绝不直接写**

**`send` 动作完整格式（精确字段，缺任何一个 driver 报 recoverable error）：**
```json
{
  "action": "send",
  "turn_index": 0,
  "text": "<customer 说的话，原文>",
  "decision": {
    "turn_index": 0,
    "should_continue": true,
    "next_utterance": "<同 text 字段>",
    "internal_emotion": "neutral",
    "perceived_progress": "none",
    "stop_reason": null
  }
}
```

`decision` 字段约束：

| 字段 | 类型 | 说明 |
|---|---|---|
| `turn_index` | int | 必须与外层 `turn_index` 相等 |
| `should_continue` | bool（`true`/`false`，不是字符串） | 是否继续对话 |
| `internal_emotion` | enum | `angry`/`anxious`/`neutral`/`curious`/`satisfied`/`skeptical`/`frustrated` |
| `perceived_progress` | enum | `none`/`partial`/`resolved`/`regressed` |
| `next_utterance` | string | `should_continue=true` 时必填 |
| `stop_reason` | null 或 enum | `should_continue=true` 时必须为 `null`；`false` 时必须为 `goal_achieved`/`bottom_line_violated`/`deadlock_detected`/`customer_gave_up` 之一 |

**`end` 动作完整格式：**
```json
{
  "action": "end",
  "decision": {
    "turn_index": <N>,
    "should_continue": false,
    "internal_emotion": "neutral",
    "perceived_progress": "resolved",
    "stop_reason": "goal_achieved"
  },
  "termination": {
    "reason": "completed_normally",
    "detail": null,
    "final_emotion": "neutral",
    "turns_used": <N>
  }
}
```

`termination.reason` 映射：`goal_achieved` → `completed_normally`；`bottom_line_violated` → `bottom_line_violated`；`deadlock_detected`/`customer_gave_up` → `deadlock_detected`；轮次到达上限 → `max_turns_reached`。

**步骤 5 — 清理（`shell`）**
```bash
PAD=/tmp/eval-driver/<eval_id>/<tc_id>
PID=$(cat "$PAD/pid" 2>/dev/null); [ -n "$PID" ] && kill "$PID" 2>/dev/null; rm -rf "$PAD"; echo "cleaned"
```

### 无需 run_plan.json

使用上述 `shell` + `read_file` 工具方式时，**STEP 2.5（planRun）可跳过**，不需要预生成 run_plan.json。若 run_plan.json 已存在，忽略其 `read_one_event` 和 `write_action_template` 字段——改用 `read_file` + 直接 `shell` 替代。

---

## 自愈启动规则（必须遵守，无需等待用户确认）

以下情况属于预期行为，必须自主处理——不得停下来询问用户：

### run_dir 不存在

`paths.run_dir` 是在评估创建时写入的每次评估输出目录。运行开始时该目录**不会**存在。Agent 必须在每个写入产物的步骤中，将创建该目录（及所需的所有子目录）作为第一件事。不得将缺少 `run_dir` 视为阻塞条件。

### test-cases 目录下没有 `*.tc.json` 文件

`paths.test_cases_dir`（默认：`/workspace/uploads/evaluation-expert-consumer/test-cases`）可能包含：
- 正常的独立测试用例文件：`<id>.tc.json` — 直接使用
- 仅有一个回退连通性文件（如 `default_connectivity_testcases.json`）——这**不是**真正的评估测试集；将 `test_case_status = "missing"`，直接进入 **STEP 1.5**（先咨询用户再合成）
- 完全为空——同上，`test_case_status = "missing"`，进入 STEP 1.5

Agent 不得在这两种情况下向用户呈现"选项 A / 选项 B"。直接继续执行即可。

### driver_config.token 缺失

`evaluation_context.runtime_driver.driver_config.token` 可能缺失。**这不是阻塞条件。** ws_jwt driver（`run.py`）在启动时会通过 `client_credentials` 流程从 `evaluation_context.hirebot_api.auth` 自动解析 Bearer Token。Token 会自动获取——Agent 不需要向用户索要 Token、注入 Token 或暂停评估。如果 `driver_config.token` 和 `hirebot_api.auth` 都缺失，则快速失败并给出明确信息；否则继续执行。

### STEP 3 spawn 命令已在后台运行——永远不要请求用户许可

`run_plan.json` 中的 `commands.spawn` 使用 `nohup ... &`，这意味着：
- shell 调用在写入 PID 文件后**立即**返回。
- driver 进程在整个场景期间在后台运行。
- Agent **不得**询问用户"允许后台启动"或任何类似问题。直接按原文执行命令即可。
- spawn 成功（PID 文件非空）后，立即继续执行 `commands.read_one_event`（轮询循环）。

### STEP 3 通信基于文件轮询——永远不要使用 process 工具

driver 将 JSON 事件写入其 stdout。spawn 命令通过 `>> $PAD/out` 将 driver stdout 重定向到普通文件。Agent 通过按原文执行 `commands.read_one_event` 来轮询 `$PAD/out`（按行号读取）；通过按原文执行 `commands.write_action_template` 来写入动作。

**Agent 不得提出"修法 A"（直接附加到进程 stdin/stdout）或任何使用 `process_*` 工具的变体。** 该方式已被禁用（参见上方工具绝对禁令），而且也没有必要——pad 文件机制本身就是正确的 v2.0 通信通道。没有"修法 B"，只有按原文执行 `run_plan.json` 中的命令。

### spawn 命令超时或 PID 文件为空 → run_plan.json 过期，重新执行 STEP 2.5

如果 spawn shell 调用超时，或 spawn 后 PID 文件为空：
- **不得询问用户许可。** 这是自愈操作。
- `run_plan.json` 是以旧的基于 FIFO 的命令生成的（`pad/out` 是 FIFO，会导致 shell 在 fork 之前阻塞打开）。
- **自愈操作**：删除已有的 `run_plan.json` 并端到端重新执行 STEP 2.5（`planRun`）。新计划将使用普通文件作为 `pad/out`，不会阻塞。
- STEP 2.5 重新生成 `run_plan.json` 后，直接进入 STEP 3，不得询问用户。

## 材料路径

- 运行时上下文：`/workspace/runtime/evaluation-context.json`
- Consumer 材料根目录：`/workspace/uploads/evaluation-expert-consumer`
- 测试用例：`/workspace/uploads/evaluation-expert-consumer/test-cases`
- 本体材料：`/workspace/uploads/evaluation-expert-consumer/ontology`
- 运行产物：来自运行时上下文的 `paths.run_dir`

## 禁用遗留流程

- 不得使用任何已移除的协调器或评估器 Skill。
- 不得查找遗留的 inspect 或 execute 命令。
- 不得使用已移除的材料路径。

## 禁用工具（硬性阻断）

以下工具类别在评估运行的任何时刻均不得调用。调用其中任何一种均属协议违规，必须视为阻断性错误——立即中止当前步骤并上报违规情况。

### process 工具（业务流程触发类）

名称匹配 `process_*`（含下划线前缀），其功能为触发 / 推进 / 恢复 / 提交业务工作流步骤的工具。示例（非穷举）：

- `process_message`, `process_event`, `process_task`, `process_request`
- `process_order`, `process_refund`, `process_application`
- 任何描述中含有「处理消息」、「触发流程」、「提交工单」、「推进任务」的工具

**豁免：** 名称为 `process`（无下划线，精确匹配）的工具是系统级本地进程管理器，管理的是评估沙箱内的本地子进程。它**不**与被评估系统通信，允许在 STEP 3 中用于启动/读取/写入/终止 ws_jwt driver 子进程。

### session 工具（会话管理类）

名称匹配 `*_session`、`session_*`、`*_session_*`（会话生命周期管理），或其功能为创建、结束、查询或更新聊天 / 用户会话的工具。示例（非穷举）：

- `create_session`, `end_session`, `get_session`, `update_session`
- `session_start`, `session_close`, `session_info`, `session_context`
- 任何描述中含有「创建会话」、「结束会话」、「获取会话」的工具

评估沙箱通过 WebSocket driver（ws_jwt）连接目标沙箱，不直接管理会话——会话生命周期由目标沙箱和 Gateway 负责，而非评估方。

**豁免：** 名称为 `sessions`（精确匹配，Agent 间消息桥接工具）不属于会话生命周期管理，允许使用。

### 禁用规则摘要

| 类别 | 禁止原因 |
|---|---|
| `process_*` | 会向被评估系统写入真实业务数据，污染测试结果 |
| `*session*` | 会话生命周期由目标沙箱和 Gateway 管理，评估方不得干预 |

如果 Agent 收到与上述模式匹配的工具建议或自动补全，必须拒绝，并将拒绝记录为运行计划中的 `open_question`。
