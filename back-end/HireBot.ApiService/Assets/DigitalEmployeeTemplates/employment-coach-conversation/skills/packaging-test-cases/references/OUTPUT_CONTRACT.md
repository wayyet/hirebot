# 打包前测试用例输出契约

## 与 live_evaluator 的对齐

主文件 JSON 须与 `evaluation-expert/skills/live_evaluator/test_cases/customer_service_demo.json` 形态一致，供评估沙箱 `material_loader.py` 解析。

来源子文件与 index 放在 `ontology/hiring-session/`，避免评估器重复扫描 `testcases/` 下多个 JSON。

## 输入字段（invoke payload）

| 字段 | 类型 | 说明 |
|------|------|------|
| `uploaded_material_files[]` | array | `relative_path`、`original_file_name`、`requested_category_title`、`content` |
| `template_package_files[]` | array | `relative_path`、`content`（manifest/skills/ontology/config 快照） |
| `history_messages[]` | array | 过滤后的 user/assistant 转录 |

## 输出文件

| 路径 | 说明 |
|------|------|
| `testcases/evaluation-test-cases.json` | **主文件**：合并去重后的 `test_cases[]` |
| `ontology/hiring-session/evaluation-test-cases.json` | 与主文件相同 |
| `ontology/hiring-session/testcases-sources-index.json` | 来源索引 |
| `ontology/hiring-session/testcases-sources/history-derived.json` | 仅基于对话历史 |
| `ontology/hiring-session/testcases-sources/materials-derived.json` | 仅基于上传资料 |
| `ontology/hiring-session/testcases-sources/template-derived.json` | 仅基于模板快照 |

## 顶层字段（主文件与子文件）

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `description` | string | 是 | 用例集描述 |
| `role` | string | 是 | 数字员工角色标识 |
| `industry` | string | 是 | 行业 |
| `test_cases` | array | 是 | 主文件 ≥1 条；子文件可为 `[]` |
| `generated_at` | string (ISO8601) | 否 | 可由后端追加 |
| `source` | string | 是 | 见下表 |

### source 取值

| 文件 | source |
|------|--------|
| 主文件 | `packaging-merged` |
| history 子文件 | `history-derived` |
| materials 子文件 | `materials-derived` |
| template 子文件 | `template-derived` |
| 降级 | `packaging-fallback` |

## test_cases[] 单条

| 字段 | 说明 |
|------|------|
| `test_case_id` | 如 `TC-001` |
| `scenario_name` | 场景标题 |
| `input.user_request` | 用户请求原文风格 |
| `input.context` | 业务上下文对象 |
| `expected_behavior_sequence` | 至少 2 步 |
| `expected_output` | 含 `resolution`、`user_satisfaction`、`artifacts_created` |

## testcases-sources-index.json

```json
{
  "generated_at": "2026-05-28T12:00:00Z",
  "primary": "testcases/evaluation-test-cases.json",
  "sources": {
    "history": "ontology/hiring-session/testcases-sources/history-derived.json",
    "materials": "ontology/hiring-session/testcases-sources/materials-derived.json",
    "template": "ontology/hiring-session/testcases-sources/template-derived.json"
  },
  "inputs_summary": {
    "history_turns": 12,
    "material_files": 3,
    "template_files": 18
  }
}
```

## dispatch_callback.technical_artifact

| 字段 | 说明 |
|------|------|
| `source` | 与主文件 `source` 一致 |
| `evaluation_test_cases_json` | 主文件完整 JSON 字符串 |
| `testcases_sources_index_json` | index 完整 JSON 字符串 |
| `history_derived_json` | history 子文件 JSON 字符串 |
| `materials_derived_json` | materials 子文件 JSON 字符串 |
| `template_derived_json` | template 子文件 JSON 字符串 |

后端从上述字段同步 `WorkingTemplatePackage`，不依赖读取沙箱工作区文件 API。
