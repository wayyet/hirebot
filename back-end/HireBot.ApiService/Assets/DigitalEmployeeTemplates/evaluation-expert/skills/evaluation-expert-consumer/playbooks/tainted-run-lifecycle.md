# 污染运行生命周期

当硬性规则被违反或 K 规则的输入门验证器失败时，运行变为**污染**状态。污染运行不会被静默中止——它们在受限规则下继续运行，以保留审计轨迹。

## 运行何时变为污染？

| 触发条件 | K 规则 | 检测方 |
|---|---|---|
| Agent 在白名单之外的技能根目录下创建了任何可执行文件 | K8 | 飞前检查不变式 10；步骤内审计 |
| `selected_metrics` 是完整注册表（跳过角色过滤） | K9 | STEP 1 自检 |
| 内联 `enriched_test_cases[]` 与持久化 `enriched-cases/<tc_id>.json` 不一致 | K10 | STEP 2 / 3 / 4 输入门 |
| STEP 5 / 6 / 7 产物缺失或无效 | K12 | STEP 9 输入门 |
| `dimension_scores.json` 键集合 ≠ `{ parent_dimension for m ∈ selected_metrics }` | K13 | STEP 6 自检 |
| 轨迹未通过四条款拒绝规则 | K14 | STEP 4 输入门 |
| 多个 `<tc>__<metric>.json` 文件共享 `scored_at` | K16 | STEP 5 输入门 |
| `MetricScore.scoring_reasoning` 未引用任何轨迹证据 | K16 | STEP 5 输入门（每文件） |
| `employee.employee_provenance` 缺失/无效，或 `reliability=low` 没有 `caveat`，或后续步骤修改了 `employee.role.role_id` | K17 | STEP 0 自检；STEP 9 输入门 |
| 某个精选决策的 `evidence` 为空、`curate_log` 条目缺失，或引用未通过源字段和子字符串检查 | K18 | STEP 1.2 自检；STEP 2 输入门 |

## 污染时发生什么

1. **立即停止对污染输出的评分。** 不得将部分输出引用为有效结果。
2. **在 `./runs/<eval_id>/` 下写入 `TAINTED.md`**（若尚无运行目录则写在技能根目录），内容包括：
   - 违反了哪条 K 规则
   - 哪个文件或步骤触发了违规
   - 下一个安全操作是什么
3. **逐步决定哪些继续：**

   | 污染范围 | 继续的内容 | 停止的内容 |
   |---|---|---|
   | 一个轨迹（K14） | 其他场景继续；其评分有效 | 污染 `tc_id` 的 STEP 4 被跳过；STEP 9 在 `open_questions` 中列出该 tc |
   | 一个评分文件（K16 推理） | 其他 (用例, 指标) 对继续；该文件被重新生成 | 无——评分文件被重新生成，不被跳过 |
   | 所有评分文件（K16 重复时间戳） | 无——每个评分文件都可疑 | STEP 5 / 6 / 7 / 8 / 9 必须等待重新评分 |
   | STEP 1 指标过滤（K9） | 无 | 整个运行停止；从 STEP 1 重新开始，使用新的 `eval_id` |
   | STEP 6 维度伪造（K13） | 无 | 以确定性方式重新运行 STEP 6 |
   | Agent 编写了脚本（K8） | 无 | 删除脚本，从 STEP 0 重新开始，使用新的 `eval_id` |
   | 员工来源（K17） | **无——原子失败** | 整个运行失败：我们不再知道谁被评估了，所以报告毫无意义。从 STEP 0 重新开始，使用新的 `eval_id` |
   | 精选审计缺口（K18） | **其他评分继续**（部分成功）：分数有效；只是精选透明度存疑 | 暴露有问题的决策；若三个污染操作部分成功，继续并在 `open_questions` 中记录失败的操作；若全部失败则停止 |

## K17 恢复流程

- **触发条件**：STEP 0 没有产生 `employee_provenance`，或 `reliability=low` 但没有 `caveat`，或 STEP 0 之后的某个步骤更改了 `employee.role.role_id`。
- **纠正措施**：重新运行 STEP 0 以产生有效的来源块（从文件 / 用户对话 / 推断回退重新解析）；修复修改了 `role_id` 的步骤。
- **恢复**：K17 是原子失败——创建**新的 `eval_id`** 并从 PRE.A / STEP 0 重新运行。不要将来源补丁应用到半评分的运行中；身份不确定性使下游所有内容失效。
- **原子性**：三个污染操作（写入 `TAINTED.md`、停止评分、在 `open_questions` 中暴露）是一个原子结果。若任何一个失败，整个运行以非成功状态失败，且不产生成功的 EvaluationReport。若 `TAINTED.md` 写入本身失败，仍要停止评分并将违规写入运行日志。

