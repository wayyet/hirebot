# AI 评估阶段产物 data 结构

本文件定义 `evaluation-expert` 在评估会话中推荐发出的阶段化 artifact payload，供前端渲染评估进度、题卡摘要、执行状态和报告下载入口。

## artifactType 列表

### evaluation_workspace_progress

用于双沙箱和材料准备阶段。

```json
{
  "target_sandbox": {
    "sandbox_id": "747e6801-f391-4bc9-92bd-7c78c63ba039",
    "gateway_endpoint": "opensandbox-gateway.ai4c.cn/747e6801-f391-4bc9-92bd-7c78c63ba039/18789",
    "status": "ready"
  },
  "evaluator_sandbox": {
    "sandbox_id": "a48f4c0b-ff86-4f4d-8d0c-5e9d18da8f30",
    "status": "running"
  },
  "materials": {
    "template_uploaded": true,
    "runtime_context_ready": true,
    "question_card_count": 0,
    "ontology_ready": true,
    "testcases_ready": true
  },
  "summary": "目标沙箱已就绪，评估沙箱正在装载模板和运行时上下文。"
}
```

### evaluation_question_cards

用于左侧会话和右侧题卡区展示阶段摘要。

```json
{
  "total_items": 3,
  "items": [
    {
      "testcase_id": "TC-001",
      "title": "电商商品质量申诉处理",
      "prompt": "用户申请某商品质量有问题，要求退货退款",
      "status": "ready"
    }
  ],
  "summary": "已生成 3 张评估题卡，可开始逐题执行。"
}
```

### evaluation_execution_progress

用于执行中状态。

```json
{
  "session_id": "eval_20260515120000_xxx",
  "current_testcase_id": "TC-001",
  "completed_count": 1,
  "total_count": 3,
  "score_preview": 72,
  "status": "running",
  "summary": "正在驱动目标沙箱执行第 2/3 题。"
}
```

### evaluation_report_ready

用于顶部报告入口和下载动作。

```json
{
  "report_id": "report_xxx",
  "overall_score": 85,
  "passed": true,
  "report_file_name": "evaluation_report.html",
  "report_mime_type": "text/html",
  "summary": "评估完成，HTML 报告已生成并可下载。"
}
```

## 通用约束

- `target_sandbox.gateway_endpoint` 必须使用真实 `gatewayEndpoint`，不要要求用户手填。
- `summary` 始终面向业务用户，不暴露 token、client_secret、password。
- 进入正式执行前，至少应发出一次 `evaluation_workspace_progress` 和一次 `evaluation_question_cards`。
- 报告生成后，应发出文件类 artifact，并配合 `evaluation_report_ready` data artifact 通知前端更新下载区。