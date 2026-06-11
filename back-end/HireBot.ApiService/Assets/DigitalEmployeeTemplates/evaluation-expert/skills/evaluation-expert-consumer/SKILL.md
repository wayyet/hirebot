---
name: evaluation-expert-consumer
version: 1.1.0
description: 员工评估消费者技能。驱动一个确定性的 13 步工作流：解析被评估员工（文件 / 用户对话 / 推断）、规范化角色、按角色过滤并通过 LLM 精选指标集、解析/丰富测试用例、通过 driver+simulator 双角色驱动 STEP 3 场景、并行化 LLM 按指标评分，并生成每场景报告及最终合并评估报告（JSON + HTML）。触发词：「evaluate employee」「assess performance」「run evaluation」「评估员工」「绩效评估」「客服打分」。
keywords: [evaluation, employee, assessment, performance, scoring, 评估, 员工评估, 绩效评估, 打分, 客服评估, 评估专家, evaluation-expert]
metadata:
  openclaw:
    emoji: 📊
upstream_producer_dependencies:
  - producer_skill: ontology_extraction
    contract_index: contracts/projections/ontology_extraction/contract-index.json
    min_version: "1.0.0"
  - producer_skill: role-ontology
    contract_index: contracts/projections/role-ontology/contract-index.json
    min_version: "1.0.0"
  - producer_skill: metric-ontology
    contract_index: contracts/projections/metric-ontology/contract-index.json
    min_version: "1.0.0"
  - producer_skill: testcase-ontology
    contract_index: contracts/projections/testcase-ontology/contract-index.json
    min_version: "1.0.0"
---

# evaluation-expert-consumer

当用户要求「evaluate employee」「assess performance」「run evaluation」或以「evaluation expert」身份行动时触发。

本技能**与模板无关**：每个员工角色都通过**同一套确定性工作流**进行评估。角色差异体现在**六个热插拔数据层**（`./metrics/`、`./test-cases/`、`./runtime-drivers/`、`./simulators/`、`./role-catalog/`、`./employees/`），这些数据层由上游生产技能或目录约定驱动——**不需要**修改本技能文件。

## 会话启动——必读文件清单

**在执行任何步骤之前，Agent 必须按以下顺序读取材料，建立对本次评估对象的完整认知：**

### 1. 运行时上下文（首先读取）
- `/workspace/runtime/evaluation-context.json` — 唯一权威的评估上下文（含 `client_secret`、目标沙箱地址、路径配置）

### 2. 被评估员工模板材料（`/workspace/uploads/artifact/<template_dir>/`）

扫描 `/workspace/uploads/artifact/` 获取 `<template_dir>`（优先与 `evaluation_context.employee.source_template_id` 匹配的子目录，否则取最近修改的），然后按以下顺序读取：

| 顺序 | 文件 | 说明 |
|---|---|---|
| ① | `config/IDENTITY.md` | 角色定位、语言风格、禁忌词汇——STEP 0 身份识别与 STEP 1.5 场景合成的行为边界 |
| ② | `config/SOUL.md` | 核心行为原则、价值观——评判员工表现的基准思维框架 |
| ③ | `config/AGENTS.md` | 多 Agent 协作架构——了解员工由哪些子 Agent 组成及其职责分工 |
| ④ | `skills/*/SKILL.md` | 技能定义：触发词、能力清单、**边界与不做**、关键口径——测试用例**必须**覆盖技能范围内的场景 |
| ⑤ | `ontology/*.slice.md` | 领域本体切片：概念、关系、术语约束——STEP 1.5 合成时的数据字段和口径依据 |

### 3. 评估专用本体（`/workspace/uploads/evaluation-expert-consumer/ontology/`，若存在）

- `*.workflow-contract.projection.json` — 针对本员工技能的评估流程约束（与员工模板 ontology 是不同层次，均须读取）
- `*.slice.json` / `*.slice.md` — 评估侧本体切片（来自上游生产技能输出）

### 4. 预置测试用例（`/workspace/uploads/evaluation-expert-consumer/test-cases/`）

- 若目录非空（且不只有 `default_connectivity_testcases.json`）：直接进入 STEP 1
- 若目录为空或只有 default 文件：`test_case_status = "missing"`，将在 STEP 1.5 合成

> **提示**：跳过上述读取步骤是导致测试用例偏离员工职责、评估结论跑偏的主要原因。每轮会话开始时务必完整执行此读取流程。

## 高层流程

