# Diagnostic Output Schema

本文件定义 `diagnosis` skill 的唯一输出结构。诊断报告是只读评估结果，不是 handoff todo，不驱动下游 skill 直接执行。

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
  handoff_correlation: []
  open_questions: []
  user_summary: <可由雇佣教练转述的一两句话>
```

## status 判定

| status | 含义 |
|---|---|
| `pass` | 所有必需项满足，无阻塞项，可进入下一系统动作 |
| `warning` | 必需项基本满足但存在推荐项缺口、上下文不足、轻微一致性风险 |
| `blocked` | 至少一个必需项缺失、失败、待复核或处于未完成状态 |

`ready_for_packaging` 只能在 `status: pass` 且三阶段必需项均完成时为 `true`。

## diagnostic_todo 结构

```yaml
- id: d_<stage>_<gap_key>_<seq>
  stage: material | skill | external | cross_stage
  level: 必需 | 推荐 | 可选
  category: <缺口类型>
  question: <还差什么>
  evidence: <为什么判断为缺>
  suggested_action: <建议上层流程如何继续引导>
  related_handoff_todos: [<handoff todo id>]
  status: open | resolved | dismissed | superseded
```

字段要求：

- `id` 必须稳定。相同缺口在多次诊断中复现时，保持同一 id。
- `level` 必须来自完备性清单；清单缺失时用默认门槛并标明 `confidence: low` 或 `medium`。
- `question` 必须描述缺口，例如“还缺一份决策规则类资料”。
- `suggested_action` 只给上层流程参考，不得直接写成 `<dispatch>`。
- `related_handoff_todos` 只能关联已有 handoff todo，不代表修改它们。

## handoff_correlation 结构

```yaml
- diagnosis_todo_id: d_skill_main_required_001
  related_handoff_todos: [s_refund_init_001]
  relationship: satisfies | partially_satisfies | conflicts | needs_review | evidence_only
  note: <简短说明>
```

常见关系：

- `satisfies`: handoff todo 已 confirmed，满足某个诊断项
- `partially_satisfies`: 有相关 todo，但字段不完整或状态未确认
- `conflicts`: 配置规则与 todo 内容冲突
- `needs_review`: todo 已被上游治理标记为待复核
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
      reason: 已有 2 条 material handoff todo confirmed，覆盖决策规则和风格语料。
    skill:
      status: partial
      reason: 已有 1 条主线 skill confirmed，但模板要求至少 2 条。
    external:
      status: missing
      reason: 阶段尚未走到，未发现 external handoff todo。
  diagnostic_todos:
    - id: d_skill_main_required_001
      stage: skill
      level: 必需
      category: 主线 skill
      question: 还缺至少 1 条主线 skill
      evidence: 完备性清单要求主线 skill >= 2，当前 confirmed 数量为 1。
      suggested_action: 回到阶段 2，引导用户补充第二个高频场景。
      related_handoff_todos: [s_seven_day_init_001]
      status: open
  handoff_correlation:
    - diagnosis_todo_id: d_skill_main_required_001
      related_handoff_todos: [s_seven_day_init_001]
      relationship: partially_satisfies
      note: 已满足其中一条主线 skill，但数量不足。
  open_questions: []
  user_summary: 资料已经够用了，技能还差一条主线能力；补齐后再配置外部系统会更稳。
```
