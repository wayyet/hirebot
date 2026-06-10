# runtime-drivers

`evaluation-expert-consumer` 的第三个热插拔数据层，与 `./metrics/` 和 `./test-cases/` 并列。

**运行时 Driver** 是 STEP 3（`driveEmployeeOnScenario`）用于与被评估沙箱通信的确定性 I/O 适配器。合同层（`contracts/projections/**`）与协议无关；协议专用代码（WebSocket、HTTP、stdio、mock 等）**只**存在于 driver 目录内。

## 热插拔规则

添加新协议（或为测试桩接口）只需**放入一个目录**：

```
runtime-drivers/
└── <driver_id>/
    ├── driver.json     ← 必需，根据 runtime-schemas/runtime_driver.schema.json 验证
    ├── <entry>         ← 必需，driver.json.entry 中命名的可执行文件
    └── ...             ← 任意辅助模块
```

添加新 driver 时，**不需要**编辑任何 `*.projection.json`、`SKILL.md` 或工作流合同。

## 必需的输入/输出合同

每个 driver 无论使用何种协议，都**必须**遵守相同的合同：

| 方向 | 格式 | 模式 |
|---|---|---|
| **输入** | 每次调用一个已丰富化的测试用例，加上运行的 evaluation context（用于路径和 `driver_config`） | `runtime-schemas/enriched_test_case.schema.json` + `runtime-schemas/evaluation_context.schema.json` |
| **输出** | 每次调用恰好一个 ExecutionTrace，写入 `./runs/<eval_id>/traces/<test_case_id>.trace.json` | `runtime-schemas/execution_trace.schema.json` |

如果生成的 JSON 未通过 `execution_trace.schema.json` 验证，STEP 3 **必须**对该场景快速失败；下游 STEP 4 扇出对失败的 `(test_case, *)` 对随后跳过。

## 运行时选择 driver

`evaluation_context.runtime_driver.driver_id` 决定调用 `./runtime-drivers/` 下的哪个目录。解析顺序：

1. `EvaluationContext.runtime_driver.driver_id`（在 STEP 0/1 落盘；通常从用户输入复制）
2. 环境变量 `EVALUATION_DRIVER_ID`
3. 硬性失败（无隐式默认值——driver 与被评估者相关，静默回退会损坏 trace）

`./runtime-drivers/` 目录本身可通过 `EVALUATION_DRIVERS_DIR` 重定位。

## `driver.json` 最小结构

```json
{
  "driver_id": "ws_jwt",
  "version": "1.0.0",
  "protocol": "websocket+jwt",
  "entry": "run.py",
  "language": "python",
  "produces": "runtime-schemas/execution_trace.schema.json",
  "consumes": [
    "runtime-schemas/evaluation_context.schema.json",
    "runtime-schemas/enriched_test_case.schema.json"
  ],
  "capabilities": {
    "supports_multi_turn": true,
    "supports_tool_call_observation": true,
    "supports_auto_approval": true
  },
  "config_schema": {
    "type": "object",
    "required": ["endpoint", "token"],
    "properties": {
      "endpoint": { "type": "string", "description": "HOST:PORT 或完整的 ws:// URL" },
      "token":    { "type": "string", "description": "JWT Bearer Token" },
      "timeout":  { "type": "integer", "default": 60 }
    }
  }
}
```

该文件根据 `runtime-schemas/runtime_driver.schema.json` 验证。STEP 3 每次评估运行读取一次，然后在调用 `entry` 前根据内嵌的 `config_schema` 验证 `evaluation_context.runtime_driver.driver_config`。

## Driver 编写者的硬性规则

1. **输出必须是 ExecutionTrace，而非原始会话记录。** 如果你的协议产生其他内容，你的 `entry` 必须在写入前将其转换。
2. **不含评估逻辑。** Driver 只负责观察和持久化；**不得**评分、判断红线或过滤信号。
3. **不得静默丢弃。** 来自被评估者的未知消息应落入 `actual_tool_calls` / `dialog_turns` / `actual_artifacts`（使用合理的枚举兼容分类），或触发 `evaluatee_error` 终止——绝不丢弃。
4. **不得写入 `./runs/<eval_id>/` 以外的位置。** 所有磁盘操作均属于运行目录。
5. **每次调用只生成一个 ExecutionTrace。** 多测试用例批处理是工作流的职责，不是 driver 的。

## 内置 driver

| `driver_id` | 协议 | 说明 |
|---|---|---|
| `ws_jwt` | `websocket+jwt` | 通过 WS 连接到 OpenClaw Gateway，JWT 放在 URL query 参数中。支持多轮；自动审批工具调用。实现在 consumer runtime-driver 层内部。 |

通过放入同级目录（含自己的 `driver.json`）来添加新 driver。