```
              ┌───────────────────────────────────────────────────────────────┐
              │  6 hot-pluggable data layers                                    │
              │  ./metrics/  ./test-cases/  ./runtime-drivers/  ./simulators/   │
              │  ./role-catalog/  ./employees/                                  │
              └───────────────────────────────┬───────────────────────────────┘
                                              │
                                              ▼
  PRE.A loadRoleCatalog ──► STEP 0 resolveEmployee ──► PRE loadMetricRegistry
        (deterministic)        (LLM + user confirm)        (deterministic)
                                              │
                                              ▼
  STEP 1 (candidate_metrics) ──► STEP 1.2 curateMetrics (selected_metrics) ──(test_case_status?)──► STEP 1.5 ──► STEP 1.6 ─┐
        (deterministic)              (LLM, bounded+auditable)              (LLM, synthesize)    (deterministic)    │  STEP 2
                                                                                                pushTestCases     └────►  ┌──────────────────────┐
                                                                                                   (optional)                │  per scenario:        │
                                                                                                                             │  STEP 3 ──► STEP 4   │ × N
                                                                                                                             └──────────┬───────────┘
                                                                                                                                        │
                                                  STEP 5 ──► STEP 6 ──► STEP 7 ──────────────────────────────────────────┘
                                                                          │
                                                  STEP 8 (per scenario) ──► STEP 9 (overall)
                                                                          │
                                                           JSON + HTML report
                                                                          │
                                                  STEP 10 uploadToHireBot (required when hirebot_api configured)
                                                  ├── sync-verdict ──► POST /api/v1/employees/{id}/evaluation/sync-verdict
                                                  └── sync-trace   ──► POST /api/v1/employees/{id}/evaluation/sync-trace
```

Legend: deterministic（确定性）= 白盒；**LLM** = STEP 1.5（条件触发）、STEP 4（并行扇出）、STEP 8（每场景综合）、STEP 9（整体综合）；**driver 子进程**仅在 STEP 3；**STEP 10** 和 **STEP 1.6** 是确定性子流程（当 `hirebot_api` 存在时 STEP 10 为必须步骤；仅在 `hirebot_api` 缺失时跳过）。

## 生产技能依赖

| 生产技能 | 发布内容 | 本技能读取位置 |
|---|---|---|
| `ontology_extraction` | 工作流合同、评分/判决提示约束、指标选择提示约束 | `contracts/projections/ontology_extraction/` |
| `role-ontology` | `role-catalog` 投影 + `role-catalog-entry.schema.json` | 合同读自 `contracts/projections/role-ontology/`；数据来自 `./role-catalog/*.role.json` |
| `metric-ontology` | `metric-catalog` 投影 + `metric.schema.json` | 合同读自 `contracts/projections/metric-ontology/`；数据来自 `./metrics/*.metric.json` |
| `testcase-ontology` | `test-case-catalog` 投影 + `test-case.schema.json` | 合同读自 `contracts/projections/testcase-ontology/`；数据来自 `./test-cases/*.tc.json` |

**热插拔规则：** 添加新指标或测试用例只需在 `./metrics/` 或 `./test-cases/` 下**放入文件**，不得修改任何 `*.projection.json`。

## 执行纪律（硬性规则）

宿主 Agent 通过**直接执行每个 STEP** 来运行本技能，而不是生成中间脚本。以下规则为阻断性规则：

