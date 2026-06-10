# simulators

`evaluation-expert-consumer` 的第四个热插拔数据层，与 `./metrics/`、`./test-cases/` 和 `./runtime-drivers/` 并列。

**用户模拟器**是评估专家 Agent 在 STEP 3（`driveEmployeeOnScenario`）中扮演客户时所使用的**角色配置文件**。模拟器与运行时 driver（I/O 子进程）共同构成 **STEP 3 的双角色**：

| 角色 | 执行模型 | 职责 | 位于 |
|---|---|---|---|
| `runtime-drivers/<driver_id>/` | **子进程**（例如 `python run.py …`） | 连接层 I/O——与被评估者通信、应用 JWT、发送/接收帧 | `./runtime-drivers/` |
| `simulators/<simulator_id>/` | **非子进程。** 由宿主 Agent 自身 LLM 消费的提示词模板 + 清单 | 客户大脑——决定下一句话以及何时/为何停止 | `./simulators/` |

> ⚠️ 关键不对称性。**Driver 是子进程**，因为协议 I/O（WebSocket / JWT / TLS / 工具审批）不是 LLM 能自行完成的。**模拟器不是子进程**：决定客户下一步说什么，正是评估专家 Agent 自身 LLM 所擅长的对话任务——与运行 STEP 1.5 / STEP 4 / STEP 8 / STEP 9 的是同一个大脑。为了跟自己对话而再启动一个带独立 API 密钥的 LLM，只会重复消费、增加运维复杂度，并破坏本技能中所有其他 LLM 步骤的一致性。

合同层（`contracts/projections/**`）是**提供商无关的**：它从不引用特定的 LLM、模型或提示词。人格专用提示词模板**只**存在于模拟器目录中；消费这些模板的 LLM 是运行时托管评估专家 Agent 的任意大脑。

## 热插拔规则

添加新人格只需**放入一个目录**：

```
simulators/
└── <simulator_id>/
    ├── simulator.json        ← 必需，根据 runtime-schemas/simulator.schema.json 验证
    ├── system_prompt.md      ← 必需，simulator.json.system_prompt 中命名的模板文件
    ├── .no-decide-script     ← 必需的哨兵文件（见下文）
    └── ...                   ← 少量 few-shot 示例、可选辅助文件（无可执行文件）
```

添加新模拟器时，**不需要**编辑任何 `*.projection.json`、`SKILL.md` 或工作流合同，也**不需要**添加任何 `decide.py` / 入口脚本——没有需要调用的子进程。

### `.no-decide-script` 哨兵文件

每个模拟器目录**必须**包含一个名为 `.no-decide-script` 的隐藏文件，它有三个用途：

1. **自文档标记**，表明此目录的 `kind: "llm_persona"` 且**没有**入口脚本。
2. **K8 审计锚**——工作流合同的 K8（`NoAdhocOrchestratorScripts`）延伸至 `./simulators/<simulator_id>/`。审计查找此哨兵以确认目录有意不含脚本；删除它会削弱防护。
3. **入门提示**——新增模拟器时，复制 `customer_realistic/.no-decide-script`，让每个新人格都重申这一合同。

示例内容（创建新模拟器时原文复制）：

> SENTINEL — DO NOT REMOVE THIS FILE
>
> This simulator is a `llm_persona` profile (see `simulator.json` → `kind: "llm_persona"`).
> It has NO entry script (no `decide.py` / `run.py` / `*.py` / `*.sh` / `*.ts` / `*.js` / `*.mjs` / `*.ipynb` / Makefile / `*.cmd` / `*.ps1`). The host evaluation-expert agent itself plays the customer in-process using `system_prompt.md`, with its own LLM brain. There is NO subprocess, NO independent LLM api_key, NO HTTP call.

如果飞行前不变量第 5 条在模拟器目录中发现了可执行文件，会立即视为 K8 违规——运行在 STEP 3 启动前就已被污染。

## 必需的输入/输出合同

每个模拟器，无论宿主 Agent 使用何种模型，都**必须**遵守相同的合同：

