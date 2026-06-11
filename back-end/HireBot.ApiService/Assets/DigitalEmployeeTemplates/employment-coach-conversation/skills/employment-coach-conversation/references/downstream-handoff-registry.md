# 下游 skill 渐进式披露交接注册表

本文件是 `employment-coach-conversation` 唤起下游 skill 的唯一交接清单。主 skill 不得假设下游 skill 的完整规则已经在上下文里；每次进入下游步骤时，都必须用本表里的内部触发块显式唤起对应 skill，并等待指定 terminal artifact。

## 总规则

1. **先路由，再执行**：主 skill 只做阶段判断、用户确认、artifact 推送和 payload 组装；不得代替下游 skill 写入 `ontology/`、`skills/`、`testcases/`、`reports/`，也不得直接运行下游 skill 的脚本。
2. **内部触发块必须点名 skill**：触发文本必须包含 `use skill <skill-name>`，并带 `artifact_payload` JSON。不要只写“开始生成”“继续处理”这类自然语言。
3. **等待 terminal artifact**：交接后，主 skill 必须等 `expected_terminal_artifact` 到达后再推进下一阶段；没有 terminal artifact 时不得声称完成。
4. **可选步骤也要走门**：测试用例和完整性审查可跳过，但必须先出现对应确认门；跳过只跳过该可选下游执行，不跳过后续审查门或打包门。
5. **用户可见回复保持业务语言**：内部触发块不得原样展示给用户。对用户只说“正在整理业务信息 / 正在准备技能所需业务资料 / 正在生成技能实现 / 正在审查内容完整性”。

---

# 阶段推进披露 (Stage Transition Disclosure)

阶段推进确认本身也是一次"技能披露"——当用户确认从阶段 N 推进到阶段 N+1，LLM 必须明确知道下一阶段要读取哪些 skill 和参考文件。以下 S1-S3 条目定义每次阶段推进时的**必须加载文件清单**、阶段摘要和入场动作。

> 阶段推进披露与 R1-R6 下游 skill 交接是两层路由：S 条目负责"进入下一阶段的上下文加载"，R 条目负责"在阶段内触发下游 skill 执行"。两者互补，不可互相替代。

## S1：阶段 1 → 阶段 2 推进披露

**前置信号**
- 资料阶段完成条件前三条已满足（至少 1 份资料已归类、source_path 已补全、用户已表达"先这些"）
- 用户对"确认推进到技能定义阶段吗？"给出肯定回应

**披露的技能与参考文件（LLM 必须在确认推进后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/SKILL.md` § 阶段 2（第 422-504 行） | 技能阶段主流程：目的、最低门槛、阶段完成条件、技能实现确认门、反停滞红线 | 必须 |
| `skills/employment-coach-conversation/references/flow-constraints.md` § 阶段 2 引导细则 | 引导话术、story-driven 推进、字段明确度对照、决策启发式 | 必须 |
| `skills/employment-coach-conversation/references/stage-data-schema.md` | `skill_workorder_progress` / `skill_workorder_summary` 的 data payload 结构 | 必须 |
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R2, R3 | projection pass 与 skill-generation 触发规则 | 按需（对应确认门通过后） |
| `skills/employment-coach-conversation/references/emit-artifact-protocol.md` | artifact 推送字段协议 | 参考 |

**阶段摘要**
- **目的**：把岗位动作和能力清单整理成结构化 skill 定义清单，每条都有明确的名称、触发条件和期望输出
- **子步骤顺序**：技能定义收集 → skill_definition_ready 确认门 → skill_workorder_summary → ontology_projection_ready 确认门 → projection pass (R2) → skill_generation_ready 确认门 → skill-generation (R3) → skill_generation_done
- **入场动作**：等待 `ontology_extraction_done` 到达 → 发出 `skill_workorder_progress` → 开始引导技能定义
- **最低门槛**：每个 skill 具备明确的 `name`（skill slug）+ `display_name` + `description` + `trigger` + `expected_output` + `generation_action`
- **禁止**：`ontology_extraction_done` 到达前不得发 `skill_workorder_progress` 或进入技能定义收集；skill-generation 完成前不得提示"可进入外部阶段"

**关联下游 R 条目**：R2（projection pass）、R3（skill-generation）

---

## S2：阶段 2 → 阶段 3 推进披露

**前置信号**
- `skill_generation_done` 已到达
- 用户对"继续进入外部配置"或等价表述给出肯定回应

**披露的技能与参考文件（LLM 必须在确认推进后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/SKILL.md` § 阶段 3（第 506-538 行） | 外部阶段主流程：目的、最低门槛、凭据红线、阶段完成条件、阶段门动作 | 必须 |
| `skills/employment-coach-conversation/references/flow-constraints.md` § 阶段 3 引导细则 | 引导话术、紧扣已有 skills 的套路、跳过分支 | 必须 |
| `skills/employment-coach-conversation/references/stage-data-schema.md` | `external_workorder_progress` / `external_workorder_summary` 的 data payload 结构 | 必须 |
| `skills/external-config/SKILL.md` | 外部配置写入规则、安全与校验 | 按需（系统层保存时触发） |
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R4 | 测试用例生成触发规则 | 按需（外部阶段完成后） |