1. **禁止临时编排脚本（白名单，非黑名单）— K8。** 本技能包内唯一允许的可执行文件，是技能创建时已提交的文件：
   - `./runtime-drivers/<driver_id>/run.py` 及同目录下的其他文件
   - 技能创建时随附的任何未来 `runtime-*/<id>/` 适配器目录

   Agent **不得**在技能根目录下的任何位置创建新的 `.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / `Makefile` / `*.cmd` / `*.ps1` 文件。尤其禁止创建 `runtime-drivers/ws_jwt/read_one_event.py`；`commands.read_one_event` 是 STEP 2.5 中的内联游标式 shell 字符串。完整反模式列表及恢复方法参见 [`playbooks/step-03-driver-and-simulator-loop.md`](./playbooks/step-03-driver-and-simulator-loop.md#hard-rule-no-orchestrator-scripts-k8)。

2. **调用 driver，不重新实现。** STEP 3 通过 shell 将所选 driver 作为子进程生成，并通过 stdin/stdout 行 JSON 通信。Agent **不得** `import` driver 模块到 Agent 编写的代码中，也**不得**在 driver 目录外复制 WebSocket / JWT / 轨迹写入逻辑。

3. **Simulator 是 Agent 自身的 LLM。** `./simulators/<simulator_id>/` 下的 simulator 角色配置，由宿主 Agent 的 LLM（与 STEP 1.5 / 4 / 8 / 9 相同的大脑）在进程内消费。Agent **不得**将 simulator 作为子进程启动，也**不得**为其配置独立的 LLM 密钥。

4. **每次运行目录仅存数据。** `./runs/<eval_id>/` 可包含 JSON 产物（合成用例、丰富测试用例、轨迹、评分、报告、日志、`TAINTED.md`）。**不得**包含可执行代码、Agent 草稿或任何 STEP 的重复实现。

5. **确定性步骤保持确定性。** PRE / STEP 1 / STEP 2 / STEP 5 / STEP 6 / STEP 7 是纯文件扫描或算术运算。Agent 以内联方式执行（读文件、计算、写 JSON）。**不得**在这些步骤中调用 LLM，也**不得**将其推迟到生成的脚本中。

6. **LLM 步骤在进程内执行。** STEP 1.5 / STEP 4 / STEP 8 / STEP 9 直接调用 Agent 自身的 LLM 大脑。Agent **不得**生成调用 HTTP LLM 端点的 Python 脚本来替代。

如果 Agent 有冲动编写任何 `.py` 文件，这说明提示词或合同不够清晰——应该暴露歧义，而不是伪造编排器。

## K 规则速览

工作流合同（`contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`）定义了 K1–K21。完整表格（含严重级别、所属步骤、污染策略及恢复方法）参见 [`playbooks/k-rules.md`](./playbooks/k-rules.md)。

| # | 名称 | 所属步骤 | 一句话说明 |
|---|---|---|---|
| K1  | `MetricRegistryNonEmpty` | PRE | 注册表为空 → block_or_escalate |
| K2  | `EnrichTestCasesAlwaysRuns` | STEP 2 | STEP 2 无条件运行 |
| K3  | `FanOutIsUniformAndPerMetric` | STEP 4 | 每 (用例, 指标) 一次 LLM 调用；禁止批处理 |
| K4  | `AggregationAndRedLineAreDeterministic` | STEP 5 / 6 / 7 | 禁止 LLM；STEP 7 为纯代码；STEP 9 不得翻转 `triggered` |
| K5  | `SynthesizedCasesIsolatedFromCatalog` | STEP 1.5 | 合成用例写入 `./runs/<eval-id>/synthesized-cases/`，不得写入 `./test-cases/` |
| K6  | `ReportLayerIsTwoTier` | STEP 8 / 9 | STEP 9 链接场景报告，不得内联 |
| K7  | `ReportNumericFieldsAreCopiesNotRecomputations` | STEP 8 / 9 | 报告中的数值字段是上游的字节拷贝 |
| K8  | `NoAdhocOrchestratorScripts` | 所有步骤 | Agent 不得在白名单之外创建任何可执行文件 |
| K9  | `SelectedMetricsRoleFilteredAtStep1` | STEP 1 + STEP 1.2 | STEP 1 生成 `candidate_metrics`（确定性角色过滤）；STEP 1.2 生成 `selected_metrics = (candidate − removed) ∪ added`；两个列表均须持久化；跳过/失败 ⇒ `selected_metrics = candidate_metrics` |
| K10 | `InlineEnrichedCasesMatchPersistedFiles` | STEP 2 / 3 / 4 | 内联 `applicable_metrics` ⊆ `selected_metrics`，且与持久化文件匹配 |
| K11 | `UserScenarioConsultationBeforeSynthesis` | STEP 1.5 | 先询问用户；SOP 合成仅在用户明确拒绝时使用；咨询记录需持久化 |
| K12 | `StepIntermediateArtifactsPersisted` | STEP 5 / 6 / 7 | 进入下一步前写入三个产物 |
| K13 | `DimensionScoresKeysMatchSelectedMetrics` | STEP 6 | 键集合**必须**等于 `{ m.parent_dimension : m ∈ selected_metrics }` |
| K14 | `DriverProtocolLoopComplete` | STEP 3 | 严格交替；在 `end` 之前不得关闭 stdin；最后一次话语发送后再 end |
| K15 | `StopConditionsAlignedWithExpectedToolCalls` | STEP 1.5 / 2 设计 + STEP 3 运行时 | （设计）`stop_conditions.success` 不可在 must-tools 未触发时满足；（运行时）simulator 在客户必要信息话语未送达时不得声明 `goal_achieved` |
| K16 | `ScoringMustInvokeEvaluatorLLMPerCaseMetric` | STEP 4 | 每 (用例, 指标) 真实 LLM 调用；`scored_at` 各不相同；推理引用轨迹 |
| K17 | `EmployeeResolutionProvenanceRequired` | STEP 0 | `employee.employee_provenance` 存在且合法；低可信度需附说明；只有 STEP 0 写 `role_id`；**原子失败**污染 |
| K18 | `CurateDecisionsMustBeAudited` | STEP 1.2 | 每个移除/添加决策均须附逐字引用证据；边界强制执行；**部分成功**污染 |
| K19 | `DriverSubprocessWiringContract` | STEP 3 | 规范 pad `/tmp/eval-driver/<eval_id>/<tc_id>/{in,out,cursor,err,pid}`——全部普通文件（无 FIFO）；`in` 为普通文件，通过 `tail -f` 管道送入 driver stdin；`out` 为普通 stdout 文件，通过游标式 `read_one_event` 读取；必须执行 spawn 前清理和场景后清理；禁止自创管道名 |
| K20 | `RunPlanMaterialisedBeforeStep3` | STEP 2.5 / STEP 3 | STEP 2.5 在 `runs/<eval_id>/run_plan.json` 中写入每场景五个**字面 shell 字符串**；STEP 3 **原文**执行；只有 `<<JSON_PAYLOAD>>` 可在运行时替换；禁止运行时字符串拼接 |
| K21 | `NegativeCasesMustMeet20Percent` | STEP 1.5 | 合成用例**必须**包含负极性用例，目标比例 `正:负 ≈ 80:20`；`N ∈ [2,4] ⇒ #负 ≥ 1`；`N ≥ 5 ⇒ #负 ≥ ceil(0.20*N)`；每个 `negative` 存在对应正例时**必须**设置 `paired_case_id`，否则设置 `polarity_rationale`；静默省略被拒绝；只有 `negative_coverage_exemption` 允许跳过 |

