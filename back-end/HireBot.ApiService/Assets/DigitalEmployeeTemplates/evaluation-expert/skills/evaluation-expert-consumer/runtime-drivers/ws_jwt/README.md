# ws_jwt driver

`evaluation-expert-consumer` 内置的 **WebSocket + JWT** 运行时 Driver，用于 STEP 3（`driveEmployeeOnScenario`）。它位于本 consumer 的热插拔 `runtime-drivers/` 层中。

## 此 Driver 的职责（v2.0，长连接 stdin/stdout 协议）

STEP 3 采用**非对称执行的双角色**模式：

| 角色 | 执行模型 | 位于 |
|---|---|---|
| **driver_role**（本目录） | 长连接子进程（`python run.py …`） | `./runtime-drivers/ws_jwt/` |
| **simulator_role** | 宿主评估专家 Agent 本身，使用其**自有** LLM 大脑。**非子进程。** | `./simulators/<simulator_id>/`（仅角色配置文件） |

本 Driver 是长连接 I/O 子进程，负责持有 WebSocket+JWT 连接、向被评估者发送客户话语、收集被评估者回复，并写入最终的 `ExecutionTrace`。它**不**决定客户说什么或何时停止——这些决策属于宿主 Agent（以其自有 LLM 扮演客户模拟器，即运行 STEP 1.5 / STEP 4 / STEP 8 / STEP 9 的同一个大脑）。

`run.py` 每个场景连接一次，在 stdout 上输出 `{"event":"ready",...}`，然后进入循环：

- **从 stdin 收到 `{"action":"send","turn_index":N,"text":"...","decision":{...}}`**：将 `decision` 缓存到 `simulator_trail[]`，通过 WS 发送 `text`，收集被评估者的回复直到 `assistant_done`，追加 `dialog_turns[]` + `actual_tool_calls[]`，并在 stdout 上输出 `{"event":"evaluatee_turn","turn_index":N,"content":"...","tool_calls":[...],"raw_messages":[]}`。
- **从 stdin 收到 `{"action":"end","decision":{...},"termination":{...}}`**：缓存最终决策，组装 `ExecutionTrace`，写入 `--output`，在 stdout 上输出 `{"event":"trace_written","path":"..."}`，关闭 WS，以 0 退出。
- **发生任何 I/O 错误**：输出 `{"event":"error","detail":"..."}`，尽力写入部分 trace，以 2 退出。

当 `auto_approve_tools=true` 时，自动审批被评估者发出的任何 `approval_required`。输出的 `ExecutionTrace` 根据 `runtime-schemas/execution_trace.schema.json` 验证。

本 Driver **不**评分、不判断红线、不生成 `observed_signals`、不过滤信号。这些操作只在 STEP 4 扇出 + STEP 7 redLineCheck 中执行。

## 文件说明

| 文件 | 职责 |
|---|---|
| `driver.json` | 清单文件，根据 `runtime-schemas/runtime_driver.schema.json` 验证 |
| `run.py` | 符合 STEP-3 规范的长连接 stdin/stdout 编排器 |
| `ws_client.py` | 低层 WebSocket 连接 + 逐轮收集（未变更） |
| `requirements.txt` | `websockets>=12.0` |

本目录及 `evaluation-expert-consumer/` 下任何其他位置均**没有** simulator 二进制文件。Simulator 角色由宿主 Agent 自身的 LLM 扮演；`evaluation_context.paths.simulators_dir / runtime_simulator.simulator_id / simulator.json` 只是宿主 Agent 读取的角色配置文件。

## 调用合同

STEP 3 每个场景启动一次本 Driver：

```bash
python run.py \
  --evaluation-context ./runs/<eval_id>/evaluation_context.json \
  --enriched-test-case ./runs/<eval_id>/enriched-cases/<test_case_id>.json \
  --output             ./runs/<eval_id>/traces/<test_case_id>.trace.json
```

`run.py` 从 `evaluation_context.runtime_driver.driver_config` 读取 `driver_config`。STEP 3 负责在启动本 Driver **之前**根据 `driver.json#/config_schema` 验证该块；`run.py` 只重新检查绝对最小值（`endpoint` 和 `token` 非空）。

