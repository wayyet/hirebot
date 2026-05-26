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
  - verdict_uploader.py
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

## 阶段化产物约束

评估会话需要像雇佣流程一样持续推送阶段产物，供前端渲染左侧阶段进度和右侧报告入口。

阶段 data 结构见 [references/stage-data-schema.md](references/stage-data-schema.md)。
artifact 生命周期与阶段门禁定义见 [contracts/artifacts.json](contracts/artifacts.json)。

推荐的 artifact 发出节奏：

| 阶段 | artifactType | 说明 |
|------|-------------|------|
| 双沙箱就绪 / 材料装载 | `evaluation_workspace_progress` | 目标沙箱先创建，拿到 `gateway_endpoint` 后再推进评估沙箱 |
| 题卡展示 | `evaluation_question_cards` | 把已解析题卡摘要推给前端 |
| 逐题执行中 | `evaluation_execution_progress` | 告知当前 testcase、完成数量、预估得分 |
| 报告已生成 | `evaluation_report_ready` | 配合 HTML 文件 artifact 提供下载入口 |

如果环境支持 `emit_artifact`：

- 准备阶段发 `kind=data`
- 报告阶段同时发 `kind=file` 的 HTML 报告和 `kind=data` 的 `evaluation_report_ready`

如果环境暂不支持 `emit_artifact`，也必须按照上述字段结构组织内部状态和回复文本，保持前后端可对齐。

## 你必须遵守的边界

1. **材料在评估沙箱本地**，不要去目标沙箱拉 testcase / ontology。
2. **目标沙箱才是业务执行者**，评估沙箱只是驱动者与裁判。
3. **鉴权由 skill 内部闭环完成**（`auth_config.json`），不要向用户索要 endpoint/token。
4. **报告最终要持久化到数据库**，但通过 `evaluation_report` 或平台注入的后端接口完成。

## 执行流程

### 运行模式

本 Skill 有两种模式，由收到消息的 `workflow` 字段决定：

**Bootstrap 模式** — 消息含 `"workflow": "live_evaluation"` 和 `runtime_context`：
全自动管道执行。**不展示题卡，不等待用户交互，不输出 think/analysis。**
顺序跑完：阶段 0→1→3→4→5→6，最终只输出 verdict JSON。

**交互模式** — 普通对话消息：
各阶段之间与用户交互，展示题卡，等待确认。

### 阶段 0：写入运行时上下文

**首先**，从当前会话的用户消息中提取 `runtime_context` JSON 对象，将其完整写入文件：

```bash
# 运行时上下文必须写入此路径，evaluate.py 从该路径读取
/workspace/runtime/evaluation-context.json
```

确保目标目录存在：`mkdir -p /workspace/runtime`

### 阶段 0.5：安装 Python 依赖

**在执行任何 Python 脚本前**，必须先确保 `websockets` 已安装，否则 `evaluate.py` 会在运行时报 `ModuleNotFoundError`。

沙箱环境已自带 Python 和 pip，直接安装即可：

```bash
pip install --quiet websockets>=12.0
```

安装完成后，**后续所有 Python 脚本统一用 `python3` 或 `python` 执行**。

### 阶段 1：检查本地材料

先调用：

```bash
python3 /workspace/skills/live_evaluator/evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode inspect \
  --output /tmp/materials_inspection.json
```

检查结果：

- 若 `status = ready`
  - 推送一次 `evaluation_workspace_progress`，明确目标沙箱 `gateway_endpoint`、材料状态和题卡数量
  - 进入下一阶段
- 若 `status = materials_incomplete`
  - 推送一次 `evaluation_workspace_progress`，标记缺失 `testcases` / `ontology`
  - 告诉用户缺什么
  - 引导用户把模板包或缺失材料上传到评估沙箱
  - 必要时调用 `scenario_parser` 生成 testcase
  - 重新执行 inspect

### 阶段 2：展示题卡

从 inspect 结果中读取 `question_cards`，在对话中展示。

展示完成后，推送一次 `evaluation_question_cards`，让前端同步右侧题卡列表。

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
python3 /workspace/skills/live_evaluator/evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode execute \
  --output /tmp/trace_result.json