> 命名空间说明。每个提示约束投影有其内部 K1–K5 命名空间。除非明确加前缀（如「scoring-judgement K3」），否则「K9」/「K12」等始终指上方**工作流合同**命名空间。另外：`playbooks/step-09-overall-report.md` 使用内部标签 K17 / K18，与工作流合同的 K17 / K18 **冲突**——这些 step-09 标签将在未来清理时重命名为 `K-S9-TPL` / `K-S9-NAR`；在此之前，`step-09-overall-report.md` 中的「K17」/「K18」指该文件内描述的 STEP 9 本地规则，而非此处的工作流合同规则。

## 5 个固定父维度

这些名称**已冻结**，确保红线下限在子指标演化时保持稳定。新子指标通过 `metric.parent_dimension` 汇入此处。

| 维度 | 默认权重 | 默认红线下限 |
|---|---|---|
| `functional_completeness` | 0.25 | ≤ 40 |
| `interaction_quality`     | 0.20 | ≤ 30 |
| `process_compliance`      | 0.20 | ≤ 30 |
| `problem_resolution`      | 0.15 | （每模板自定义） |
| `tool_call_correctness`   | 0.20 | = 0（must 工具缺失） |

这些下限由 STEP 7 `redLineCheck` 在 STEP 6 汇总后评估。新指标可在 `*.metric.json` 中声明自己的 `red_line` 块；STEP 7 将其与上方下限合并。

**默认通过标准**（customer-service-ecommerce）：

- 总加权分 ≥ 70
- 所有 5 个父维度 ≥ 60
- 未触发任何红线

## 11 个步骤

权威执行图存在于 `contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`。每步的操作手册在 `./playbooks/` 下。

