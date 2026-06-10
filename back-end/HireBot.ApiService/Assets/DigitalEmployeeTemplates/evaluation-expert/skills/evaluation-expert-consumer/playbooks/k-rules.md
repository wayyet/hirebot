# K 规则速览

工作流合同（`contracts/projections/ontology_extraction/metric-selection/metric-selection.workflow-contract.projection.json`）在 `constraint_mappings[]` 中定义了 K1–K21。本表为人类可读的索引。

> K 规则命名空间说明。工作流合同拥有 K1–K18（工作流前置条件）。两个提示约束投影各自拥有内部 K1–K5 命名空间（scoring-judgement：基线=50 / 红线 / 高分罕见 / 证据 / 报告完整性；metric-selection：声明维度 / 权重求和 / 仅注册表 / 角色+场景匹配）。role-catalog 投影拥有自己的 K1–K4（文件名 / 重复 / 继承 / 仅 STEP 0 规范化）。当 SKILL.md 或 playbook 说"K9 违规"时，除非明确加前缀（如"scoring-judgement K3"或"role-catalog K2"），否则始终指**工作流合同**命名空间。

## 工作流合同 K 规则（权威编号）

| # | 名称 | 所属步骤 | 严重级别 | 禁止/要求的内容 | 失败处理 |
|---|---|---|---|---|---|
| K1  | `MetricRegistryNonEmpty` | PRE | critical | PRE 文件系统扫描后 `metric_registry` 必须非空 | block_or_escalate |
| K2  | `EnrichTestCasesAlwaysRuns` | STEP 2 | high | STEP 2 无条件运行，即使对已声明 `applicable_metrics` 的完全精选用例也是如此 | block_or_escalate |
| K3  | `FanOutIsUniformAndPerMetric` | STEP 4 | critical | 每 `(test_case, metric)` 恰好一次 LLM 调用；禁止批处理 | block_or_escalate |
| K4  | `AggregationAndRedLineAreDeterministic` | STEP 5 / 6 / 7 | critical | 5/6/7 中禁止 LLM；STEP 7 红线为纯代码；STEP 9 的 LLM 不得覆写 `triggered` | 污染运行 |
| K5  | `SynthesizedCasesIsolatedFromCatalog` | STEP 1.5 | high | STEP 1.5 的输出写入 `./runs/<eval-id>/synthesized-cases/`；绝不写入 `./test-cases/` | block_or_escalate |
| K6  | `ReportLayerIsTwoTier` | STEP 8 / 9 | high | STEP 9 必须在所有适用场景都有 ScenarioReport 后才能运行；STEP 9 链接，不内联 | block_or_escalate |
| K7  | `ReportNumericFieldsAreCopiesNotRecomputations` | STEP 8 / 9 | critical | 报告中所有数值字段是上游 `MetricScore` / STEP 5 / 6 / 7 输出的字节拷贝 | 重新生成报告 |
| K8  | `NoAdhocOrchestratorScripts` | 所有步骤 | critical | 宿主 Agent 不得在技能创建时白名单（`./runtime-drivers/<id>/`、`./runtime-*/<id>/`）之外创建任何可执行文件（`.py` / `.sh` / `.ts` / `.js` / `.mjs` / `.ipynb` / Makefile / `.cmd` / `.ps1`）；编排作为对话中的 Agent 工具调用轮次运行 | 污染运行 + 写入 `TAINTED.md` |
| K9  | `SelectedMetricsRoleFilteredAtStep1` | STEP 1 + STEP 1.2 | critical | **（已重写）** STEP 1 生成 `candidate_metrics`（确定性，机器可验证的角色过滤）；STEP 1.2 生成 `selected_metrics = (candidate_metrics − removed) ∪ added`。禁止将完整注册表复制到 `candidate_metrics`；`candidate_metrics` 和 `dropped_metrics` 均须持久化；当 STEP 1.2 跳过/失败时，`selected_metrics == candidate_metrics` | 污染运行 |
| K10 | `InlineEnrichedCasesMatchPersistedFiles` | STEP 2 / 3 / 4 | critical | 对每个 tc：内联 `evaluation_context.enriched_test_cases[*].applicable_metrics` 必须与持久化的 `./runs/<eval-id>/enriched-cases/<tc_id>.json` 字节相同，且为 `selected_metrics` 的子集 | 污染运行 |
| K11 | `UserScenarioConsultationBeforeSynthesis` | STEP 1.5 | high | 当 `test_case_status=='missing'` 时，Agent 必须先询问用户，再进行 SOP 合成；咨询持久化到 `evaluation_context.user_consultation_log`；Tier-2 用例带 `reliability_caveat` | 在 EvaluationReport.open_questions 中标记 |
| K12 | `StepIntermediateArtifactsPersisted` | STEP 5 / 6 / 7 | critical | STEP 5 → `aggregated_metric_scores.json`；STEP 6 → `dimension_scores.json`；STEP 7 → `red_line_check.json`；三者均须在下一步前写入 | 污染运行；STEP 9 在 `open_questions` 中列出缺失产物 |
| K13 | `DimensionScoresKeysMatchSelectedMetrics` | STEP 6 | critical | `dimension_scores.json` 的键必须等于 `{ m.parent_dimension for m ∈ selected_metrics }`；禁止伪造维度 | 污染运行 |
| K14 | `DriverProtocolLoopComplete` | STEP 3 | critical | 严格交替 `send → read evaluatee_turn → send \| end`；在写入 `end` 之前绝不关闭 stdin；`decision.next_utterance` 非空时必须先 send 再 end；唯一合法的提前停止原因是 `should_continue==false`、`turn_index+1 >= effective_max_turns`、driver `error` 事件 | 拒绝轨迹；污染运行 |
| K15 | `StopConditionsAlignedWithExpectedToolCalls` | STEP 1.5 / 2（设计）+ STEP 3（运行时） | high | （设计）当 must-criticality 工具从未触发时，`stop_conditions.success` 不可满足；必要信息交接已覆盖；success 描述可操作的结案。（运行时）Simulator 在客户必要信息话语仍被锁在 `next_utterance` 中时不得声明 `goal_achieved`。 | 在 STEP 3 前修改用例；运行时触发通过 K14 第四条款拒绝轨迹 |
| K16 | `ScoringMustInvokeEvaluatorLLMPerCaseMetric` | STEP 4 | critical | 每 `(test_case, metric)` 一次真实 LLM 调用；`scored_at` 为每次调用的真实时间戳；跨文件重复 `scored_at` 字符串 = 批量伪造；推理必须引用轨迹的具体子字符串 | 污染运行；STEP 9 以 `critical` 列出每对重复时间戳 |
| K17 | `EmployeeResolutionProvenanceRequired` | STEP 0 | critical | `employee.employee_provenance` 必须存在，`source` ∈ {authoritative_file, user_dialog, inferred_fallback}，`reliability` ∈ {high, low}；`reliability=low` 要求非空 `caveat`；报告字节拷贝该字段；推断回退结论使用"indicative"而非"definitive"；只有 STEP 0 可写 `employee.role.role_id` | 污染运行（**原子**：任何污染操作失败都使整个运行失败） |
| K18 | `CurateDecisionsMustBeAudited` | STEP 1.2 | critical | 每个 `removed`/`added` 决策都有 `curate_log` 条目，附 ≥1 个证据引用，标明源字段 + 引用逐字子字符串；`len(curate_log)==len(removed)+len(added)`；`max_metrics` / `min_dimensions_covered` 边界强制执行 | 污染运行（**部分成功**：继续 + 记录失败操作；完全失败则停止） |
| K19 | `DriverSubprocessWiringContract` | STEP 3 | critical | 长生命周期 driver 子进程必须通过规范 pad `PAD=/tmp/eval-driver/<eval_id>/<tc_id>`（文件 `{in,out,cursor,err,pid}`——全部普通文件，无 FIFO）接线。`in` 为普通文件：Agent 通过 `>>` 追加动作 JSON，`tail -f` 将其管道进入 driver stdin。`out` 和 `cursor` 为普通文件，供计划的游标式 `read_one_event` 轮询器使用。禁止自创临时管道名（`/tmp/eval-stdin-pipe`、`/tmp/eval-stdout.txt`等），禁止用 `cat` 或 `tail` 替换计划的 stdout/cursor 读取器，禁止跳过必须的 spawn 前 + 场景后清理。废弃的 `<> "$PAD/in"`（O_RDWR FIFO）模式在容器内核上导致了提前 stdin EOF——`tail -f` 消除了这个竞争条件。参见 `playbooks/step-03-driver-and-simulator-loop.md` § "Driver 子进程接线合同"获取五个必须的 shell 命令和自检方法。 | 拒绝场景；在规范 pad 下重新运行；若反复出现则污染运行 |
| K20 | `RunPlanMaterialisedBeforeStep3` | STEP 2.5 / STEP 3 | critical | STEP 2.5（`planRun`）必须在 STEP 3 开始前写入 `runs/<eval_id>/run_plan.json`（根据 `runtime-schemas/run_plan.schema.json` 验证）。计划每场景包含五个**字面 shell 字符串**（`pre_spawn_cleanup`、`spawn`、`read_one_event`、`write_action_template`、`post_scenario_cleanup`）以及 `pad.*` 路径和 `effective_max_turns`。STEP 3 必须原文执行这些字符串；唯一允许的运行时替换是用当前 `send`/`end` 动作 JSON 替换 `commands.write_action_template` 中的标记 `<<JSON_PAYLOAD>>`。在运行时拼接 shell、修改 `commands.*` 的任何其他字符，或在没有 `run_plan.json` 的情况下开始 STEP 3，均为 K20 违规。参见 `playbooks/step-2.5-plan-run.md`。 | 阻断 STEP 3；重新运行 STEP 2.5；若 STEP 3 已在即兴命令下开始，拒绝场景并在计划下重新运行 |
| K21 | `NegativeCasesMustMeet20Percent` | STEP 1.5 | high | 合成测试用例必须包含**负极性**用例（练习受限 / 上升 / 失败路径的用例），目标比例 `正:负 ≈ 80:20`。具体而言，令 `N = #cases where polarity ∈ {positive, negative}`（边界极性用例不计入比例）：(a) 当 `N == 1` 时不强制比例；(b) 当 `2 ≤ N ≤ 4` 时，`#negative ≥ 1` 必须成立；(c) 当 `N ≥ 5` 时，`#negative ≥ ceil(0.20 * N)` 必须成立。每个负用例在对应正用例存在时必须设置 `paired_case_id`（反之亦然，对应正用例也明确指向负用例）；若不存在天然的正用例配对，必须设置 `polarity_rationale`。跳过 K21 的唯一方式是记录 `evaluation_context.negative_coverage_exemption = { reason, evidence }`（如"所有场景都是没有决策边界的纯信息查询"）——静默省略负用例是 K21 违规。适用于 Tier-1（用户提供）和 Tier-2（SOP 派生）合成。参见 `playbooks/step-1.5-consult-then-synthesize.md` § "负用例覆盖"。 | 拒绝 STEP 1.5 输出；重新合成含必要负用例的用例，或记录豁免；STEP 9 在 `open_questions` 中暴露违规 |

> **命名空间说明（延续）**：`playbooks/step-09-overall-report.md` 当前使用标签 `K17`（仅模板）和 `K18`（中文叙述）来表示两个 STEP-9 本地硬性规则，这些规则与上方工作流合同的 K17 / K18 **冲突**。这些 step-09 标签应在未来清理时重命名（如 `K-S9-TPL` / `K-S9-NAR`）；在此之前，`step-09-overall-report.md` 中对"K17"/"K18"的任何引用均指该文件中描述的 STEP 9 本地规则，而非本表中的工作流合同规则。

## 严重级别阶梯

| 严重级别 | 对运行的影响 |
|---|---|
| critical | 运行被污染；后续步骤停止或对污染范围进行防护；STEP 9 在 `open_questions` 中暴露；写入 `TAINTED.md` |
| high | 运行附带说明继续；STEP 9 在 `open_questions` 中暴露；语言降级（"indicative" / "preliminary"） |

## 如何找到权威文本

对于每条 K 规则，`metric-selection.workflow-contract.projection.json → constraint_mappings[i].notes` 是权威的。Playbooks 是意译；若 playbook 和合同有分歧，合同胜出，playbook 必须修补。
