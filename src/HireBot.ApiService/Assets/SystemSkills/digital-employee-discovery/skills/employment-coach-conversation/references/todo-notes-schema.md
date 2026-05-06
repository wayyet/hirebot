# todo notes 结构化合约

本 skill 不引入新的 todo 对象，也不要求运行时提供第二套 todo。所有待下游处理的事项都必须写入系统 `todo` 工具；系统 `todo` 负责 session 级持久化、展示和可见完成态。本文件只定义写入 `todo.notes` 的结构化输入合约，下游 skill 按这个合约消化。

## 目录

- [系统 todo 工具映射](#系统-todo-工具映射)
- [notes 通用结构](#notes-通用结构)
- [状态机](#状态机)
- [ID 稳定性原则](#id-稳定性原则)
- [与诊断 skill 的 todo 区分](#与诊断-skill-的-todo-区分)
- [阶段 1：material](#阶段-1material)
- [阶段 2：skill](#阶段-2skill)
- [阶段 3：external](#阶段-3external)

## 系统 todo 工具映射

每个待下游处理的事项对应一条系统 `todo`：

- `id`：由 `todo.add` 返回，作为 dispatch 块中的 todo id；不要自己伪造
- `text`：给用户和侧边栏看的短标题，例如`资料：退货规则手册抽取退款节点`、`技能：退货资格初判`
- `notes`：一段 JSON 字符串，内容见 [notes 通用结构](#notes-通用结构)
- `Completed`：仅在 `notes.status = confirmed` 且用户确认后，通过 `todo.complete` 置为 done

工具调用规则：

- 新建：`todo.add`，写入 `text` 和完整 `notes`
- 修改字段 / payload / 流程状态：`todo.update`，保持同一个系统 todo `id`
- 确认完成：先 `todo.update` 把 `notes.status` 写成 `confirmed`，再 `todo.complete`
- 用户撤销：`todo.update` 把 `notes.status` 写成 `dismissed`；如果不需要继续展示，再 `todo.remove`
- 查询当前清单：`todo.list` 可核对系统 todo id、标题和 open / done 状态；结构化状态以该 todo 的 `notes.status` 为准，更新时继续使用同一个 id

系统 `todo` 只有 `open / done` 两个可见状态，因此 `drafting / ready_to_dispatch / dispatched / dirty / confirmed / needs_review / dismissed` 必须放在 `notes.status`。

## notes 通用结构

```json
{
  stage,               // material | skill | external
  target_skill,        // ontology-extraction | skill-generation | external-config
  intent,              // 一句话目标，给用户读
  category,            // 阶段相关分类（见各阶段定义）
  payload,             // 阶段相关结构化字段（见各阶段 payload 字段）
  source,              // 来自对话的哪一段（消息片段或上传文件名）
  acceptance,          // 完成判定信号，供下游 skill 自检
  status,              // 见状态机
  fingerprint,         // 稳定内容指纹：基于 stage + 核心意图生成，用于识别同一意图
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

- 同一意图（如"退货资格初判"这条 skill）在多轮对话中被反复修改时，继续更新同一个系统 todo id，payload 字段被覆盖
- 用 `notes.fingerprint` 识别重复意图，避免因为用户换个说法就新建一条 todo
- 用户撤回某条意图（"算了不要这条"）→ `notes.status` 改为 `dismissed`，系统 todo id 保留；如 UI 不需要继续展示，再用 `todo.remove` 移除

## 与诊断 skill 的 todo 区分

- 诊断用途 todo 回答"还差什么"
- 流程 todo 记录目标阶段、下游 skill 和输入
- 两类都是系统 `todo`，只通过 `notes.target_skill` / `notes.stage` / `notes.kind` 区分用途；本 skill 只维护 `todo.notes`

---

## 阶段 1：material

`target_skill = ontology-extraction`

**最低门槛**：至少 1 份资料被指认归类，且对应 todo 的 `notes` 明确写出"要从中抽什么分类的本体 + 目标"。

**核心字段**：
- `category`: 资料类型（业务对象定义 / 决策规则 / 流程 SOP / 案例库 / 边界与约束 / 风格语料 / 其他）
- `objective`: 一句话目标，例如"抽出退货场景里所有可能的退款节点和对应的判定规则"
- `source_files`: 已上传的文件名列表
- `scene_hint`: 场景类型（客服 / 销售 / 内勤 / ...），来自场景判定（见 [scene-types.md](./scene-types.md)）
- `mode`: incremental（默认）/ full_replace（用户明确说"全量替换"时）

**dispatch 时机**：用户表示"先这些"或"暂时就这么多" + 至少 1 条 todo 达到明确度。

---

## 阶段 2：skill

`target_skill = skill-generation`

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
- 主路径：用户用对话描述能力 → 你引导 → 形成带结构化 `notes` 的 todo
- 二级路径：用户上传现成的 skill 文件 → 直接形成 todo（标记 `from_upload: true`），不必再追问明确度

**不要做的事**：
- 不要把行为约束（如"不能承诺金额"）放进 skill description——那归 agent.md（走配置文件治理路径）
- 不要为每个细小动作都创建一条 skill；3-7 条覆盖一个数字员工的主线工作通常合适
- 不要替用户决定 skill 之间怎么协作——技能生成 skill 自己会处理

**dispatch 时机**：至少 1 条 skill todo 达到明确度，且用户表示"先这些" → 发 dispatch；后续用户继续补充新 skill 时再追加 dispatch。

---

## 阶段 3：external

`target_skill = external-config`

**最低门槛**：每条外部能力都明确 `category` + `objective` + `target_system`；或者用户明确表达"不需要外部系统"（标记 skipped）。

**核心字段**：

| 字段 | 说明 |
|---|---|
| `category` | read / write / notify / search / transform 中之一 |
| `objective` | 一句话目标，例如"在用户咨询时，从 CRM 拉到该用户的最近 3 个订单" |
| `target_system` | 目标系统名（CRM / ERP / 企微 / 钉钉 / 自有 OA 等，含厂商或自研标识） |
| `linked_skills` | 这个能力被哪条 skill 用到（指对应 skill 阶段的系统 todo id 列表） |
| `auth_kind` | 凭据形式（OAuth / Bearer Token / 长期 Key 等），**不含凭据值** |
| `kind` | normal / skip |

**凭据规则（强约束，与 SKILL.md 顶层一致）**：
- token / 密钥 / 密码 / API Key 等**绝不在会话里收集**
- 用户在会话里如果输入了凭据，立刻提示"这类信息请填到右侧表单，不要在对话里发"
- todo `notes` 里只描述"需要凭据 X 的形式"，不带值

**用户跳过分支**：
- 用户明确说"不需要外部系统"或等价表述 → 形成一条 `kind: skip` 的 todo
- 这个 todo 同样作为信号传给系统，等价于阶段已走过

**dispatch 时机**：每条新的外部能力 todo 达到明确度后即可发 dispatch；表单里的凭据字段由用户在右侧自己填，不影响本 skill 发信号的时机。