| 方向 | 格式 | 模式 |
|---|---|---|
| **输入**（由宿主 Agent 的 LLM 通过提示词展开消费） | 已丰富化的测试用例（人格/目标/停止条件/开场话语）+ 进行中的执行 trace（对话轮次 + 前序 simulator_trail） | `runtime-schemas/enriched_test_case.schema.json` + `runtime-schemas/execution_trace.schema.json` |
| **输出**（由宿主 Agent 的 LLM 生成，本地验证后追加） | 每轮恰好一个 `SimulatorDecision` | `runtime-schemas/simulator_decision.schema.json` |

如果生成的 JSON 未通过 `simulator_decision.schema.json` 验证，STEP 3 必须对该轮次快速失败；场景以 `reason=evaluatee_error` 终止，`detail` 记录验证失败信息。

## 运行时选择模拟器

`evaluation_context.runtime_simulator.simulator_id` 决定宿主 Agent 加载 `./simulators/` 下的哪个目录。解析顺序（与 driver 解析一致）：

1. `EvaluationContext.runtime_simulator.simulator_id`
2. 环境变量 `EVALUATION_SIMULATOR_ID`
3. 硬性失败（无隐式默认值——错误的人格会像错误的协议一样悄无声息地损坏评估）。

`./simulators/` 目录本身可通过 `EVALUATION_SIMULATORS_DIR` 重定位。

## `simulator.json` 最小结构

```json
{
  "simulator_id": "customer_realistic",
  "version": "2.0.0",
  "kind": "llm_persona",
  "system_prompt": "system_prompt.md",
  "produces": "runtime-schemas/simulator_decision.schema.json",
  "consumes": [
    "runtime-schemas/enriched_test_case.schema.json",
    "runtime-schemas/execution_trace.schema.json"
  ],
  "capabilities": {
    "supports_emotion_tracking": true,
    "supports_progress_assessment": true,
    "supports_bottom_line_check": true
  }
}
```

该文件根据 `runtime-schemas/simulator.schema.json` 验证。STEP 3 每次评估运行读取一次。

模拟器层中**没有** `entry`、`language`、`config_schema`、`model` 或 `api_key_env` 字段。这些概念属于 driver，不属于模拟器。

## 模拟器编写者的硬性规则

1. **每轮一个 SimulatorDecision。** 宿主 Agent 每次客户轮次调用一次 LLM，展开渲染后的系统提示词 + 上下文。
2. **对话状态存在于 trace 中。** 宿主 Agent 每轮从 `execution_trace.simulator_trail` + `dialog_turns` 重新推导 `current_emotion` / `dialog_so_far`——绝不依赖隐藏的 Agent 记忆。
3. **遵守 `goal.bottom_line`。** 如果被评估者的最新回复低于客户底线，决策**必须**为 `should_continue=false`、`stop_reason=bottom_line_violated`、`violated_bottom_line=true`。STEP 3 信任客户大脑的判断。
4. **遵守 `stop_conditions.success`。** 一旦客户的主要目标达成，不要继续说话。以 `stop_reason=goal_achieved` 发出 `should_continue=false`。
5. **不要偏离人格。** 情绪可以演变（`calmer` / `more_upset`），但 `customer_persona.personality` 在场景期间是固定的。不要突然把"急性子"客户变成耐心的人。
6. **No evaluation logic.** Simulators play the customer; they MUST NOT score the employee, mention metrics, or judge red lines. Scoring is STEP 4's job.
7. **`internal_emotion` and `rationale` are NEVER shown to the evaluatee.** Only `next_utterance` is forwarded to the driver. Everything else is audit-only and lives in `simulator_trail`.

## Built-in simulators

| `simulator_id` | `kind` | Notes |
|---|---|---|
| `customer_realistic` | `llm_persona` | Default. Realistic customer respecting persona / goal / stop_conditions. Emits emotion arc + perceived progress per turn. |

Add new simulators (e.g. `customer_calm`, `customer_aggressive`) by dropping a sibling directory with its own `simulator.json` + `system_prompt.md`.