```

这里的“执行”指：

- 评估沙箱通过 WebSocket 把题目发送给目标沙箱
- 由目标沙箱真正执行业务逻辑
- 评估沙箱采集返回的消息、工具调用、思考块、状态变化

执行过程中按 testcase 粒度推送 `evaluation_execution_progress`。

### 阶段 4：执行评分

**重要：这里没有可调用的评分脚本。评分由你自己作为 AI 完成，不要尝试执行任何 Python 文件，不要去 `/workspace/skills/evaluator/` 寻找脚本。**

执行步骤：

1. 读取 `/workspace/skills/evaluator/SKILL.md`，获取红线原则、评分哲学和各维度评分规则
2. 读取本地 testcase（`/workspace/uploads/materials/testcases/`）
3. 读取本地 ontology（`/workspace/uploads/materials/ontology/`）
4. 读取 `/tmp/trace_result.json` 中目标沙箱的真实执行 turns
5. **你（AI）按照 evaluator SKILL.md 的规则，对每道题的 turns 逐条打分**，将结果写入 `/tmp/evaluation_result.json`：

```json
{
  "session_id": "<session_id>",
  "employee_id": "<employee_id>",
  "verdict": "PASS",
  "overall_score": 75.8,
  "dimensions": {
    "functional_completeness": { "score": 80, "evidence": "...", "issues": [] },
    "tool_call_accuracy":      { "score": 70, "evidence": "...", "issues": [] },
    "process_compliance":      { "score": 75, "evidence": "...", "issues": [] },
    "interaction_quality":     { "score": 77, "evidence": "...", "issues": [] }
  },
  "red_line_triggered": false,
  "red_line_details": [],
  "per_question": []
}
```

评分必须证据驱动，每个分数都要引用 trace 中的具体内容。

### 阶段 5：生成报告

调用 `report_generator`，生成：

- `evaluation_result.json`
- `evaluation_report.html`

生成后需把 HTML 报告作为文件 artifact 推给前端，并再推送一次 `evaluation_report_ready` data artifact。

### 阶段 6：持久化

#### 阶段 6a：上传执行轨迹（失败不阻断后续流程）

调用 `trace_uploader.py` 把执行轨迹上传到 HireBot 后端：

```bash
python3 /workspace/skills/live_evaluator/trace_uploader.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --trace-result /tmp/trace_result.json \
  --output /tmp/trace_upload_result.json
```

脚本会自动从运行时上下文中读取以下配置：

| 配置项 | 来源 |
|--------|------|
| HireBot 后端地址 | `runtime_context.ncrew_hire.base_url` |
| API Token | 由脚本内部通过 `auth_client.resolve_auth()` 从 `auth_config.json` 自动获取，无需手动传入 |

上传成功后，后端将完成：

- 执行轨迹原始 JSON 资产存储（`EvaluationAssets` 表，`assetType = "trace-json"`）

读取 `/tmp/trace_upload_result.json`，若 `status != "success"`，输出告警，**继续执行阶段 6b，不阻断**。

#### 阶段 6b：上传评估结果

调用 `verdict_uploader.py` 把评估结果上传到 HireBot 后端：

```bash
python3 /workspace/skills/live_evaluator/verdict_uploader.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --evaluation-result /tmp/evaluation_result.json \
  --output /tmp/verdict_upload_result.json
```

脚本会自动从运行时上下文中读取以下配置：

| 配置项 | 来源 |
|--------|------|
| HireBot 后端地址 | `runtime_context.ncrew_hire.base_url` |
| API Token | 由脚本内部通过 `auth_client.resolve_auth()` 从 `auth_config.json` 自动获取，无需手动传入 |

上传成功后，后端将完成：

- 评估报告落库（`EvaluationReports` 表）
- 原始 verdict JSON 资产存储（`EvaluationAssets` 表）
- 员工状态流转（AI 通过 → `interning_human`，AI 不通过 → `failed`）

读取 `/tmp/verdict_upload_result.json`，确认 `status = "success"`。
若上传失败，输出报错并告知用户，不阻塞报告展示。

如果平台已注入 `evaluation_report` 工具，可同时调用以完成文件资产的上传；
若未注入，仅用 `verdict_uploader.py` 完成核心状态同步即可。

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
| 目标沙箱鉴权失败 | 提示检查 `auth_config.json` 配置或凭据过期 |
| 目标沙箱执行超时 | 返回失败结果并保留已采集 trace |
| 轨迹持久化失败 | 明确说明执行轨迹落库失败，不阻断 verdict 上传 |
| 报告持久化失败 | 明确说明评分已完成，但后端落库失败 |

## 禁止事项

1. 禁止以“用户手填 endpoint + token + 本地 testcase 文件”作为执行入口。
2. 禁止把目标沙箱当成评分器。
3. 禁止在没有题卡和本体的情况下直接开始评分。
4. 禁止把 access token、密码、client secret 回显到对话中。
