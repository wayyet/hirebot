# TODO 生成与完成推进

本文档定义如何通过 KingCrab 原生 `todo` 工具生成、更新和完成 TODO，以及如何通过 TODO 完成状态驱动阶段推进。

> 名词速查: 不熟悉的术语见 [01-glossary.md](01-glossary.md)

---

## 1. TodoTool 接入规范

### API

KingCrab `TodoTool`（`OpenClaw.Gateway.Tools.TodoTool`）提供以下操作：

| action | 参数 | 行为 |
|---|---|---|
| `add` | `text`(必填), `notes`(可选) | 新建 TODO，返回 `id`（格式 `todo_{guid前16位}`）。`notes.status` 默认为 `open` |
| `update` | `id`(必填), `text`(可选), `notes`(可选) | 修改已有 TODO，未传字段保持不变 |
| `complete` | `id`(必填) | 将 TODO 标记为完成（`Completed=true`）。等价于 `update` + `Completed=true` |
| `remove` | `id`(必填) | 从列表中删除 TODO |
| `list` | 无 | 返回所有 TODO，按 `(Completed ASC, CreatedAtUtc ASC)` 排序 |
| `clear` | 无 | 清空全部 TODO |

### 调用时机

| 时机 | 操作 | 说明 |
|---|---|---|
| 识别到新的状态缺口 | `add` | 新 gap TODO |
| 用户补充了缺口信息 | `update` | 更新 `expected_state`、`acceptance_criteria` |
| 用户开始处理某 TODO | `update` | `status` → `in_progress` |
| 缺口已解决（文件产出 + 用户确认） | `update` + `complete` | `status` → `done`，填写 `acceptance_evidence` |
| 用户撤回/跳过 | `update` + 可选 `remove` | `status` → `dismissed` |
| 每轮对话开始 | `list` | 了解当前全部 TODO 状态 |
| 阶段推进前 | `list` | 确认当前阶段所有 `required` 项已 `done` |

### 输出格式

`list` 的返回格式：
```
todo_abc123def45678 [open] 资料：退货规则手册 → 抽取判定规则本体
todo_xyz789ghi01234 [done] 技能：退货资格初判 ← 需要明确技能定义
```

---

## 2. notes JSON 结构

每条 TODO 的 `notes` 字段是一个 JSON 字符串，包含以下字段：

```jsonc
{
  // ── 阶段标识 ──
  "stage": "material",            // material | skill | external | cross_stage

  // ── 分类 ──
  "kind": "gap",                  // gap（状态缺口）| diagnosis（诊断项，仅诊断 skill 使用）
  "gap_type": "ontology_slice",   // 缺口类型（见各阶段枚举）

  // ── 状态描述核心（必填） ──
  "current_state": "uploads/ 中有《退货规则手册》.pdf，ontology/ 目录为空",
  "expected_state": "ontology/ 中至少包含 1 个从该手册抽取的本体切片 JSON",
  "acceptance_criteria": "ontology/ 目录下存在 .json 文件，内容包含退货判定条件",

  // ── 完成证据（完成后填写） ──
  "acceptance_evidence": null,    // 证明完成的文件路径

  // ── 流程状态 ──
  "status": "open",               // open | in_progress | done | dismissed

  // ── 优先级 ──
  "priority": "required",         // required（阻塞阶段推进）| recommended | optional

  // ── 来源追溯 ──
  "source": "用户上传《退货规则手册》.pdf 并确认需要从这份资料抽本体",
  "fingerprint": "material:return-rules:ontology-slice-001",

  // ── 关联 ──
  "related_files": ["uploads/退货规则手册.pdf"],
  "related_todos": [],            // 关联的其他 TODO id（如 skill TODO 依赖某个 material TODO）

  // ── 时间戳 ──
  "created_at": "2026-05-07T10:30:00Z",
  "updated_at": "2026-05-07T10:30:00Z"
}
```

### 字段说明

