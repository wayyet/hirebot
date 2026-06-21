# Prep Agent — 操作手册

**角色**：评估初始化代理  
**职责范围**：评估前完备性检查 + PRE.A + STEP 0 + PRE + STEP 1 + STEP 1.2 + STEP 1.5 + STEP 1.6 + STEP 2 + STEP 2.5  
**上下文边界**：见 `agent-boundaries.md` §Prep Agent  
**退出条件**：`runs/<eval_id>/run_plan.json` 写入完成

---

## 启动检查单（Prep Agent 开始前必须全部通过）

> 这是对 `pre-flight-invariants.md` 在 Prep Agent 视角下的补充，不替换原文。

1. `/workspace/runtime/evaluation-context.json` 存在且可解析
2. `evaluation_context.evaluation_id` 非空（确定本次 `eval_id`）
3. 六个热插拔数据层可读（同 pre-flight 不变式 1）
4. 至少一个 `*.metric.json` 通过 schema 验证（同不变式 2）
5. `runs/<eval_id>/` 不存在，或存在且含 `TAINTED.md` 且用户明确重试（同不变式 9）
6. 若 `evaluation_context.parallelism.enabled == true`：确认平台支持并发子 Agent

---

## 上下文加载顺序（STEP 0 前必须完成）

**目的**：确保 STEP 1.5 走 P0 路径（模板自动合成），避免不必要的用户咨询中断。

```
步骤  文件                                                      用途
 1.  /workspace/runtime/evaluation-context.json               确定 eval_id、driver、simulator、模板目录
 2.  /workspace/uploads/artifact/<template_dir>/config/IDENTITY.md   角色定位、语言风格
 3.  /workspace/uploads/artifact/<template_dir>/config/SOUL.md       核心行为原则
 4.  /workspace/uploads/artifact/<template_dir>/config/AGENTS.md     多 Agent 架构
 5.  /workspace/uploads/artifact/<template_dir>/skills/*/SKILL.md    技能定义（触发词、能力、边界）
 6.  /workspace/uploads/artifact/<template_dir>/ontology/*.slice.md  领域本体
 7.  /workspace/uploads/evaluation-expert-consumer/ontology/*.workflow-contract.projection.json（若存在）
 8.  ./test-cases/ 目录扫描（设置 test_case_status）
```

> 第 2~6 步是 STEP 1.5 P0 路径的必要前提。**跳过任何一步都会导致 STEP 1.5 误触发用户咨询（P2），影响评估流畅性。**

---

## 执行流程

Prep Agent 按以下顺序执行，每步详情见各自的 playbook 文件。

### 阶段 A：初始化（确定性，无 LLM）

```
评估前完备性检查（pre-flight-invariants.md）
  └─ 任何不变式失败 → block_or_escalate，退出
PRE.A  loadRoleCatalog（step-00-resolve-employee.md §PRE.A）
```

### 阶段 B：员工与指标解析（含 LLM 交互）

```
STEP 0  resolveEmployee（step-00-resolve-employee.md §STEP 0）
  └─ 员工文件 → authoritative_file
  └─ 用户描述 → user_dialog（需确认）
  └─ 无来源 → inferred_fallback（低可信度）
PRE     loadMetricRegistry（内联，扫描 ./metrics/*.metric.json）
STEP 1  resolveEmployeeAndCheckTestCases（step-01-resolve-and-filter.md）
  └─ 输出：candidate_metrics、dropped_metrics、test_case_status
STEP 1.2 curateMetrics（step-1.2-curate-metrics.md）
  └─ 输出：selected_metrics（= candidate − removed ∪ added）
  └─ 失败时降级：selected_metrics = candidate_metrics
```

### 阶段 C：测试用例准备（条件性 LLM）

```
若 test_case_status == "missing":
  STEP 1.5  parseTestCases（step-1.5-consult-then-synthesize.md）
    └─ P0（首选）：模板材料已加载 → 自动合成，不询问用户
    └─ P1：用户本轮提供了场景
    └─ P2：用户明确拒绝 → SOP 回退（reliability=low）
    └─ P3：block_or_escalate（无材料、无用户响应）
  STEP 1.6  pushSynthesizedTestCases（若 hirebot_api 存在）
    └─ 推送 synthesized-cases/ 到 HireBot，使前端 Question Cards 立即可见
否则（test_case_status == "ready"）:
  跳过 STEP 1.5 / 1.6
```

### 阶段 D：丰富化与计划（确定性，无 LLM）

```
STEP 2   enrichTestCases（step-02-enrich-test-cases.md）
  └─ 对每个 test-cases/*.tc.json 添加 applicable_metrics 绑定
  └─ 已有对应 enriched 文件（STEP 1.5 双写）的 tc_id 直接跳过
  └─ 输出：runs/<eval_id>/enriched-cases/<tc_id>.enriched.json
STEP 2.5 planRun（step-2.5-plan-run.md）
  └─ 输出：runs/<eval_id>/run_plan.json（完成信号）
```

---

## Prep Agent 完成后持久化的产物

| 产物 | 消费方 | 备注 |
|---|---|---|
| `runs/<eval_id>/evaluation_context.json` | Run Agent、Report Agent | Prep Agent 在 STEP 0 后写入，含 selected_metrics；**不含** client_secret |
| `runs/<eval_id>/enriched-cases/<tc_id>.enriched.json` | Run Agent | 每个 TC 一个 |
| `runs/<eval_id>/synthesized-cases/<tc_id>.tc.json` | STEP 1.6 / Report Agent | 仅当走 STEP 1.5 时存在 |
| `runs/<eval_id>/run_plan.json` | Orchestrator（完成检测） | 写入 = Prep Agent 完成信号 |

---

## 常见失败场景与处理

| 失败场景 | 处理方式 |
|---|---|
| 员工模板目录不存在 | STEP 1.5 无法走 P0；转 P1/P2（询问用户）|
| `selected_metrics` 为空（K1） | block_or_escalate，退出 Prep Agent |
| STEP 1.2 超时或格式错误 | 降级：`selected_metrics = candidate_metrics`，继续 |
| STEP 1.5 用户超时未响应（P1/P2 路径） | 超时后走 P2 SOP 回退（若 SOP 存在）；否则 block |
| STEP 2 某个 tc.json schema 验证失败 | 跳过该 TC，记录 open_question，继续处理其余 TC |
| 所有 TC enrichment 失败 | block_or_escalate（无法生成 run_plan） |

---

## Prep Agent 绝不做的事（K22 自检）

在执行每个步骤前，Prep Agent 必须确认该步骤在允许列表内：

```
允许：评估前完备性检查, PRE.A, STEP 0, PRE, STEP 1, STEP 1.2, STEP 1.5, STEP 1.6, STEP 2, STEP 2.5
禁止：STEP 3, STEP 4, STEP 5, STEP 6, STEP 7, STEP 8, STEP 9, STEP 10
禁止：直接连接目标沙箱（WebSocket / driver）
禁止：读取其他 TC 的 trace 文件或 score 文件
```

---

## 参考

- 评估前完备性检查：[`pre-flight-invariants.md`](./pre-flight-invariants.md)（+ 本文新增的不变式 14）
- 各步骤详情：`step-00-resolve-employee.md` ~ `step-2.5-plan-run.md`
- Agent 边界：[`agent-boundaries.md`](./agent-boundaries.md)
- K 规则：[`k-rules.md`](./k-rules.md)