## K18 恢复流程

- **触发条件**：`curate_log` 条目的 `evidence` 为空，`removed`/`added` 决策没有匹配条目，或引用未能正确引用命名源字段的真实子字符串。
- **纠正措施**：重新运行 STEP 1.2 以生成带有正确证据引用的精选决策；或者，若 STEP 1.2 本身不可靠，设置 `metric_selection_policy.mode = "never"` 使 `selected_metrics = candidate_metrics`（确定性基线），从 STEP 1.2 重新运行。
- **恢复**：K18 是部分成功容忍的——已计算的 `candidate_metrics` 和任何有效分数仍可使用。一旦精选决策修复，在同一 `eval_id` 中从 STEP 1.2 向后运行即可，然后继续 STEP 2。
- **部分失败规则**：若三个污染操作中至少一个成功，接受部分状态，继续评估，并在 `evaluation_context.open_questions` 中记录失败的操作。若全部失败，以非成功状态停止，且不产生成功的 EvaluationReport。

4. **STEP 9 暴露违规。** `EvaluationReport.open_questions` 必须以严重级别 `critical` 列出每个污染产物，且从污染范围得出的结论语言必须降级。

5. **HTML 报告显示红色横幅** 当任何 `open_questions` 条目严重级别为 `critical` 时，在雷达图上方显示红色横幅。

## 如何恢复

污染运行**不会**被自动删除。先审计它，然后选择恢复路径：

### A. 本地修复（一个轨迹 / 一个评分文件）

若只有单个产物被污染，可以：

- 重新生成该产物（例如重新评分单个 (用例, 指标) 对以修复 K16 推理）
- 在 `TAINTED.md` 中用 `superseded_by: <新文件>` 标记原始污染产物
- 若重新生成影响了聚合值，从 STEP 5 开始重新运行

### B. 部分重启（一个步骤的输出集）

若某个确定性步骤的输出（STEP 5 / 6 / 7）被污染但其输入是干净的：

- 删除污染产物
- 内联重新运行该步骤
- 重新运行所有下游步骤（STEP 9 接收新输入）

### C. 完全重启（K8 / K9 / 大规模伪造）

若违规表明 Agent 的流程本身崩溃（编写了脚本、复制了完整注册表、或批量伪造了所有分数）：

- 创建新的 `eval_id`
- 若其输入仍然有效，复制 `evaluation_context.json`
- 从 PRE / STEP 1 开始，使用全新的运行目录
- 保留污染目录用于审计；**不得**删除它

## `TAINTED.md` 应包含的内容

```markdown
# TAINTED — <eval_id>

**Detected at**: <ISO8601>
**Violated rule(s)**: K9 (SelectedMetricsRoleFilteredAtStep1), K12 (StepIntermediateArtifactsPersisted)
**Trigger**:
- selected_metrics in evaluation_context.json contains all metrics from the registry without role filtering (the eval-xiaofu-001 historical incident copied all 8 metrics that existed at the time; today the registry has 15),
  even though employee.role = "customer-service-ecommerce" should only match a strict subset.
- aggregated_metric_scores.json was never written before STEP 6 ran.

**Affected artifacts**:
- ./runs/<eval_id>/evaluation_context.json
- ./runs/<eval_id>/dimension_scores.json (downstream)
- ./runs/<eval_id>/reports/evaluation_report.json (downstream)

**Recovery path**: Full restart (Section C). Created new eval_id <eval_id_v2>.

**Audit notes**: This run is preserved as a reference fixture demonstrating
the K9 violation pattern.
```

## Anti-patterns during recovery

| Anti-pattern | Why it's wrong |
|---|---|
| Delete `TAINTED.md` to "clean up" the run | Loses the audit trail; STEP 9 can no longer surface the violation |
| Reuse the tainted `eval_id` directory for a new run | Mixes clean and tainted artifacts; the audit trail becomes ambiguous |
| Patch a tainted artifact in place without `TAINTED.md` superseded_by entry | Future readers can't tell which version is authoritative |
| Skip STEP 9 surface step ("the run is broken anyway") | Loses the K-rule self-reporting feedback loop; future operators don't learn |
