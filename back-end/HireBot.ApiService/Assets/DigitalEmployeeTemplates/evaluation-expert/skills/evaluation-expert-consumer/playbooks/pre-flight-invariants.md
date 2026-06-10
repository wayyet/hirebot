# 飞前检查不变式

这些不变式必须在宿主 Agent 进入 PRE.A / STEP 0 之前成立。这些是短路检查；任何一项失败都意味着运行不会启动。

Agent 必须以内联方式运行此检查清单（文件系统读取 + 算术运算）——无 LLM 调用。

## 不变式

| # | 不变式 | 检查方式 | 失败时 |
|---|---|---|---|
| 1 | 所有六个热插拔数据层均存在且可读 | stat `./metrics/`、`./test-cases/`、`./runtime-drivers/`、`./simulators/`、`./role-catalog/`、`./employees/`（或其环境变量覆盖根目录） | `block_or_escalate`，并指出缺失路径 |
| 2 | 至少一个 `*.metric.json` 通过 `metric.schema.json` 验证 | 文件系统扫描 + 模式验证 | K1 — `block_or_escalate`（空注册表） |
| 3 | `evaluation_context.runtime_driver.driver_id` 解析为 `EVALUATION_DRIVERS_DIR` 下的目录 | 检查 `./runtime-drivers/<driver_id>/driver.json` 存在且通过 `runtime_driver.schema.json` 验证 | `fail_fast` — 禁止静默默认 |
| 4 | `evaluation_context.runtime_simulator.simulator_id` 解析为 `EVALUATION_SIMULATORS_DIR` 下的目录 | 检查 `./simulators/<simulator_id>/simulator.json` 存在且通过 `simulator.schema.json` 验证 | `fail_fast` — 禁止静默默认 |
| 5 | 所选 simulator 目录包含 `.no-decide-script` 哨兵文件 | stat `./simulators/<simulator_id>/.no-decide-script` | 警告；若目录中存在任何 `.py` / `.sh` / 可执行文件，视为 K8 违规并预先污染 |
| 6 | `evaluation_context.runtime_driver.driver_config` 通过 `driver.json#/config_schema` 验证 | JSON Schema 验证 | `fail_fast` |
| 6a | `evaluation_context.hirebot_api.auth` 存在且完整（`mode == "client_credentials"`、`token_url`/`client_id`/`client_secret` 均非空）。ws_jwt driver 在启动时通过此配置从 Keycloak 换取 Bearer Token，是唯一的 Token 来源。 | 检查 `hirebot_api.auth` 存在且以上字段均非空 | `fail_fast` |
| 7 | `evaluation_context.global_turn_cap` 已设置（缺失时默认 30）且 `1 <= cap <= 50` | 边界检查 | `fail_fast` |
| 8 | 至少一个指标的 `applicable_roles` 覆盖规范化员工的 `role_id`（或 `*`） | 内联过滤 | K9 路径——若 STEP 1 后 `candidate_metrics` 为空则 `block_or_escalate` |
| 9 | `./runs/<eval_id>/` 不存在（不覆盖）或已有目录包含 `TAINTED.md` 且用户明确选择重试 | 路径检查 | `fail_fast`，防止静默覆盖 |
| 10 | 宿主 Agent 自技能创建后未在技能根目录下创建任何可执行文件 | 清点 `./` 下白名单之外的 `.py`/`.sh`/`.ts`/`.js`/`.mjs`/`.ipynb`/`Makefile`/`*.cmd`/`*.ps1` | K8 — 立即污染 |
| 11 | Role_Catalog 目录可读，且（若非空）至少可解析 | 扫描 `EVALUATION_ROLES_DIR`（默认 `./role-catalog/`）；每文件失败为软失败（跳过 + open_question），但目录本身必须可 stat | 仅在目录不可读时 `block_or_escalate`；单个错误文件不阻断（role-catalog K1–K3） |
| 12 | 若需要员工文件，`EVALUATION_EMPLOYEES_DIR`（默认 `./employees/`）可读 | stat 该目录；`<employee_id>.json` 缺失不是失败（STEP 0 回退到用户对话 / 推断） | 仅在目录路径已设置但不可读时 `block_or_escalate` |
| 13 | `evaluation_context.metric_selection_policy`（若存在）具有可解析的默认值 | 验证 `mode` ∈ {auto, always, never}；`max_metrics` [1,100]、`min_dimensions_covered` [1,5]、`auto_apply_threshold` [0,1] 的边界；省略字段采用文档中的默认值 | 显式越界值 `fail_fast`；省略可以接受 |

## 何时运行

- 全新评估开始前在 PRE.A 之前
- 任何环境变更（新 driver、新 simulator、新角色、环境变量覆盖）后
- 从污染运行恢复时，在复用产物之前

## 为什么需要这些不变式

大多数难以调试的评估失败源于静默回退：

- driver_id / simulator_id 的 `silent_default_disallowed`（工作流合同 S3）——没有不变式 3/4，Agent 会愉快地对错误的协议或角色进行评分
- PRE 后空 `metric_registry`（K1）——没有不变式 2，STEP 1 会在工作流深处阻断而不是在入口处
- Agent 在技能根目录下编写的脚本（K8）——不变式 10 在 STEP 3 花费轮次产生污染轨迹之前捕获此问题
- 缺失/不可读的 Role_Catalog 目录（不变式 11）会静默禁用 STEP 0 规范化，使每个角色都走 `role_id_no_catalog_entry` 说明路径——在前期暴露它，可以区分"未配置目录"和"目录配置错误"
- 越界的 `metric_selection_policy`（不变式 13）否则会在运行深处以令人困惑的 STEP 1.2 阻断形式出现

在前期暴露不变式，让运行要么干净启动，要么响亮失败。