**阶段摘要**
- **目的**：把支撑技能所需的外部能力和系统资源整理成有分类、有目标的外部能力清单
- **入场动作**：发出 `external_workorder_progress` → 紧扣已确认 skills 逐条引导外部能力定义
- **最低门槛**：每个外部能力明确 `分类（read/write/notify/search/transform）+ 目标 + 目标系统 + 鉴权方式 + 关联 skill`；或用户明确表达"不需要外部系统"
- **凭据红线**：token/密钥/密码/API Key 绝不在会话里收集，指引用户填写右侧表单
- **禁止**：skill-generation 未完成时不得进入外部阶段

**关联下游 R 条目**：R4（测试用例，外部阶段完成后可选触发）

---

## S3：阶段 3 → 阶段 4 推进披露

**前置信号**
- 外部配置已保存（`external_config_committed`）或明确跳过
- 用户对"生成数字员工"或等价打包意图给出肯定回应

**披露的技能与参考文件（LLM 必须在确认推进后读取）**

| 文件 | 用途 | 读取优先级 |
|------|------|-----------|
| `skills/employment-coach-conversation/references/downstream-handoff-registry.md` R4, R5, R6 | 测试用例（可选）→ 完整性审查（可选）→ 打包执行 | 必须 |
| `skills/packaging-test-cases/SKILL.md` | 评估测试用例生成规则 | 按需（用户选择生成测试用例时） |
| `skills/digital-employee-package-completeness-review/SKILL.md` | 完整性审查规则 | 按需（用户选择审查时） |

**阶段摘要**
- **目的**：经过测试用例确认门 → 完整性审查门 → 执行打包，生成最终数字员工包
- **子步骤顺序**：`packaging_testcases_ready` 确认门 → (可选) R4 测试用例 → manifest 同步 → `review_readiness` 确认门 → (可选) R5 完整性审查 → R6 打包
- **入场动作**：发出 `packaging_testcases_ready` → 询问是否生成评估测试用例
- **禁止**：未取得真实 `fileUrl` 前不得声称数字员工包已生成；不得跳过审查门直接打包

**关联下游 R 条目**：R4（测试用例）、R5（完整性审查）、R6（打包执行）

---

## R1：资料收口后触发本体抽取

**前置信号**
- 刚发出 `material_handoff_summary`，且 data 中有真实 `workspace_root`、`template_slug`、`items[]`。

**必须唤起**
- `ontology-extraction`

**内部触发块**

````text
[Internal downstream trigger: use skill ontology-extraction]
source_skill: employment-coach-conversation
trigger_reason: material_handoff_summary_completed
artifact_payload:
```json
<material_handoff_summary.data>
```
required_artifacts:
- ontology_extraction_progress
- ontology_extraction_done
return_to: employment-coach-conversation
````

**等待结果**
- `ontology_extraction_done`

**禁止**
- 不得在 `ontology_extraction_done` 前发 `skill_workorder_progress`。
- 不得由主 skill 自己输出或写入本体切片文件。

## R2：业务资料准备确认后触发投影 (Projection Pass)

**前置信号**
- 已发出 `skill_workorder_summary`。
- 已发出 `ontology_projection_ready`。
- 用户对“是否开始为技能准备业务资料”给出肯定。

**必须唤起**
- `ontology-extraction` 的 projection pass 模式

**内部触发块**

````text
[Internal downstream trigger: use skill ontology-extraction]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_ontology_projection
artifact_payload:
```json
{
  "trigger_mode": "projection_pass",
  "workspace_root": "<skill_workorder_summary.data.workspace_root>",
  "template_slug": "<skill_workorder_summary.data.template_slug>",
  "skills": <skill_workorder_summary.data.items>
}
```
required_artifacts:
- ontology_projection_progress
- ontology_projection_done
return_to: employment-coach-conversation
````