| # | 步骤 | 类型 | 操作手册 |
|---|---|---|---|
| PRE.A | `loadRoleCatalog` | 确定性 | 内联（扫描 `./role-catalog/*.role.json` 文件系统；失败时软降级，参见 role-catalog K1–K3） |
| 0    | `resolveEmployee` | LLM + 强制确认，条件触发 | [`step-00-resolve-employee.md`](./playbooks/step-00-resolve-employee.md) |
| PRE  | `loadMetricRegistry` | 确定性 | 内联（扫描 `./metrics/*.metric.json` 文件系统；注册表为空时快速失败） |
| 1    | `resolveEmployeeAndCheckTestCases` | 确定性 | [`step-01-resolve-and-filter.md`](./playbooks/step-01-resolve-and-filter.md) — 按角色过滤到 `candidate_metrics` |
| 1.2  | `curateMetrics` | LLM，有界 + 可审计，条件触发 | [`step-1.2-curate-metrics.md`](./playbooks/step-1.2-curate-metrics.md) — `selected_metrics = (candidate − removed) ∪ added` |
| 1.5  | `parseTestCases` | LLM，条件触发（仅当 `test_case_status == "missing"`） | [`step-1.5-consult-then-synthesize.md`](./playbooks/step-1.5-consult-then-synthesize.md) |
| 1.6  | `pushSynthesizedTestCases` | 确定性子流程（可选，若 `hirebot_api` 缺失或无合成用例则跳过） | 内联 — 运行 `testcase_uploader.py --synthesized-dir .../synthesized-cases/`；将用例推送到 HireBot 以便前端右侧面板卡片立即显示 |
| 2    | `enrichTestCases` | 确定性，始终运行 | 内联（附加每个 K10 的 `applicable_metrics ⊆ selected_metrics`；`*` 是通配符，非字面值） |
| 2.5  | `planRun` | 确定性，无 LLM | [`step-2.5-plan-run.md`](./playbooks/step-2.5-plan-run.md) — 将 `runs/<eval_id>/run_plan.json` 落盘（根据 `runtime-schemas/run_plan.schema.json` 验证）：每场景包含整个 driver 生命周期的字面 shell 字符串。负责 **K20**。STEP 3 必须在该文件存在后方可开始。 |
| 3    | `driveEmployeeOnScenario` | 双角色（driver 子进程 + 宿主 LLM simulator） | [`step-03-driver-and-simulator-loop.md`](./playbooks/step-03-driver-and-simulator-loop.md) — 轻量执行器：读取 `run_plan.scenarios[i].commands.*` 并**原文**执行（K19 + K20）；只有 `<<JSON_PAYLOAD>>` 可在运行时替换 |
| 4    | `scoreScenario` | LLM 并行扇出 | [`step-04-fanout-scoring.md`](./playbooks/step-04-fanout-scoring.md) |
| LOOP | （每场景 STEP 3、STEP 4） | — | 重复直至所有丰富用例完成 |
| 5    | `aggregateAcrossScenarios` | 确定性 | [`step-05-07-deterministic-rollup.md`](./playbooks/step-05-07-deterministic-rollup.md) |
| 6    | `rollUpToDimensions` | 确定性 | 同上 |
| 7    | `redLineCheck` | 确定性，禁止 LLM | 同上 |
| 8    | `buildScenarioReports` | LLM 综合（仅散文，每场景） | 内联（数值字段从 MetricScore 字节拷贝；LLM 仅撰写散文） |
| 9    | `buildOverallReport` | LLM 综合（仅散文，执行一次） | [`step-09-overall-report.md`](./playbooks/step-09-overall-report.md) |
| 10   | `uploadToHireBot`   | 确定性子流程（`hirebot_api` 存在时必须；缺失时跳过） | [`step-10-upload-to-hirebot.md`](./playbooks/step-10-upload-to-hirebot.md) |

在以上任何步骤运行之前，请验证[飞前检查不变式](./playbooks/pre-flight-invariants.md)。当 HARD RULE 或 K 规则失败时，遵循[污染运行生命周期](./playbooks/tainted-run-lifecycle.md)。

### STEP 1.6 内联流程

After STEP 1.5 has written every `*.tc.json` to `runs/<eval_id>/synthesized-cases/`:

```bash
python3 runtime-drivers/ws_jwt/testcase_uploader.py \
  --evaluation-context runs/<eval_id>/evaluation_context.json \
  --synthesized-dir    runs/<eval_id>/synthesized-cases/ \
  --output             runs/<eval_id>/upload_testcase_result.json
```

- **若 `hirebot_api` 缺失**：跳过（合成用例将在 STEP 10 作为备选内嵌到 trace bundle 中）。
- **若目录为空或不存在**：上传脚本打印跳过信息并以退出码 0 退出——不视为失败。
- **成功后**：前端右侧面板卡片在下次刷新时通过 `EnsureQuestionCardsFromRuntimeTextAsync` → `CollectRuntimeTestcases` 检测 bundle 中的 `test_cases` 数组后显示。

## 技能专属约束

- **支持的交付物**：evaluation_report、scoring_criteria、workflow_contract、metric_set
- **支持的投影类型**：workflow-contract、prompt-constraint、domain-model、metric-catalog、test-case-catalog
- **超出共享最小集的支持投影字段**：`concept_mappings.target_path`、`concept_mappings.target_kind`、`constraint_mappings.severity_mapping`、`constraint_mappings.applies_to_layer`、`delivery_artifacts.path`、`metric_catalog.scoring_dimensions`、`evaluation_criteria.red_lines`、`workflow_step.kind`、`workflow_step.fallback_chain`、`workflow_step.always_runs`、`workflow_step.uniform_fanout`、`workflow_step.llm_disallowed`
- **热插拔数据**：
  - `./role-catalog/*.role.json`（每角色一个文件；文件名必须等于 `role_id`）
  - `./employees/<employee_id>.json`（每员工一个文件；文件名必须等于 `employee_id`）
  - `./metrics/*.metric.json`（每指标一个文件；文件名必须等于 `metric_code`）
  - `./test-cases/*.tc.json`（每用例一个文件；文件名必须等于 `test_case_id`）
  - `./runtime-drivers/<driver_id>/`（driver manifest + 可执行入口 + 辅助文件）
  - `./simulators/<simulator_id>/`（simulator manifest + system_prompt.md；无可执行文件）