| 字段 | 必须 | 说明 |
|---|---|---|
| `stage` | 是 | TODO 所属阶段。系统层和诊断 skill 按此字段筛选各阶段 TODO |
| `kind` | 是 | `gap` = 雇佣教练维护的缺口；`diagnosis` = 诊断 skill 维护的诊断项 |
| `gap_type` | 是 | 缺口分类。各阶段枚举见下方 |
| `current_state` | 是 | **当前**模板包中相关文件/目录的实际状态。用一句话描述 |
| `expected_state` | 是 | **预期**达到的状态。足够具体，可以据此判断是否完成 |
| `acceptance_criteria` | 是 | 完成判定标准。可验证的、无歧义的描述 |
| `acceptance_evidence` | 否 | 完成后填写：证明缺口已解决的证据（文件路径或关联 TODO id） |
| `status` | 是 | 流程状态：`open` → `in_progress` → `done` / `dismissed` |
| `priority` | 是 | `required` = 必须解决，阻塞阶段推进；`recommended` = 建议；`optional` = 可选 |
| `source` | 是 | 缺口来源（用户哪句话或哪个文件触发了这个 TODO） |
| `fingerprint` | 是 | 稳定标识。同一缺口在多轮中变化时用于**更新同一条 TODO 而非新建**。格式: `{stage}:{核心关键词}:{缺口类型}-{序号}` |
| `related_files` | 否 | 关联的模板包文件路径 |
| `related_todos` | 否 | 关联的其他 TODO id（如 skill TODO 依赖 material TODO 产出） |

### fingerprint 稳定性

同一缺口（如"退货资格初判"这条 skill）在多轮对话中被反复修改时，**继续更新同一个系统 `todo` 的 `id`**，不要新建 TODO。用 `fingerprint` 识别重复意图：

- 用户第一次说"要会处理退货" → `add`，fingerprint=`skill:return-qualification:definition-001`
- 用户第二次补充"不只是退货，还要能查退款进度" → `update` 同 id，修正 `expected_state`
- 用户第三次说"退货那个再细化一下触发条件" → 继续 `update` 同 id

---

## 3. TODO 文本命名规范

`todo.text` 是给用户和侧边栏看的短标题。格式：

```
{阶段前缀}{缺口类型}：{简短描述}

示例:
  "资料：退货规则手册 → 抽取判定规则本体"
  "技能：退货资格初判 ← 需要明确技能定义"
  "外部：CRM 订单读取 ← 需要配置连接"
  "配置：AGENTS.md ← 缺少 VIP 升级红线"
```

---

## 4. 各阶段 TODO 生成规则

### 资料阶段 (material)

| gap_type | 使用场景 | 示例 expected_state |
|---|---|---|
| `missing_upload` | 无上传资料 | `uploads/ 中至少有 1 份业务文件` |
| `unclassified_upload` | 有文件但未归类 | `《退货规则手册》.pdf 归类为"决策规则"` |
| `ontology_slice` | 需要从资料抽取本体 | `ontology/ 中有包含退货判定规则的切片 JSON` |
| `insufficient_coverage` | 缺少某类资料 | `至少还有 1 份"流程 SOP"类资料` |

### 技能阶段 (skill)

| gap_type | 使用场景 | 示例 expected_state |
|---|---|---|
| `missing_skill_definition` | 无技能定义文件 | `skills/return-qualification/SKILL.md 存在且字段完整` |
| `incomplete_skill_fields` | 技能字段不完整 | `skill_name 从"处理售后"细化为"退货资格初判"` |
| `skill_ontology_gap` | 技能依赖的本体缺失 | `该 skill 依赖的退货判定规则本体切片已产出` |
| `skill_boundary_conflict` | 与 AGENTS.md 冲突 | `移除"自动退款"逻辑（AGENTS.md 禁止不经审批的退款）` |

### 外部阶段 (external)

| gap_type | 使用场景 | 示例 expected_state |
|---|---|---|
| `missing_external_config` | 缺少系统配置 | `external/crm-read-order.yaml 存在且字段完整` |
| `external_skip_declaration` | 用户声明不需要 | `用户确认跳过外部阶段` |
| `unlinked_external` | 配置未关联 skill | `CRM 读取配置关联到"退货资格初判"skill` |
| `missing_credential_slot` | 缺凭据槽位 | `系统层已创建 CRM 凭据槽位，状态为 bound` |

---

## 5. 阶段推进

### 推进判定

```
1. todo.list → 获取当前全部 TODO
2. 过滤: stage=当前阶段 AND priority=required AND status!=dismissed
3. 全部 done → 诊断 skill 确认 complete → 推进
4. 否则 → 继续当前阶段
```

### TODO 完成确认

当模板包状态发生变化（文件产出、用户确认）时：
1. `todo.update` 将 `notes.status` 设为 `done`，填写 `acceptance_evidence`
2. `todo.complete` 将系统层 `Completed` 设为 `true`
3. 诊断 skill 下次触发时重新评估阶段就绪状态

### 配置文件变更影响

当 SOUL/IDENTITY/AGENTS 变更可能影响已完成的 TODO 时（见 [04-package-rules.md](04-package-rules.md)）：
1. 受影响的 `done` TODO → `todo.update` 将 `status` 切回 `open`（或新建关联 TODO）
2. 一行简短告知用户
3. 诊断 skill 标记 cross_stage 诊断项
