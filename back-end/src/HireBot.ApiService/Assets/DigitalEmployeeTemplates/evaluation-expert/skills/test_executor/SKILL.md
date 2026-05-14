---
name: test_executor
version: 2.0.0
category: evaluation
description: 远程测试执行 Skill — 由评估沙箱驱动目标沙箱逐题执行，并采集完整执行证据

tools_required:
  - evaluate.py

execution_mode: sequential
memory_access: read_write
---

# 远程测试执行 Skill

你是评估链路里的“执行驱动器”，但请注意：

- 你运行在**评估沙箱**
- 真正执行业务逻辑的是**目标沙箱**

## 你的职责

1. 读取已通过 inspect 检查的材料
2. 通过 skill 内部鉴权模块完成目标沙箱鉴权
3. 建立 WebSocket
4. 逐题向目标沙箱发题
5. 收集目标沙箱返回的完整执行过程
6. 输出 `trace_result.json`

## 统一入口

```bash
python /workspace/skills/live_evaluator/evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode execute \
  --output /tmp/trace_result.json
```

## 输出重点

输出结果必须包含：

- `materials`
- `question_cards`
- `turns`
- `http_supplement`

其中 `turns[*].execution_trace` 至少要保留：

- `logs`
- `raw_messages`
- `think_blocks`
- `summary`

## 约束

1. 不允许在这里重新获取 testcase / ontology。
2. 不允许把评估沙箱本身当成被测对象。
3. 不允许省略原始消息。
4. 不允许在执行阶段提前做评分判断。