- **本地排除项**：不得发明不受支持的评估标准；不得绕过已映射的约束；不得修改 `./runs/<eval_id>/` 之外的文件；不得将运行时证据写回任何 `*.projection.json`

## 投影合同

本技能通过 `contracts/projections/**/contract-index.json` 发现绑定的投影合同进行增强。

- 发现、路由选择和提示补丁均由运行时处理，而非本文件中的手动规则。
- 人工评审：先读 `contract-index.json`，再读所选主题的 `README.md` 和 `REVIEW.md`，最后读所选的 `*.projection.json`。
- 所选投影对术语、说明、删减范围和阻断条件具有权威性。
- 若 `mapping_policy` 要求 `block_or_escalate`，或 `open_questions` 非空，在暴露问题之前不得最终确认输出。
- 不得重新创建 `dropped_items` 中列出的条目。

## 路径默认值与覆盖

| 层 | 默认路径（相对于技能根目录） | 覆盖环境变量 |
|---|---|---|
| 角色目录数据（`<role_id>.role.json`） | `./role-catalog/` | `EVALUATION_ROLES_DIR` |
| 员工文件（`<employee_id>.json`） | `./employees/` | `EVALUATION_EMPLOYEES_DIR` |
| 指标数据 | `./metrics/` | `EVALUATION_METRICS_DIR` |
| 测试用例数据 | `./test-cases/` | `EVALUATION_TEST_CASES_DIR` |
| 每次运行产物 | `./runs/<eval_id>/` | `EVALUATION_RUN_DIR` |
| 合成测试用例（STEP 1.5 输出） | `./runs/<eval_id>/synthesized-cases/` | 从运行目录派生 |
| 运行时 driver（STEP 3 协议适配器） | `./runtime-drivers/` | `EVALUATION_DRIVERS_DIR` |
| 所选 driver id | （无——`evaluation_context.runtime_driver` 的必填字段） | `EVALUATION_DRIVER_ID` |
| 用户 simulator（STEP 3 客户角色配置，由宿主 Agent 自身 LLM 消费——**不是**子进程） | `./simulators/` | `EVALUATION_SIMULATORS_DIR` |
| 所选 simulator id | （无——`evaluation_context.runtime_simulator` 的必填字段） | `EVALUATION_SIMULATOR_ID` |
| 每场景最大轮次（硬上限） | 每 `*.tc.json` 的 `turn_budget.hard_max_turns`；回退到 `evaluation_context.global_turn_cap`（默认 30） | — |

## 内置路由选择

`ontology_extraction` 合同索引的路由表（信号触发如下所示的主题 / target_view）：

| 员工模板 | 主要主题 | 默认视图 | 触发信号 |
|---|---|---|---|
| customer-service-ecommerce | customer-service-ecommerce | workflow-contract | 「客服」、「售后」、「退货」、「投诉」、「电商」、「工单」 |
| 任意                        | metric-selection | workflow-contract | 「测试用例」、「用例匹配」、「指标库」、「评估流程」、「fan-out」、「评估编排」 |
| 任意                        | metric-selection | prompt-constraint | 「指标」、「评分维度」、「评估标准」、「维度权重」 |
| 任意                        | scoring-judgement | prompt-constraint | 「打分」、「评分」、「严格评估」、「红线」、「起评分」 |

在 `metric-selection/workflow-contract` 中，指标注册表包含 **15 个指标**：7 个跨角色通用指标（每个角色都获得全部 7 个）加上 8 个角色专属指标。STEP 1 角色过滤后每个角色的指标数量：

| 角色 | 角色专属 / 通配符匹配 | 通用 | 角色总计（STEP 1 candidate_metrics） |
|---|---|---|---|
| `customer-service-ecommerce` | `tool_call_correctness`、`interaction_empathy`、`order_refund_policy_accuracy` | 7 | 10 |
| `after-sales-agent` | `tool_call_correctness`、`interaction_empathy` | 7 | 9 |
| `hr-attendance` | `tool_call_correctness`*、`attendance_rule_compliance`、`confidentiality_boundary_compliance` | 7 | 10 |
| `bid-writer` | `tool_call_correctness`*、`bid_clause_completeness`、`confidentiality_boundary_compliance` | 7 | 10 |
| `legal-expert` | `tool_call_correctness`*、`legal_citation_accuracy`、`confidentiality_boundary_compliance` | 7 | 10 |
| `software-engineer` | `tool_call_correctness`*、`code_change_risk_disclosure`、`confidentiality_boundary_compliance` | 7 | 10 |

