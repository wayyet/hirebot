# evaluation-expert-consumer（中文导读）

> 本文档是给人看的中文索引。Skill 行为的**权威定义**在 [`SKILL.md`](./SKILL.md) 与契约文件中；本文与 SKILL.md 不一致时，以 SKILL.md 为准。

## 这个 skill 是做什么的

当用户说"评估这位员工"、"绩效评估"、"打分"、"作为评估专家"时，会触发本 skill。

它执行**一条与员工角色无关、对所有员工通用的 13 步评估流水线**。每个角色之间的差异不在流程上，而是在**六层热插拔的数据**里：

- `./role-catalog/` —— 角色权威目录（STEP 0 规范化用，一角色一文件 `*.role.json`）
- `./employees/` —— 员工权威档案（STEP 0 最高优先级解析源，一员工一文件 `<employee_id>.json`）
- `./metrics/` —— 评估指标（一指标一文件）
- `./test-cases/` —— 测试用例（一用例一文件）
- `./runtime-drivers/` —— STEP 3 通信协议适配器（如 `ws_jwt`）
- `./simulators/` —— STEP 3 客户角色档案（评估专家 agent 用自己的 LLM 扮真人客户时使用的 system_prompt + 人设档案，**不是子进程**）

新增一个角色 = 往 `./role-catalog/` 放一个 `*.role.json` 文件，**不需要改任何 `*.projection.json`**。
新增一个指标 = 往 `./metrics/` 放一个 `*.metric.json` 文件，同理。
新增一个测试用例 = 往 `./test-cases/` 放一个 `*.tc.json` 文件，同理。

## 13 步流水线（高层视角）

```
PRE.A 载入角色目录 → STEP 0 解析员工(文件/对话/推断)+规范化角色 → PRE 载入指标
  → STEP 1 (candidate_metrics) → STEP 1.2 curate (selected_metrics)
  → 1.5（缺用例时）→ 2 →  ┌── 3 → 4 ──┐ × N → 5 → 6 → 7 → 8（每场景）→ 9（总报告）
                          └────────────┘                            ↓
                                                              JSON + HTML 报告
```

各步详细操作手册位于 [`./playbooks/`](./playbooks/README.md)：

- 步骤 0（PRE.A 载入角色目录 + 解析员工三源链 + 角色规范化）
- 步骤 1（角色过滤 → candidate_metrics）
- 步骤 1.2（LLM 裁定 → selected_metrics = (candidate − removed) ∪ added）
- 步骤 1.5（用户优先 / SOP 兜底的合成链）
- 步骤 3（driver 子进程 + 宿主 LLM 模拟器的对偶循环）
- 步骤 4（per-(case, metric) 扇出打分）
- 步骤 5/6/7（确定性聚合 + 维度 roll-up + 红线判定）
- 步骤 9（双格式 JSON + HTML 总报告）
- K-rules 速查表（K1–K18）
- 启动前不变量（pre-flight）
- Tainted 评估的生命周期与恢复

## 执行铁律（HARD RULES）

详细规则与反模式在 SKILL.md 与各 playbook 中。要点：

