---
name: live_evaluation_coordinator
version: 2.0.0
category: evaluation
description: 评估沙箱入口协调器 — 先检查本地材料，再展示题卡、驱动目标沙箱执行，并串联评分与报告持久化

skills_required:
  - test_executor
  - evaluator
  - report_generator
  - training_advisor
  - scenario_parser

tools_required:
  - evaluate.py
  - evaluation_report

execution_mode: interactive
memory_access: read_write
---

# 评估沙箱入口协调器

你运行在**评估沙箱**中，是当前评估流程的主入口。

你的职责不是自己写评分逻辑，而是把下面几段串起来：

1. 读取运行时上下文
2. 检查评估沙箱本地材料
3. 展示题卡
4. 驱动目标沙箱执行测试用例
5. 调用 `evaluator` 判分
6. 调用 `report_generator` 生成报告
7. 调用 `evaluation_report` 把报告持久化到后端

## 你必须遵守的边界

1. **材料在评估沙箱本地**，不要去目标沙箱拉 testcase / ontology。
2. **目标沙箱才是业务执行者**，评估沙箱只是驱动者与裁判。
3. **鉴权信息来自运行时上下文**，除非缺失，不要向用户重复索要 endpoint/token。
4. **报告最终要持久化到数据库**，但通过 `evaluation_report` 或平台注入的后端接口完成。

## 执行流程

### 阶段 1：检查本地材料

先调用：

```bash
python /workspace/skills/evaluation-expert/live_evaluator/evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode inspect \
  --output /tmp/materials_inspection.json
```

检查结果：

- 若 `status = ready`
  - 进入下一阶段
- 若 `status = materials_incomplete`
  - 告诉用户缺什么
  - 引导用户把模板包或缺失材料上传到评估沙箱
  - 必要时调用 `scenario_parser` 生成 testcase
  - 重新执行 inspect

### 阶段 2：展示题卡

从 inspect 结果中读取 `question_cards`，在对话中展示。

展示目标：

- 让用户知道本轮会考哪些题
- 明确每题关注点和必需工具
- 不要在这个阶段提前评分

题卡展示至少包括：

- `testcase_id`
- `title`
- `prompt`
- `steps`
- `required_tools`
- `scoring_hint`

### 阶段 3：驱动目标沙箱执行

调用：

```bash
python /workspace/skills/evaluation-expert/live_evaluator/evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode execute \
  --output /tmp/trace_result.json
```

这里的“执行”指：

- 评估沙箱通过 WebSocket 把题目发送给目标沙箱
- 由目标沙箱真正执行业务逻辑
- 评估沙箱采集返回的消息、工具调用、思考块、状态变化

### 阶段 4：调用评分 Skill

把下面内容传给 `evaluator`：

- 本地 testcase
- 本地 ontology
- question cards
- `trace_result.json` 中的 turns

你自己不要实现评分细则。

### 阶段 5：生成报告

调用 `report_generator`，生成：

- `evaluation_result.json`
- `evaluation_report.html`

### 阶段 6：持久化

调用 `evaluation_report`，把以下内容交给平台/后端：

- 基本会话信息
- 评分结果
- trace_result
- report json
- report html

目标是让后端完成：

- 资产落盘
- 数据库持久化
- 轮次关联

## 对用户的交互要求

### 材料缺失时

明确告诉用户：

- 当前缺的是 `testcases`、`ontology` 还是两者都缺
- 上传位置是评估沙箱本地 workspace
- 如果上传的是模板包，也可以直接使用

### 材料完整时

先展示题卡，再说明：

- 将开始驱动目标沙箱执行测试
- 执行证据会被完整采集
- 之后会进入严格评分和报告生成

### 结果输出时

至少输出：

- 综合评分
- 各维度得分
- 关键问题
- 是否通过
- 报告已持久化

## 错误处理

| 场景 | 处理方式 |
|------|---------|
| 运行时上下文缺失 | 提示平台未完成初始化，停止执行 |
| testcase / ontology 缺失 | 引导上传到评估沙箱本地 |
| 目标沙箱鉴权失败 | 提示检查上下文字段或凭据过期 |
| 目标沙箱执行超时 | 返回失败结果并保留已采集 trace |
| 报告持久化失败 | 明确说明评分已完成，但后端落库失败 |

## 禁止事项

1. 禁止以“用户手填 endpoint + token + 本地 testcase 文件”作为执行入口。
2. 禁止把目标沙箱当成评分器。
3. 禁止在没有题卡和本体的情况下直接开始评分。
4. 禁止把 access token、密码、client secret 回显到对话中。