7 个通用指标：`problem_resolution_completeness`、`response_clarity_and_structure`、`response_conciseness`、`factual_accuracy`、`proactive_clarification`、`safety_and_ethics_boundary`、`professional_tone_consistency`。`*` 表示通过 `applicable_roles: ["*"]` 通配符匹配。完整的每指标详情参见 [`metrics/README.md`](./metrics/README.md#当前内置指标15-个--7-通用--8-角色专属)。

## 参考资料

### 权威合同

- [`metric-selection.workflow-contract.projection.json`](./contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json) — 确定性流程（含 STEP 2.5 `planRun`）+ K1–K21
- [`metric-selection.prompt-constraint.projection.json`](./contracts/projections/ontology_extraction/metric-selection/metric-selection.prompt-constraint.projection.json) — 指标选择护栏（内部 K1–K4 命名空间）
- [`scoring-judgement.prompt-constraint.projection.json`](./contracts/projections/ontology_extraction/scoring-judgement/scoring-judgement.prompt-constraint.projection.json) — 分层评分策略（K1–K5，含 `applies_to_layer`）
- [`metric-library.metric-catalog.projection.json`](./contracts/projections/metric-ontology/metric-library/metric-library.metric-catalog.projection.json) — 指标注册合同
- [`testcase-library.test-case-catalog.projection.json`](./contracts/projections/testcase-ontology/testcase-library/testcase-library.test-case-catalog.projection.json) — 测试用例注册合同
- [`ontology_extraction/contract-index.json`](./contracts/projections/ontology_extraction/contract-index.json) — 路由选择索引（声明 `upstream_producer_dependencies`）

### 数据层编写指南

- [`role-catalog/README.md`](./role-catalog/README.md)、[`employees/README.md`](./employees/README.md)、[`metrics/README.md`](./metrics/README.md)、[`test-cases/README.md`](./test-cases/README.md)、[`runtime-drivers/README.md`](./runtime-drivers/README.md)、[`simulators/README.md`](./simulators/README.md)、[`runs/README.md`](./runs/README.md)、[`runtime-schemas/README.md`](./runtime-schemas/README.md)

### 运行时驱动脚本（ws_jwt driver）

STEP 3 的执行依赖 `./runtime-drivers/ws_jwt/` 目录下以下脚本（**只读引用，不得修改**）：

| 脚本 | 调用步骤 | 职责摘要 |
|---|---|---|
| [`run.py`](./runtime-drivers/ws_jwt/run.py) | STEP 3 `spawn` | 长连接 stdin/stdout 编排器：持有 WebSocket+JWT 连接、发送话语、收集 `assistant_done` 轮次、写 `ExecutionTrace`。不评分，不判断红线。 |
| [`ws_client.py`](./runtime-drivers/ws_jwt/ws_client.py) | 被 `run.py` 内部调用 | 底层 WebSocket 连接：连接 Gateway WS、发送消息、按轮次收集服务器推送。无业务语义解析。 |
| [`auth_client.py`](./runtime-drivers/ws_jwt/auth_client.py) | 被所有上传脚本调用 | 鉴权：解析 `evaluation_context.hirebot_api.auth`（`client_credentials`），向 Keycloak 换取 access_token，供出站 HTTP/WS 调用统一使用。 |
| [`testcase_uploader.py`](./runtime-drivers/ws_jwt/testcase_uploader.py) | STEP 1.6 | 把 `synthesized-cases/` 下的合成用例推送至 HireBot (`sync-trace`)，使前端右侧面板卡片立即可见。 |
| [`trace_uploader.py`](./runtime-drivers/ws_jwt/trace_uploader.py) | STEP 10（第一步） | 合并 `traces/*.trace.json` 为一个 bundle 并上传至 HireBot (`sync-trace`)。 |
| [`verdict_uploader.py`](./runtime-drivers/ws_jwt/verdict_uploader.py) | STEP 10（第二步） | 读取 STEP 9 的 `overall_report.json`，构造 `EvaluationVerdictSyncRequestDto` 并上传 (`sync-verdict`)。 |
| [`driver.json`](./runtime-drivers/ws_jwt/driver.json) | STEP 2.5 验证 | Driver 清单：声明 `driver_id` 及 `config_schema`，STEP 2.5 依此验证 `evaluation_context.runtime_driver.driver_config`。 |
| [`requirements.txt`](./runtime-drivers/ws_jwt/requirements.txt) | STEP 3 前置检查 | Python 依赖：`websockets>=12.0`。STEP 3 前确认已安装。 |

### 客户模拟器（simulators/customer_realistic/）

STEP 3 的双角色中，**宿主 Agent 自身 LLM** 扮演客户，依据以下文件生成 `SimulatorDecision`（**非子进程，无入口脚本**）：

| 文件 | 作用 |
|---|---|
| [`simulators/customer_realistic/system_prompt.md`](./simulators/customer_realistic/system_prompt.md) | **客户大脑核心提示词模板**。包含 8 条硬性行为规则、情绪状态机、`goal_achieved` / `bottom_line_violated` / `deadlock_detected` 判断逻辑，以及严格的 `SimulatorDecision` JSON 输出格式。STEP 3 每轮展开 Mustache 占位符（`{{customer_persona.*}}`、`{{goal.*}}`、`{{stop_conditions.*}}`、`{{current_emotion}}`、`{{dialog_so_far}}`、`{{effective_max_turns}}`）后调用宿主 LLM 生成决策。 |
| [`simulators/customer_realistic/simulator.json`](./simulators/customer_realistic/simulator.json) | 清单：`kind: "llm_persona"`，声明 `system_prompt` 文件名及 `consumes` / `produces` schema 路径 |
| [`simulators/customer_realistic/.no-decide-script`](./simulators/customer_realistic/.no-decide-script) | K8 审计哨兵：确认此目录无可执行入口脚本，**禁止删除** |
| [`simulators/README.md`](./simulators/README.md) | 热插拔规则、合同说明、添加新人格的检查清单 |

> **核心规则**：`system_prompt.md` 明确区分了"解释流程"与"解决问题"——Agent 给出步骤列表不等于完成任务，模拟客户应在此时设 `perceived_progress="partial"`, `should_continue=true`，继续追问直到 Agent 执行了实际操作或给出个性化具体答复。这条规则直接影响 STEP 4 评分的有效轮次覆盖率，评估质量的关键所在。

被评估员工的完整模板资料位于 `/workspace/uploads/artifact/<template_dir>/`，是 STEP 0 身份识别和 STEP 1.5 测试用例合成的**核心输入**。合成场景必须忠实于员工的技能边界和领域约束，不得凭空捏造。

| 文档 | 路径（相对于 `<template_dir>`） | 关键内容 | 必读步骤 |
|---|---|---|---|
| Agent 架构说明 | `config/AGENTS.md` | 多 Agent 职责分工与协作模式 | STEP 0、STEP 1.5 |
| 身份声明 | `config/IDENTITY.md` | 角色定位、语言风格、禁忌词汇、价值观 | STEP 0、STEP 1.5 |
| 灵魂文件 | `config/SOUL.md` | 核心行为原则与思维框架 | STEP 0、STEP 1.5 |
| 技能定义 | `skills/<skill_name>/SKILL.md` | 触发词、能力清单、边界与不做、关键口径 | STEP 1、STEP 1.5 |
| 本体切片 | `ontology/*.slice.md` | 领域概念、关系、术语约束（人类可读） | STEP 1.5 |
| 本体切片（JSON） | `ontology/*.slice.json` | 同上，机器可读结构 | STEP 1.5 |
| 本体投影（evaluation 专用） | `ontology/projections/*.projection.json`（若存在） | 针对评估场景的 workflow-contract | STEP 1、STEP 1.5 |

> **提示**：`/workspace/uploads/evaluation-expert-consumer/ontology/` 下还可能存在为本次评估专门生成的 `*.workflow-contract.projection.json`——这是对员工技能 ontology 的评估专项约束投影，与员工模板目录里的通用 ontology 是不同层次的文档，两者都要读。

### 操作手册

- [`playbooks/`](./playbooks/) — 每步操作流程、K 规则表、飞前检查不变式、污染运行生命周期

### 共享模板

- [`templates/CONSUMER_SKILL_PROJECTION_SECTION.md`](./contracts/projections/ontology_extraction/templates/CONSUMER_SKILL_PROJECTION_SECTION.md)、[`templates/NEW_CONSUMER_SKILL_CHECKLIST.md`](./contracts/projections/ontology_extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md)、[`references/PROJECTION_CONSUMPTION_GUIDE.md`](./contracts/projections/ontology_extraction/references/PROJECTION_CONSUMPTION_GUIDE.md)、[`references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`](./contracts/projections/ontology_extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md)
