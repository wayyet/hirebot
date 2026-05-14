---
name: live_evaluator
version: 2.0.0
category: evaluation
description: 评估沙箱远程执行驱动 Skill — 检查本地材料，并驱动目标沙箱逐题执行测试用例后采集 trace

tools_required:
  - evaluate.py
  - auth_client.py
  - material_loader.py
  - http_client.py

execution_mode: sequential
memory_access: read_write
---

# 评估沙箱远程执行驱动 Skill

你运行在**评估沙箱**内，负责：

1. 读取运行时上下文
2. 检查评估沙箱本地 `testcases` / `ontology`
3. 生成题卡
4. 对目标沙箱完成鉴权
5. 通过 WebSocket 驱动目标沙箱逐题执行
6. 采集目标沙箱的执行证据并输出 `trace_result.json`

**你不负责评分，不负责直接写数据库。**

## 输入契约

必须通过运行时上下文文件驱动，统一入口如下：

```bash
python evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode inspect|execute \
  --output /tmp/output.json
```

## 模式说明

### inspect

先检查评估沙箱本地材料是否就绪。

输出：

- 是否缺少 `testcases`
- 是否缺少 `ontology`
- 题卡列表
- ontology 权重与规则摘要

### execute

正式执行评估。

执行步骤：

1. 读取 inspect 阶段同一套本地材料
2. 通过 skill 内部鉴权模块获取 token
3. 建立评估沙箱到目标沙箱的 WebSocket
4. 按题卡顺序逐题驱动目标沙箱执行
5. 等待 `assistant_done`
6. 采集 WS 原始消息与 HTTP 补充信息
7. 输出 `trace_result.json`

## 运行时上下文要求

上下文至少包含：

- `session`
- `materials`
- `target_sandbox`（仅含连接元数据：sandbox_id / gateway_endpoint / http_base_url）
- `execution`

## 鉴权配置

鉴权由 skill 内部闭环完成，不依赖运行时上下文注入。

`auth_client.py` 优先使用运行时上下文中的 `target_sandbox.auth`（如果存在），否则自动从同目录下的 `auth_config.json` 加载。当前 `auth_config.json` 默认配置为 `client_credentials` 模式。

`auth_client.py` 支持的鉴权模式：

- `client_credentials`（默认，client_id / client_secret / token_url）
- `static_token`（静态 access_token）
- `password`（用户名密码，兼容保留）

## 输出结构

### inspect 输出

```json
{
  "mode": "inspect",
  "status": "ready|materials_incomplete",
  "materials": {
    "missing": [],
    "testcases": {
      "count": 3,
      "question_cards": []
    },
    "ontology": {
      "dimension_weights": {},
      "dimension_rules": {}
    }
  }
}
```

### execute 输出

```json
{
  "status": "completed",
  "materials": { "...": "..." },
  "question_cards": [ "..."],
  "turns": [ "..."],
  "http_supplement": { "...": "..." }
}
```

## 执行约束

1. 材料来源以评估沙箱本地 workspace 为准。
2. 目标沙箱是业务执行者，评估沙箱只负责驱动和采集。
3. 题卡展示必须来自本地 testcase，而不是现场编造。
4. 不得把明文口令、secret、token 写入输出文件。
5. 采集阶段不得提前做通过/不通过判断。