1. **不写编排脚本（K8）**。除了 `./runtime-drivers/<driver_id>/` 内已提交的 driver 实现，agent **不得**在 skill 任何位置生成 `.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / `Makefile` / `*.cmd` / `*.ps1`。STEP 3 的循环靠 agent 的对话回合驱动，而不是脚本。
2. **driver 是被调用的，不是被复刻的**。STEP 3 通过 shell 派生 driver 子进程，靠 stdin/stdout JSON 协议通信。
3. **simulator 用 agent 自己的 LLM**，不开子进程，不配独立 API key。
4. **`./runs/<eval_id>/` 里只有数据**。任何可执行代码、agent 草稿都禁止。
5. **PRE / 1 / 2 / 5 / 6 / 7 是确定性的**，agent 内联完成，不调 LLM。
6. **1.5 / 4 / 8 / 9 是 LLM 步骤**，调用宿主 agent 自身的 LLM brain，不要写一个 HTTP 客户端代它。

## 上游 producer 依赖

| Producer | 它发布的契约 | 本 skill 在哪里读 |
|---|---|---|
| `ontology_extraction` | 工作流契约 / 评分判分 prompt 约束 / 指标选取 prompt 约束 | `contracts/projections/ontology_extraction/` |
| `metric-ontology` | `metric-catalog` projection 与 `metric.schema.json` | 契约从 `contracts/projections/metric-ontology/` 读，数据从 `./metrics/*.metric.json` 读 |
| `testcase-ontology` | `test-case-catalog` projection 与 `test-case.schema.json` | 契约从 `contracts/projections/testcase-ontology/` 读，数据从 `./test-cases/*.tc.json` 读 |

`ontology_extraction/contract-index.json` 顶层显式声明了 `upstream_producer_dependencies`，运行时一并加载这两个上游 producer。

## 5 个固定父维度（不要发明新维度）

`functional_completeness` · `interaction_quality` · `process_compliance` · `problem_resolution` · `tool_call_correctness`

K13 强约束：`dimension_scores.json` 的 key 集合必须**正好等于** `{ m.parent_dimension : m ∈ selected_metrics }`。多写一个就 taint。

## 路径与环境变量

| 层 | 默认路径（相对 skill 根） | 覆盖环境变量 |
|---|---|---|
| 角色目录数据（`<role_id>.role.json`） | `./role-catalog/` | `EVALUATION_ROLES_DIR` |
| 员工档案（`<employee_id>.json`） | `./employees/` | `EVALUATION_EMPLOYEES_DIR` |
| 指标数据 | `./metrics/` | `EVALUATION_METRICS_DIR` |
| 用例数据 | `./test-cases/` | `EVALUATION_TEST_CASES_DIR` |
| 单次评估产物 | `./runs/<eval_id>/` | `EVALUATION_RUN_DIR` |
| 合成用例（STEP 1.5 输出） | `./runs/<eval_id>/synthesized-cases/` | 由运行目录派生 |
| 运行时驱动器（STEP 3 协议适配器） | `./runtime-drivers/` | `EVALUATION_DRIVERS_DIR` |
| 选用的驱动器 id | （无默认；必须在 `evaluation_context.runtime_driver.driver_id` 中显式指定） | `EVALUATION_DRIVER_ID` |
| 用户模拟器（STEP 3 客户角色档案，宿主 agent 用自己的 LLM 扮，不是子进程） | `./simulators/` | `EVALUATION_SIMULATORS_DIR` |
| 选用的模拟器 id | （无默认；必须在 `evaluation_context.runtime_simulator.simulator_id` 中显式指定） | `EVALUATION_SIMULATOR_ID` |
| 单场景对话硬上限 | 每个 `*.tc.json` 的 `turn_budget.hard_max_turns`；缺失则回退到 `evaluation_context.global_turn_cap`（默认 30） | — |

## 想了解更多？

| 想看 | 去这里 |
|---|---|
| 完整流程、HARD RULES、K-rules、5 个父维度、路径表、路由表 | [`SKILL.md`](./SKILL.md) |
| 某一步具体怎么做 | [`./playbooks/`](./playbooks/README.md) |
| K-rules 一一对应、严重性、taint 行为 | [`./playbooks/k-rules.md`](./playbooks/k-rules.md) |
| 启动前不变量（pre-flight） | [`./playbooks/pre-flight-invariants.md`](./playbooks/pre-flight-invariants.md) |
| Tainted 怎么处理、怎么恢复 | [`./playbooks/tainted-run-lifecycle.md`](./playbooks/tainted-run-lifecycle.md) |
| 角色目录怎么写、继承 / 别名 / fail-soft | [`./role-catalog/README.md`](./role-catalog/README.md) |
| 员工档案怎么写、三源解析优先级 | [`./employees/README.md`](./employees/README.md) |
| 指标数据层细节（含 15 个内置指标 = 7 通用 + 8 角色专属 + 角色覆盖矩阵） | [`./metrics/README.md`](./metrics/README.md) |
| 用例数据层细节（v2.0 simulator-driven 字段、provenance、polarity） | [`./test-cases/README.md`](./test-cases/README.md) |
| Driver 怎么写、怎么选 | [`./runtime-drivers/README.md`](./runtime-drivers/README.md) |
| Simulator 怎么写、为什么不是子进程、`.no-decide-script` 哨兵 | [`./simulators/README.md`](./simulators/README.md) |
| 单次评估目录里的所有产物 + 三个 reference fixture（eval-soul-001 / eval-xiaofu-00{1,2}）分别演示了什么反模式 | [`./runs/README.md`](./runs/README.md) |
| 运行时数据形状（schemas）+ HTML 模板占位符契约 | [`./runtime-schemas/README.md`](./runtime-schemas/README.md) |
| 路由选择 / 上游 producer 依赖（含 role-ontology）/ 主题打分算法 | [`./contracts/projections/ontology_extraction/contract-index.json`](./contracts/projections/ontology_extraction/contract-index.json) |
| 13 步流水线 + K1–K18 权威文本 | [`./contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`](./contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json) |

## 不变约定

- **契约文件（`*.projection.json` / `*.schema.json` / `contract-index.json`）的字段名与文本仍以英文为准**——它们是 producer/consumer 双方共同消费的合约语言，翻译 key 会破坏校验。
- 本中文文档只做"找谁读什么"的索引，不重复 SKILL.md 里的细则；细则更新只改 SKILL.md 与 playbook，不需要双向同步。
