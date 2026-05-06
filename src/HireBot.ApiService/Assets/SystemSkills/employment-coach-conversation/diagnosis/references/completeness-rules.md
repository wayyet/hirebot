# Completeness Rules

本文件定义 `diagnosis` 对资料、技能、外部和跨阶段一致性的判断口径。模板完备性清单优先于本文件默认规则。

## 通用判断顺序

1. 先读取模板完备性清单，按 `required`、`recommended`、`optional` 分类建立缺口表。
2. 再读取系统 todo，按 `notes.stage` 和 `notes.status` 归类。
3. 只有 `confirmed` 的 todo 可直接满足完备性项。
4. `ready_to_dispatch`、`dispatched`、`dirty`、`needs_review` 只能作为存在证据，不能算完成。
5. 下游 artifacts 只能作为佐证；如果没有对应 confirmed todo，不单独算完成。
6. 用户明确撤销的 `dismissed` todo 不参与满足判断，但可以解释为什么某项仍缺。

## 阶段 1：material

必须检查：

- 是否至少有 1 条 `notes.stage: material` 的系统 todo 已 `confirmed`
- 是否覆盖完备性清单要求的资料类型，如业务对象定义、决策规则、流程 SOP、案例库、边界与约束、风格语料
- 是否有上传资料未被任何 material 系统 todo 归类
- 是否有 material todo 处于 `ready_to_dispatch`、`dispatched`、`dirty` 或 `needs_review`
- 最新 `ontology-extraction` callback 是否 `failed` 或 `partial`

默认阶段状态：

| 条件 | material.status |
|---|---|
| 无 material todo，且无可用上传资料 | `missing` |
| 有 material todo 但无 confirmed，或必需资料类型未满足 | `partial` |
| 必需资料类型均由 confirmed todo 覆盖 | `complete` |

常见诊断 todo：

- `d_material_required_source_001`: 还缺至少一份可用于抽取本体的业务资料
- `d_material_decision_rules_required_001`: 还缺决策规则类资料
- `d_material_unclassified_uploads_001`: 有上传资料尚未归类到系统 todo
- `d_material_callback_failed_001`: 本体提取回传失败或部分失败，需要重走

## 阶段 2：skill

必须检查：

- 是否至少有 1 条 `notes.stage: skill` 的系统 todo 已 `confirmed`
- confirmed skill 是否具备 `payload.skill_name`、`payload.skill_description`、`payload.trigger`、`payload.expected_output`
- 是否满足完备性清单要求的主线 skill 数量或类别
- 是否有用户提到的能力只停留在 drafting / ready_to_dispatch
- 已生成的 `skills/` 产物是否能通过 callback、artifact 或 todo id 对应回系统 todo

字段明确度判断：

- `skill_name` 不能是“处理售后”“回答问题”这类泛称
- `skill_description` 必须包含触发情境、核心逻辑、输入依赖或输出形式中的关键要素
- `trigger` 必须能被运行时识别为场景或关键词条件
- `expected_output` 必须说明输出形态和后续动作，而不只是“回复用户”

默认阶段状态：

| 条件 | skill.status |
|---|---|
| 无 skill todo | `missing` |
| 有 skill todo 但无 confirmed，或字段 / 数量 / 类别不足 | `partial` |
| 必需 skill 均由 confirmed todo 覆盖，且字段完整 | `complete` |

常见诊断 todo：

- `d_skill_main_required_001`: 主线 skill 数量不足
- `d_skill_trigger_missing_001`: skill 缺少可识别触发条件
- `d_skill_expected_output_missing_001`: skill 缺少明确输出形态
- `d_skill_artifact_uncorrelated_001`: 生成产物无法对应到系统 todo

## 阶段 3：external

必须检查：

- 是否存在 confirmed 的 `external` 系统 todo，或 confirmed 的 `payload.kind: skip`
- 每条 external todo 是否明确 `category`、`payload.objective`、`payload.target_system`
- `payload.linked_skills` 指向的 skill todo 是否存在且已 confirmed
- 是否满足完备性清单要求的 read / write / notify / search / transform 能力
- 是否有凭据值出现在对话、todo payload、callback 摘要或 artifacts 摘要中
- 最新 `external-config` callback 是否失败或部分失败

默认阶段状态：

| 条件 | external.status |
|---|---|
| 无 external todo，且用户未明确跳过 | `missing` |
| 有 external todo 但无 confirmed，或字段 / 链接 / 类别不足 | `partial` |
| 存在 confirmed 的 `kind: skip` | `skipped` |
| 必需外部能力均由 confirmed todo 覆盖 | `complete` |

凭据安全：

- 只允许记录 `auth_kind`，不允许记录具体 token、密钥、密码、API Key、连接串
- 发现疑似凭据时，诊断 todo 只写“发现疑似凭据值泄露”，不得复述原文

常见诊断 todo：

- `d_external_read_required_001`: 还缺必需外部读取能力
- `d_external_target_system_missing_001`: 外部能力缺少目标系统
- `d_external_linked_skill_unconfirmed_001`: 外部能力依赖的 skill 尚未确认
- `d_external_secret_exposure_001`: 发现疑似凭据值泄露

## 跨阶段一致性

必须检查：

- `agent.md` 中的判定规则、红线、阈值、必转条件是否和 confirmed skill 冲突
- `agent.md` 的数据访问范围是否和 external 系统 todo 冲突
- `soul.md` 的使命范围是否让已有 skill 明显越界或遗漏关键能力
- 是否存在 `needs_review` 的系统 todo
- 是否存在 `dirty` 或仍在 `dispatched` 的必需相关 todo

常见诊断 todo：

- `d_cross_agent_rule_conflict_001`: agent.md 规则与已确认 skill 存在冲突
- `d_cross_data_scope_conflict_001`: 数据访问范围与外部能力不一致
- `d_cross_needs_review_001`: 存在待复核系统 todo
- `d_cross_pending_dispatch_001`: 必需项仍在等待下游回传

## 出口判定

只有同时满足以下条件，才能输出 `ready_for_packaging: true`：

- 资料、技能、外部三阶段的 required 项全部满足
- 外部阶段如果跳过，必须是 confirmed 的 skip todo
- 没有 required 相关的 `dirty`、`dispatched`、`needs_review`
- 最新 required 相关 callback 没有 `failed`
- 没有高风险安全诊断项

推荐项缺失时，`status` 可为 `warning`，但 `ready_for_packaging` 应根据产品策略决定；如果上层要求推荐项不阻塞，则仍可为 `true`，并在 `user_summary` 中轻描淡写提示。
