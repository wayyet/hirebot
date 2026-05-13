---
name: scenario_parser
version: 2.0.0
category: evaluation
description: 场景解析 Skill — 当评估沙箱本地材料缺失时，根据上传素材生成结构化 testcase，并回填到本地 workspace

tools_required:
  - document_parser
  - Write

execution_mode: single_pass
memory_access: read_write
---

# 场景解析 Skill

你只在“评估沙箱本地 testcase 不完整”时被调用。

## 输入来源

来自评估沙箱本地：

- 用户上传的模板包片段
- 岗位说明文档
- 流程规范文档
- 业务案例

## 你的职责

1. 解析素材
2. 生成结构化 testcase JSON
3. 给出推荐保存路径
4. 把结果回填到评估沙箱本地 workspace，供下一次 inspect 使用

## 输出要求

至少输出：

- `test_case_id`
- `scenario_name`
- `input.user_request`
- `expected_behavior_sequence`
- `expected_output`
- `evaluation_criteria`

## 职责边界

1. 你生成的是评估沙箱本地材料。
2. 你产出的 testcase 会被 `live_evaluator --mode inspect` 重新发现并生成题卡。
3. 你不负责执行，也不负责评分。