## 通信协议（行分隔 JSON）

### driver → 宿主 Agent（stdout，每行一个 JSON 对象）

```json
{"event":"ready","driver_id":"ws_jwt","effective_max_turns":15,"evaluation_id":"eval-001","test_case_id":"tc-..."}
{"event":"evaluatee_turn","turn_index":0,"content":"...","tool_calls":[...],"raw_messages":[...]}
{"event":"evaluatee_turn","turn_index":1,"content":"...","tool_calls":[...],"raw_messages":[...]}
{"event":"trace_written","path":"./runs/.../traces/tc-....trace.json","termination":{"reason":"completed_normally","turns_used":4}}
```

不可恢复故障时：

```json
{"event":"error","detail":"<diagnostic>"}
```

### 宿主 Agent → driver（stdin，每行一个 JSON 对象）

```json
{"action":"send","turn_index":0,"text":"我已经等了一星期了 …","decision":{...full SimulatorDecision...}}
{"action":"send","turn_index":1,"text":"那能不能再给点补偿 …","decision":{...}}
{"action":"end","decision":{...final SimulatorDecision with should_continue=false...},
 "termination":{"reason":"completed_normally","detail":"...","final_emotion":"satisfied","turns_used":4}}
```

宿主 Agent 可在任意轮次提前结束（例如 `bottom_line_violated`、`goal_achieved`）。Driver **不会**在 `effective_max_turns` 到达时自动结束——当轮次上限到达时，Driver 只是停止接受后续 `send` 动作；宿主 Agent 需要发出 `termination.reason=max_turns_reached` 的 `end` 动作。

## driver_config + runtime_simulator 示例

```json
{
  "runtime_driver": {
    "driver_id": "ws_jwt",
    "driver_config": {
      "endpoint": "localhost:18789",
      "token": "<JWT>",
      "timeout": 60,
      "auto_approve_tools": true
    }
  },
  "runtime_simulator": {
    "simulator_id": "customer_realistic"
  },
  "global_turn_cap": 30
}
```

说明：

- 遗留字段 `max_turns` 已**移除**。每场景硬性上限现在来自 `min(test_case.turn_budget.hard_max_turns, evaluation_context.global_turn_cap)`。
- 遗留字段 `simulator_timeout` 已**移除**。不再有需要超时的 simulator 子进程——Simulator 运行在宿主 Agent 内部。
- `runtime_simulator.simulator_config` 也已移除（不再有 `model`、`api_key_env`）。驱动客户角色的 LLM 是宿主 Agent 自己的 LLM，在 Agent 运行时层面配置——绝不在本合同内部配置。

## 终止语义

| 条件（除注明外，均由宿主 Agent 的 `end` 动作驱动） | `termination.reason` |
|---|---|
| 宿主 Agent 以 `stop_reason=goal_achieved` 结束 | `completed_normally` |
| 宿主 Agent 以 `stop_reason=bottom_line_violated` 结束 | `bottom_line_violated` |
| 宿主 Agent 以 `stop_reason=deadlock_detected` 或 `customer_gave_up` 结束 | `deadlock_detected` |
| 宿主 Agent 在 `effective_max_turns` 轮后以 `reason=max_turns_reached` 结束 | `max_turns_reached` |
| 被评估者发出任意 `error` 消息 | `evaluatee_error` |
| 逐轮超时耗尽（无 `assistant_done`） | `timeout` |
| `end` 动作到达前 stdin 已关闭 / 未处理异常 | `evaluatee_error`（含详情） |

此映射是有意为之：Driver **不**决定缺少工具调用是否为错误。那是 STEP 4 扇出 + STEP 7 redLineCheck 的职责。

## 安装

```bash
cd evaluation-expert-consumer/runtime-drivers/ws_jwt
pip install -r requirements.txt
```

无需安装 simulator 侧依赖。客户角色就是宿主 Agent 本身；它不会以本 Driver 的名义调用任何外部 LLM API 或读取任何额外的环境变量。
