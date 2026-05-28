---
name: packaging-test-cases
description: 在打包前根据雇佣会话历史、待办上传资料与实例包模板快照生成 live_evaluator 兼容的 evaluation-test-cases.json，写入 testcases/ 与来源 index/子文件，并返回 dispatch_callback 供后端同步。
compatibility: HireBot employment-coach-conversation v1.0
metadata:
  category: generation
  autonomy: 85
  trigger: hiring-session-packaging, ready-for-packaging, backend-staging-invoke
  input: invoke_packaging_testcases, session-history, uploaded-materials, template-package-snapshot
  output: testcases-json, sources-index, dispatch-callback
---

# 打包前评估测试用例生成

## 何时使用

在以下任一情况**必须**执行本 Skill：

- 收到后端注入的 `<invoke_packaging_testcases>...</invoke_packaging_testcases>` 消息
- 雇佣流程已进入 `ready_for_packaging`，且工作区尚不存在有效的 `testcases/evaluation-test-cases.json`
- 用户即将调用 `package_workspace` 打包实例包

**不要**在资料/技能/外部阶段中途生成 testcase；**不要**代替 `employment-coach-conversation` 执行打包。

## 输入契约

解析 `<invoke_packaging_testcases>` 内的 JSON，字段：

| 字段 | 说明 |
|------|------|
| `session_id` | KingCrab 会话 ID |
| `template_name` | 模板名称 |
| `structured_data` | 结构化业务字段（键值对） |
| `history_messages` | 已过滤的对话转录，`role` 为 `user` / `assistant`（可为空） |
| `uploaded_material_files` | 待办面板上传的资料正文（`.md`/`.json`，含 `requested_category_title`） |
| `template_package_files` | 实例包文本快照（`manifest.json`、`skills/**/SKILL.md`、`ontology/**`、`config/**` 等） |

若 **history / materials / template 三者皆空**，**不得**编造用例，直接走降级输出（见下文）。

## 生成要求

你是数字员工评估测试用例编写专家。综合三类输入生成用于 **live_evaluator** 的 JSON 文件。

1. 分别基于 **history / materials / template** 各生成 0～8 条用例（输入不足时可少于 3 条；无输入则 `test_cases: []`）
2. **合并去重**后写入主文件 `testcases/evaluation-test-cases.json`，`source: packaging-merged`
3. 分别写入来源子文件（路径固定）：
   - `ontology/hiring-session/testcases-sources/history-derived.json`（`source: history-derived`）
   - `ontology/hiring-session/testcases-sources/materials-derived.json`（`source: materials-derived`）
   - `ontology/hiring-session/testcases-sources/template-derived.json`（`source: template-derived`）
4. 写入索引 `ontology/hiring-session/testcases-sources-index.json`（见 [references/OUTPUT_CONTRACT.md](references/OUTPUT_CONTRACT.md)）
5. 字段名使用 **snake_case**；每条含 `test_case_id`、`scenario_name`、`input.user_request`、`expected_behavior_sequence`（≥2 步）、`expected_output`
6. 从真实业务内容提炼场景；**忽略**打包/生成实例包/系统 priming 类消息
7. 结构示例见 [templates/TEMPLATE.json](templates/TEMPLATE.json)

## 写入工作区（强制）

使用沙箱文件写入工具，**必须**写入上述 5 个 JSON 文件；其中主文件与 ontology 副本内容相同：

- `testcases/evaluation-test-cases.json`
- `ontology/hiring-session/evaluation-test-cases.json`

## 返回 dispatch_callback（强制）

在助手回复中输出 XML 标签 `<dispatch_callback>`，JSON 结构遵循 [examples/ready/packaging-test-cases-dispatch-callback.json](examples/ready/packaging-test-cases-dispatch-callback.json)。

关键字段：

- `source_dispatch_target`: `"packaging-test-cases"`
- `status`: `"success"` 或 `"fallback"`
- `technical_artifact.source`: `"packaging-merged"` 或 `"packaging-fallback"`
- `technical_artifact.evaluation_test_cases_json`: 合并主文件完整 JSON 字符串
- `technical_artifact.testcases_sources_index_json`: index 完整 JSON 字符串
- `technical_artifact.history_derived_json` / `materials_derived_json` / `template_derived_json`: 各来源子文件 JSON 字符串
- `artifacts[]`: 至少一项 `path` 为 `testcases/evaluation-test-cases.json`

## 降级

当三类输入皆空、无法提炼业务场景或生成校验失败时：

1. 写入占位 JSON（`test_cases: []`，见 [examples/ready/packaging-fallback-dispatch-callback.json](examples/ready/packaging-fallback-dispatch-callback.json)）
2. `technical_artifact.source` = `"packaging-fallback"`
3. `status` = `"fallback"`

## 与雇佣教练的关系

- `employment-coach-conversation` **不得**自行编造 testcase 内容
- 本 Skill 完成后，coach 在 `package_workspace` 白名单中**必须**包含已有 `testcases/` 目录

详细字段说明见 [references/OUTPUT_CONTRACT.md](references/OUTPUT_CONTRACT.md)。
