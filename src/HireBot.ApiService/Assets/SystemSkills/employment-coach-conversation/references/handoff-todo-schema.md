# Handoff Todo 完整 schema

本 skill 维护的所有 handoff todo 共用一套结构，下游 skill 把它当输入合约消化。

## 目录

- [通用结构](#通用结构)
- [状态机](#状态机)
- [ID 稳定性原则](#id-稳定性原则)
- [与诊断 skill 的 todo 区分](#与诊断-skill-的-todo-区分)
- [阶段 1：material](#阶段-1material)
- [阶段 2：skill](#阶段-2skill)
- [阶段 3：external](#阶段-3external)

## 通用结构

```
{
  id,                  // 稳定 ID：基于 stage + 内容指纹生成，重复发现同一意图时不会变
  stage,               // material | skill | external
  target_skill,        // ontology_extraction | skill_generation | external_config
  intent,              // 一句话目标，给用户读
  category,            // 阶段相关分类（见各阶段定义）
  payload,             // 阶段相关结构化字段（见各阶段 payload 字段）
  source,              // 来自对话的哪一段（消息片段或上传文件名）
  acceptance,          // 完成判定信号，供下游 skill 自检
  status,              // 见状态机
  created_at,
  updated_at
}
```

## 状态机

| 状态 | 含义 |
|---|---|
| `drafting` | 还在引导中，明确度不够，不能 dispatch |
| `ready_to_dispatch` | 明确度达标，可在合适时机发 dispatch |
| `dispatched` | 已发 dispatch，等下游回传 |
| `dirty` | 在 `dispatched` 期间被用户改动，回传到达后需重发 |
| `confirmed` | 下游回传 + 用户确认通过 |
| `needs_review` | 上游配置文件改动后需要复核（见 [config-file-governance.md](./config-file-governance.md)） |
| `dismissed` | 用户主动撤销，保留 id 不再 dispatch |

合法转移：

- `drafting` → `ready_to_dispatch` / `dismissed`
- `ready_to_dispatch` → `drafting`（用户继续改）/ `dispatched` / `dismissed`
- `dispatched` → `dirty` / `confirmed`
- `dirty` → `ready_to_dispatch`（重发后再走 `dispatched` / `confirmed`）
- `confirmed` → `needs_review` / `dismissed`
- `needs_review` → `confirmed`（无需改）/ `ready_to_dispatch`（要改重发）

## ID 稳定性原则

- 同一意图（如"退货资格初判"这条 skill）在多轮对话中被反复修改，id 不变，payload 字段被更新
- 用户撤回某条意图（"算了不要这条"）→ status 改为 `dismissed`，id 保留，不再 dispatch

## 与诊断 skill 的 todo 区分

- 诊断 todo 回答"还差什么"
- 本 skill 的 handoff todo 回答"差的部分要交给谁、要带什么去"
- 两类 todo 由系统层决定如何在 UI 上合并展示，本 skill 不关心

---

## 阶段 1：material

`target_skill = ontology_extraction`

**最低门槛**：至少 1 份资料被指认归类，且对应的 handoff todo 明确写出"要从中抽什么分类的本体 + 目标"。

**核心字段**：
- `category`: 资料类型（业务对象定义 / 决策规则 / 流程 SOP / 案例库 / 边界与约束 / 风格语料 / 其他）
- `objective`: 一句话目标，例如"抽出退货场景里所有可能的退款节点和对应的判定规则"
- `source_files`: 已上传的文件名列表
- `scene_hint`: 场景类型（客服 / 销售 / 内勤 / ...），来自场景判定（见 [scene-types.md](./scene-types.md)）
- `mode`: incremental（默认）/ full_replace（用户明确说"全量替换"时）

**dispatch 时机**：用户表示"先这些"或"暂时就这么多" + 至少 1 条 todo 达到明确度。

---

## 阶段 2：skill

`target_skill = skill_generation`

**最低门槛**：至少 1 条 skill 同时具备**明确的 name + 明确的 description**，并且每条 skill 能说清触发条件和期望输出。

**核心字段**：
- `skill_name`
- `skill_description`
- `trigger`
- `expected_output`
- `source`: 来自对话的哪一段

**字段明确度对照**：

| 字段 | 不够明确的样子 | 够明确的样子 |
|---|---|---|
| `skill_name` | "处理售后" | "退货资格初判" |
| `skill_description` | "用户问退货怎么办时回应一下" | "在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户" |
| `trigger` | "用户问起来" | "用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单" |
| `expected_output` | "回复用户" | "一条回复消息（含结论 + 依据），以及一条工单流转建议（如需要人工介入）" |

**支持的输入路径**：
- 主路径：用户用对话描述能力 → 你引导 → handoff todo 形成
- 二级路径：用户上传现成的 skill 文件 → 直接形成 todo（标记 `from_upload: true`），不必再追问明确度

**不要做的事**：
- 不要把行为约束（如"不能承诺金额"）放进 skill description——那归 agent.md（走配置文件治理路径）
- 不要为每个细小动作都创建一条 skill；3-7 条覆盖一个数字员工的主线工作通常合适
- 不要替用户决定 skill 之间怎么协作——技能生成 skill 自己会处理

**dispatch 时机**：至少 1 条 skill todo 达到明确度，且用户表示"先这些" → 发 dispatch；后续用户继续补充新 skill 时再追加 dispatch。

---

## 阶段 3：external

`target_skill = external_config`

**最低门槛**：每条外部能力都明确 `category` + `objective` + `target_system`；或者用户明确表达"不需要外部系统"（标记 skipped）。

**核心字段**：

| 字段 | 说明 |
|---|---|
| `category` | read / write / notify / search / transform 中之一 |
| `objective` | 一句话目标，例如"在用户咨询时，从 CRM 拉到该用户的最近 3 个订单" |
| `target_system` | 目标系统名（CRM / ERP / 企微 / 钉钉 / 自有 OA 等，含厂商或自研标识） |
| `linked_skills` | 这个能力被哪条 skill 用到（指 skill todo id 列表） |
| `auth_kind` | 凭据形式（OAuth / Bearer Token / 长期 Key 等），**不含凭据值** |
| `kind` | normal / skip |

**凭据规则（强约束，与 SKILL.md 顶层一致）**：
- token / 密钥 / 密码 / API Key 等**绝不在会话里收集**
- 用户在会话里如果输入了凭据，立刻提示"这类信息请填到右侧表单，不要在对话里发"
- handoff todo 里只描述"需要凭据 X 的形式"，不带值

**用户跳过分支**：
- 用户明确说"不需要外部系统"或等价表述 → 形成一条 `kind: skip` 的 todo
- 这个 todo 同样作为信号传给系统，等价于阶段已走过

**dispatch 时机**：每条新的外部能力 todo 达到明确度后即可发 dispatch；表单里的凭据字段由用户在右侧自己填，不影响本 skill 发信号的时机。