> Projection Pass 模式下，`ontology-extraction` 自行扫描 `<workspace_root>/ontology/` 下的 `*.slice.json`，无需上游传入 `material_summary` 或 `ontology_result`。

**等待结果**
- `ontology_projection_done`

**禁止**
- 不得在 `ontology_projection_done` 前调用 `skill-generation`。
- 不得把 `projection_binding_confirmed` 写进 `ontology_projection_ready` 或 `skill_generation_ready`；该字段只属于 R3 的内部触发 payload。

## R3：技能生成确认后触发技能实现生成

**前置信号**
- 已收到 `ontology_projection_done`。
- `ontology_projection_done.data.projected_count > 0`。
- `ontology_projection_done.data.projection_paths[]` 非空，且路径能对应 `skill_workorder_summary.data.items[].name`。
- 已发出 `skill_generation_ready`。
- 用户对“是否开始生成技能实现”给出肯定。

**必须唤起**
- `skill-generation`

**内部触发块**

````text
[Internal downstream trigger: use skill skill-generation]
source_skill: employment-coach-conversation
trigger_reason: projection_done_generate_skills
artifact_payload:
```json
{
  "workspace_root": "<skill_workorder_summary.data.workspace_root>",
  "template_slug": "<skill_workorder_summary.data.template_slug>",
  "items": <skill_workorder_summary.data.items>,
  "confirmed_skill_slugs": ["<items[].name>"],
  "projection_binding_confirmed": true,
  "projection_contract_mode": "required",
  "projection_result": <ontology_projection_done.data>,
  "projection_skill_slugs": ["<parsed from projection_paths>"]
}
```
required_artifacts:
- skill_generation_progress
- skill_generation_done
return_to: employment-coach-conversation
````

**等待结果**
- `skill_generation_done`

**失败分支**
- 如果 projection 结果不可消费，停留在阶段 2，提示用户补业务资料或调整技能定义；不得降级直连 `skill-generation`。

## R4：外部配置完成后触发可选测试用例生成

**前置信号**
- 已收到 `external_config_committed`，或系统层确认外部配置已跳过。
- 已发出 `packaging_testcases_ready`。
- 用户明确选择生成评估测试用例。

**必须唤起**
- `packaging-test-cases`

**内部触发块**

````text
[Internal downstream trigger: use skill packaging-test-cases]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_packaging_testcases
artifact_payload:
```json
{
  "trigger_after": "external_config_committed",
  "workspace_root": "<workspace_root>",
  "template_slug": "<template_slug>",
  "latest_material_summary": <latest material_handoff_summary.data>,
  "latest_skill_summary": <latest skill_workorder_summary.data>,
  "latest_external_summary": <latest external_workorder_summary.data>,
  "external_config": <latest external_config_committed.data>
}
```
required_artifacts:
- packaging_testcases_progress
- packaging_testcases_done
return_to: employment-coach-conversation
````

**跳过分支**
- 用户选择跳过时，不调用 `packaging-test-cases`，直接进入 R5 审查门准备；不得把测试用例缺失列为打包等待项。

## R5：打包前触发可选完整性审查

**前置信号**
- 已完成打包预检和 manifest 同步。
- 已发出 `review_readiness`。
- 用户明确选择审查。

**必须唤起**
- `digital-employee-package-completeness-review`

**内部触发块**

````text
[Internal downstream trigger: use skill digital-employee-package-completeness-review]
source_skill: employment-coach-conversation
trigger_reason: user_confirmed_package_review
artifact_payload:
```json
{
  "package_root": "<workspace_root>",
  "workspace_root": "<workspace_root>",
  "template_slug": "<template_slug>",
  "report_path": "reports/package-completeness-review.md"
}
```
required_artifacts:
- review_progress
- review_report
return_to: employment-coach-conversation
````

**等待结果**
- `review_report`

**禁止**
- 主 skill 不得直接运行 `validate_digital_employee_package.py`。
- 主 skill 不得根据 validator 输出直接修改 manifest 或技能文件；若用户选择修复，回到对应阶段或确定性同步步骤后再重新审查。

## R6：审查完成或跳过后执行打包

**前置信号**
- 用户已跳过审查，或已收到 `review_report` 且用户选择继续。

**执行**
- 发 `packaging_progress`，`data.status = "packing"`。
- 调用可用的打包/导出工具；没有专用工具时，在真实 `workspace_root` 内按白名单生成 zip。
- 验证 zip 根层级后，发 `template_package`，`kind = "file"`，`fileUrl` 使用真实返回值。

**禁止**
- 未取得真实 `fileUrl` 前，不得声称数字员工包已生成。
