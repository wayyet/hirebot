# Diagnostic Output Schema

本文件定义 `diagnosis` skill 的输出结构。诊断报告是评估结果投影，不驱动下游 skill 直接执行；其中的诊断项必须来自 Handoff tools 中 `kind: diagnosis` 的条目。

## 顶层结构

```yaml
diagnostic_report:
  status: pass | warning | blocked
  confidence: high | medium | low
  current_stage: material | skill | external | ready_for_packaging
  ready_for_packaging: true | false
  stage_readiness:
    material:
      status: missing | partial | complete
      reason: <一句话证据>
    skill:
      status: missing | partial | complete
      reason: <一句话证据>
    external:
      status: missing | partial | complete | skipped
      reason: <一句话证据>
  diagnostic_todos: []
  todo_correlation: []
  open_questions: []
  user_summary: <可由雇佣教练转述的一两句话>
```

## status 判定

| status | 含义 |
| --- | --- |
| `pass` | 所有必需项满足，无阻塞项，可进入下一系统动作 |
| `warning` | 必需项基本满足但存在推荐项缺口、上下文不足、轻微一致性风险 |
| `blocked` | 至少一个必需项缺失、失败、待复核或处于未完成状态 |

`ready_for_packaging` 只能在 `status: pass` 且三阶段必需项均完成时为 `true`。

## diagnostic_todo 结构

诊断项与流程 Handoff todo 一样通过 Handoff tools 承载。`diagnostic_report.diagnostic_todos` 只是 `handoff.list(kind=diagnosis)` 的结构化投影，不是另一套存储。

```yaml
- session_id: session_20260508_001
  handoff_id: d_<stage>_<gap_key>_<seq>
  title: <还差什么的用户可读标题>
  kind: diagnosis
  stage: material | skill | external | cross_stage
  level: required | recommended | optional
  category: <缺口类型>
  question: <还差什么>
  evidence: <为什么判断为缺>
  suggested_action: <建议上层流程如何继续引导>
  related_todos: [<handoff id>]
  status: open | resolved | dismissed | superseded
```

字段要求：

- `session_id` 必须来自当前会话，由宿主注入并随诊断项投影返回。
- `handoff_id` 必须在同一 `session_id` 下稳定。相同缺口在多次诊断中复现时，继续使用同一 `d_...` id 或同一 `fingerprint` upsert。
- `kind` 必须为 `diagnosis`；不得用诊断项承载下游 dispatch 输入。
- `level` 必须来自完备性清单；清单缺失时用默认门槛并标明 `confidence: low` 或 `medium`。
- `question` 必须描述缺口，例如“还缺一份决策规则类资料”。
- `suggested_action` 只给上层流程参考，不得直接写成 `<dispatch>`。
- `related_todos` 只能关联已有 Handoff id，不代表修改它们。

## todo_correlation 结构

`todo_correlation` 表示诊断项与流程 Handoff todo 的关联，不表示存在新的 todo 类型，也不表示可以修改流程 Handoff todo。

```yaml
- diagnosis_handoff_id: d_skill_main_required_001
  related_todos: [s_refund_init_001]
  relationship: satisfies | partially_satisfies | conflicts | needs_review | evidence_only
  note: <简短说明>
```

常见关系：

- `satisfies`: 相关流程 Handoff todo 已 `status: confirmed`，满足某个诊断项
- `partially_satisfies`: 有相关 Handoff todo，但字段不完整或状态未确认
- `conflicts`: 配置规则与 Handoff todo 内容冲突
- `needs_review`: Handoff todo 已被上游治理标记为待复核
- `evidence_only`: 仅作为证据参考，不足以满足缺口

## 示例

```yaml
diagnostic_report:
  status: blocked
  confidence: high
  current_stage: skill
  ready_for_packaging: false
  stage_readiness:
    material:
      status: complete
      reason: 已有 2 条 material Handoff todo confirmed，覆盖决策规则和风格语料。
    skill:
      status: partial
      reason: 已有 1 条主线 skill confirmed，但模板要求至少 2 条。
    external:
      status: missing
      reason: 阶段尚未走到，未发现 external Handoff todo。
  diagnostic_todos:
    - handoff_id: d_skill_main_required_001
      stage: skill
      level: required
      category: 主线 skill
      question: 还缺至少 1 条主线 skill
      evidence: 完备性清单要求主线 skill >= 2，当前 confirmed 数量为 1。
      suggested_action: 回到阶段 2，引导用户补充第二个高频场景。
      related_todos: [s_seven_day_init_001]
      status: open
  todo_correlation:
    - diagnosis_handoff_id: d_skill_main_required_001
      related_todos: [s_seven_day_init_001]
      relationship: partially_satisfies
      note: 已满足其中一条主线 skill，但数量不足。
  open_questions: []
  user_summary: 资料已经够用了，技能还差一条主线能力；补齐后再配置外部系统会更稳。
```
