# live_evaluator - 评估沙箱远程执行驱动器

`live_evaluator` 运行在**评估沙箱**内，负责两件事：

1. 检查评估沙箱本地的 `testcases` / `ontology` 是否齐备。
2. 使用运行时上下文中的鉴权信息连接**目标沙箱**，逐题驱动目标沙箱执行并采集 trace。

它**不负责评分**，也**不负责直接写数据库**。

## 运行方式

### 1. 材料检查

```bash
python evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode inspect \
  --output /tmp/materials_inspection.json
```

输出结果包含：

- 材料是否就绪
- 缺失项列表
- 题卡（question cards）
- ontology 权重与规则摘要

### 2. 正式执行

```bash
python evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode execute \
  --output /tmp/trace_result.json
```

执行流程：

1. 读取评估沙箱本地材料
2. 根据运行时上下文完成鉴权
3. 建立到目标沙箱的 WebSocket
4. 逐题驱动目标沙箱执行
5. 采集 WS 消息和 HTTP 补充数据
6. 输出 `trace_result.json`

## 运行时上下文

推荐由平台在评估沙箱内写入 `/workspace/runtime/evaluation-context.json`。

关键字段：

```json
{
  "session": {
    "session_id": "EVAL-SESSION-001",
    "employee_id": "EMP-001",
    "employee_name": "客服专员",
    "iteration": 1
  },
  "materials": {
    "workspace_root": "/workspace",
    "template_root": "/workspace/employee-template",
    "template_package_zip": "/workspace/uploads/employee-template.zip",
    "testcases_path": null,
    "ontology_path": null
  },
  "target_sandbox": {
    "sandbox_id": "SB-TARGET-001",
    "gateway_endpoint": "ws://127.0.0.1:18789/ws",
    "http_base_url": "http://127.0.0.1:18789",
    "auth": {
      "mode": "password",
      "token_url": "https://keycloak.example.com/realms/demo/protocol/openid-connect/token",
      "username": "sandbox-evaluator",
      "password": "******",
      "client_id": "gateway-client",
      "client_secret": "******",
      "ws_transport": "query",
      "ws_query_param": "token",
      "http_header_name": "Authorization",
      "http_scheme": "Bearer"
    }
  },
  "execution": {
    "timeout_seconds": 60,
    "http_supplement": true
  }
}
```

另附示例文件：[runtime_context.example.json](/E:/hirebot/back-end/src/HireBot.ApiService/Assets/DigitalEmployeeTemplates/evaluation-expert/live_evaluator/runtime_context.example.json:1)。

## 支持的鉴权模式

- `static_token`
- `password`
- `client_credentials`

## 输出

### `materials_inspection.json`

```json
{
  "mode": "inspect",
  "status": "ready",
  "session": { "...": "..." },
  "materials": {
    "status": "ready",
    "missing": [],
    "testcases": {
      "count": 3,
      "question_cards": [ "..."]
    },
    "ontology": {
      "dimension_weights": { "...": 0.25 },
      "dimension_rules": { "...": { } }
    }
  }
}
```

### `trace_result.json`

```json
{
  "status": "completed",
  "meta": {
    "session_id": "EVAL-SESSION-001",
    "employee_id": "EMP-001",
    "target_sandbox_id": "SB-TARGET-001"
  },
  "materials": { "...": "..." },
  "question_cards": [ "..."],
  "turns": [
    {
      "turn_index": 0,
      "test_case_id": "TC-001",
      "execution_trace": {
        "logs": [ "..."],
        "raw_messages": [ "..."],
        "think_blocks": [ "..."],
        "summary": { "...": "..." }
      }
    }
  ],
  "http_supplement": {
    "runtime_events": {},
    "sessions": {},
    "approval_history": {}
  }
}
```

## 文件说明

| 文件 | 职责 |
|------|------|
| `evaluate.py` | inspect / execute 入口 |
| `auth_client.py` | 鉴权解析与 token 获取 |
| `material_loader.py` | 本地材料发现、解析、题卡生成 |
| `ws_client.py` | WebSocket 连接与消息采集 |
| `http_client.py` | HTTP 补充采集 |
| `trace_builder.py` | trace 结果格式化 |

## 约束

1. 材料来源以评估沙箱本地 workspace 为准，不再从目标沙箱拉取 testcase / ontology。
2. 业务执行者始终是目标沙箱，评估沙箱只负责驱动和采集。
3. 输出结果中不得写入明文口令、client secret 或 access token。
