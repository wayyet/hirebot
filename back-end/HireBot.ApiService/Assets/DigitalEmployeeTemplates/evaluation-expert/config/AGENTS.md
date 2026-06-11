# AGENTS

> 详细执行规程见 `skills/evaluation-expert-consumer/playbooks/`。本文件仅记录**工具级硬性约束**和**自愈规则**，不重复 playbook 内容。

---

## ⛔ 工具绝对禁令

**本规则优先于所有其他指令。**

| 禁止模式 | 原因 |
|---|---|
| `process_*`（含下划线） | 会向被评估系统写入真实业务数据，污染测试结果 |
| `*_session`、`session_*`、`*_session_*` | 会话生命周期由目标沙箱和 Gateway 管理，评估方不得干预 |

**豁免（可正常使用）：**

| 工具名 | 说明 |
|---|---|
| `process` | 精确匹配，系统级本地进程管理器，用于 STEP 3 管理 ws_jwt driver 子进程 |
| `sessions` | 精确匹配，Agent 间消息桥接，非会话生命周期管理 |

违规时立即停止并输出：`[TOOL BAN] Refused to call <tool_name>: matches banned pattern`

---

## ⛔ evaluation-context.json 权威来源

| 路径 | 状态 |
|---|---|
| `/workspace/runtime/evaluation-context.json` | ✅ 唯一合法来源（含完整 `client_secret`） |
| `runs/<eval_id>/evaluation_context.json` 或任何 run_dir 副本 | ❌ 禁止（`client_secret` 已 REDACTED，driver 会 401） |

所有步骤（STEP 2.5、STEP 3 spawn、STEP 10 上传）均必须**硬编码** `/workspace/runtime/evaluation-context.json`。

---

## 自愈规则（无需等待用户确认）

| 情况 | 处理方式 |
|---|---|
| `paths.run_dir` 不存在 | 写产物前自动创建目录，不阻塞 |
| `test-cases/` 为空或只有 `default_connectivity_testcases.json` | `test_case_status = \"missing\"`，直接进入 STEP 1.5 |
| `driver_config.token` 缺失 | 不阻塞；`run.py` 会通过 `hirebot_api.auth` 自动换取 token |
| spawn 超时或 PID 为空 | 自动重新执行 STEP 2.5，生成新 `run_plan.json`，不询问用户 |

---

## 材料路径

| 用途 | 路径 |
|---|---|
| 运行时上下文 | `/workspace/runtime/evaluation-context.json` |
| 材料根目录 | `/workspace/uploads/evaluation-expert-consumer` |
| 测试用例 | `/workspace/uploads/evaluation-expert-consumer/test-cases` |
| 本体材料 | `/workspace/uploads/evaluation-expert-consumer/ontology` |
| **被评估员工模板资料**（SOP、角色定义等，生成评估用例时的参考依据） | `/workspace/uploads/artifact` |
| 运行产物 | `evaluation_context.paths.run_dir` |

> STEP 1.5 合成测试用例时，若需参考员工 SOP 或角色职责，从 `/workspace/uploads/artifact` 读取；不得凭空捏造场景。
