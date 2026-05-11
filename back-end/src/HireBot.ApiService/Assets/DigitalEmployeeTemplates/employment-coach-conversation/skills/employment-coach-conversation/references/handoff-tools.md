# Handoff tool 结构化交接合约

本文件定义 `employment-coach-conversation` 维护下游交接 todo 的唯一入口。所有要交给 `ontology-extraction`、`skill-generation` 或 `external-config` 的事项，都必须通过 Gateway 内置 Handoff tool 中由 `OpenClaw:Handoff` 配置声明的 `employment-coach` workflow 维护为当前会话 session 下的结构化 Handoff todo。

Handoff todo 是“交给谁、带什么输入、做到哪一步”的工作单元。它可以投影给 UI，但 canonical 状态、`session_id` 和 payload 以 Handoff tool 返回的数据为准。

## 目录

- [工具面](#工具面)
- [返回结构](#返回结构)
- [通用结构](#通用结构)
- [状态机](#状态机)
- [单条 Handoff todo 的完成判断](#单条-handoff-todo-的完成判断)
- [ID 与字段规范](#id-与字段规范)
- [阶段 1：material](#阶段-1material)
- [阶段 2：skill](#阶段-2skill)
- [阶段 3：external](#阶段-3external)
- [写入红线](#写入红线)

## 工具面

| action | 用途 | 关键输入 | 关键输出 |
| --- | --- | --- | --- |
| `upsert` | 新建或按 `fingerprint` 更新同一条 Handoff todo | `title`、`stage`、`target_skill`、`payload`、`fingerprint` | `SessionHandoffMutationResponse`：`session_id`、`item`、`items` |
| `patch` | 修改字段、payload 或来源摘要 | `handoff_id`、`patch`、可选 `expected_revision` | `SessionHandoffMutationResponse`：`session_id`、`item`、`items` |
| `transition` | 执行状态流转 | `handoff_id`、`status`、可选 `dispatch_id` / `callback_summary` | `SessionHandoffMutationResponse`：`session_id`、`item`、`items` |
| `list` | 读取当前 session 的结构化清单 | 可选 `stage`、`kind`、`target_skill`、`status`、`fingerprint` | `SessionHandoffListResponse`：`session_id`、`items` |
| `remove` | 用户撤销且 UI 不需要继续展示时移除投影 | `handoff_id`、`reason` | `SessionHandoffRemoveResponse`：`session_id`、`handoff_id`、`removed`、`reason`、`items` |

参数 schema 建议保持扁平，方便模型稳定调用：

```json
{
  "type": "object",
  "properties": {
    "action": { "type": "string", "enum": ["list", "upsert", "patch", "transition", "remove"], "default": "list" },
    "workflow_id": { "type": "string", "const": "employment-coach" },
    "handoff_id": { "type": "string" },
    "title": { "type": "string" },
    "kind": { "type": "string", "const": "handoff_todo" },
    "stage": { "type": "string", "enum": ["material", "skill", "external", "cross_stage"] },
    "target_skill": { "type": "string", "enum": ["ontology-extraction", "skill-generation", "external-config"] },
    "intent": { "type": "string" },
    "category": { "type": "string" },
    "payload": { "type": "object" },
    "source": { "type": "string" },
    "acceptance": { "type": "string" },
    "status": { "type": "string", "enum": ["drafting", "ready_to_dispatch", "dispatched", "dirty", "confirmed", "needs_review", "dismissed"] },
    "fingerprint": { "type": "string" },
    "patch": { "type": "object" },
    "expected_revision": { "type": "integer" },
    "dispatch_id": { "type": "string" },
    "callback_summary": { "type": "string" },
    "reason": { "type": "string" }
  },
  "required": ["action"]
}
```

使用规则：

- 新建或合并同一意图：调用 `handoff`，传 `action = upsert`，必须传 `fingerprint`，避免同一意图换个说法就生成新条目。
- `workflow_id` 可省略；省略时 Gateway 内置工具使用配置的默认 workflow。Employment Coach 场景需要显式写出时只能写 `employment-coach`。
- `session_id` 由宿主从当前会话上下文注入，skill 不手写、不伪造、不跨 session 查询。
- 更新字段或 payload：调用 `handoff`，传 `action = patch`，保持同一个 `handoff_id`。
- 发 dispatch 前：调用 `handoff`，传 `action = list` 读取目标阶段，逐条确认 `status = ready_to_dispatch`。
- 发 dispatch 时：输出 `<dispatch>` 前不要调用 `handoff` 把本轮条目标为 `dispatched`，也不要写入或猜测 `dispatch_id`；宿主会先校验 `ready_to_dispatch` / `dirty` 条目并生成真实调度记录。
- 用户确认下游结果：若成功条目仍是 `ready_to_dispatch`，先调用 `handoff` transition 到 `dispatched`；用户确认后再 transition 到 `confirmed`，并写入回传摘要或 artifact 引用。若条目是 `dirty`，不能用旧回传确认，必须先回到 `ready_to_dispatch` 并重发。
- 用户撤销：先把状态流转为 `dismissed`；只有 UI 不需要保留追溯时才调用 `handoff`，传 `action = remove`。
- 首轮进入会话时：即使用户还没提供资料，也必须 `upsert` 一条 `stage = material`、`target_skill = ontology-extraction`、`status = drafting` 的资料收集 Handoff todo；后续收到资料后 `patch` 同一条，不能等上传后才第一次建工单。

不要用对话正文、memory、临时文件或通用 todo 工具另维护一套清单。


## 返回结构

Handoff tool 成功时返回 JSON 字符串；失败时返回以 `Error:` 开头的普通字符串。调用方必须先判断是否为错误字符串，再把成功结果作为 JSON 解析。不要把错误字符串当作空列表、成功 mutation 或已完成状态。

所有成功响应中的 `items` 都只包含当前 `workflow_id` 下、当前 session 内的 Handoff todo；不会返回其他 workflow 或其他 session 的数据。`item` 表示本次被新建或更新的单条 Handoff todo，结构见[通用结构](#通用结构)。

### `list` 返回

`list` 返回当前 session 中匹配过滤条件的结构化清单。未传过滤条件时返回当前 workflow 的全部 Handoff todo；传入 `stage`、`kind`、`target_skill`、`status` 或 `fingerprint` 时按精确值过滤。

```json
{
  "session_id": "session_20260508_001",
  "items": [
    {
      "session_id": "session_20260508_001",
      "workflow_id": "employment-coach",
      "handoff_id": "m_6d23a9f4b1c8402a",
      "title": "资料：抽取售后 SOP",
      "kind": "handoff_todo",
      "stage": "material",
      "target_skill": "ontology-extraction",
      "intent": "从售后 SOP 中抽取退货规则和流程节点",
      "category": "流程 SOP",
      "payload": {
        "objective": "抽出退货场景里的流程节点、判定规则和边界约束",
        "source_files": ["return-sop.md"],
        "scene_hint": "客服",
        "mode": "incremental"
      },
      "source": "用户上传 return-sop.md",
      "acceptance": "ontology-extraction 回传的切片能覆盖退货节点和判定规则",
      "status": "ready_to_dispatch",
      "fingerprint": "material:return-sop",
      "related_todos": [],
      "related_files": ["return-sop.md"],
      "revision": 2,
      "created_at": "2026-05-08T02:10:00Z",
      "updated_at": "2026-05-08T02:16:00Z",
      "dispatch_id": null,
      "callback_summary": null
    }
  ]
}
```

字段含义：

- `session_id`：宿主注入的当前会话 id。
- `items`：过滤后的 Handoff todo 数组；数组元素是完整 item，不是 id 列表。

### `upsert` 返回

`upsert` 用 `fingerprint` 在当前 workflow + session 内查重。没有同指纹 item 时创建新 item；已有同指纹 item 时保留原 `handoff_id` 和 `created_at`，合并新输入并递增 `revision`。

```json
{
  "session_id": "session_20260508_001",
  "item": {
    "session_id": "session_20260508_001",
    "workflow_id": "employment-coach",
    "handoff_id": "s_7c7f4dc9101d44fb",
    "title": "技能：退货资格初判",
    "kind": "handoff_todo",
    "stage": "skill",
    "target_skill": "skill-generation",
    "intent": "生成退货资格初判技能",
    "category": "判定",
    "payload": {
      "skills": [
        {
          "origin": "conversation",
          "generation_action": "generate_new",
          "skill_name": "退货资格初判",
          "skill_description": "根据订单状态、商品类型和购买时间判断是否符合退货条件",
          "trigger": "用户提出退货、退款或退掉商品",
          "expected_output": "结论、依据和必要时的人工介入建议",
          "from_upload": false
        }
      ]
    },
    "source": "material 阶段回传和用户确认",
    "acceptance": "skill-generation 产出的 skill 文件能匹配该技能定义",
    "status": "ready_to_dispatch",
    "fingerprint": "skill:return-qualification",
    "related_todos": ["m_6d23a9f4b1c8402a"],
    "related_files": [],
    "revision": 1,
    "created_at": "2026-05-08T03:00:00Z",
    "updated_at": "2026-05-08T03:00:00Z",
    "dispatch_id": null,
    "callback_summary": null
  },
  "items": []
}
```

字段含义：

- `item`：本次创建或按 `fingerprint` 更新后的完整 Handoff todo。
- `items`：mutation 后当前 workflow 的完整 Handoff todo 列表。实际返回会包含 `item` 本身；示例中省略为 `[]` 仅表示列表结构。
- 新建时 `handoff_id` 由工具生成，Employment Coach 中 `material` 使用 `m_` 前缀，`skill` 使用 `s_` 前缀，`external` 使用 `e_` 前缀。
- 新建时 `revision = 1`；更新已有 item 时 `revision` 递增。
- `payload` 是对象级递归合并：已有值保留，新对象字段覆盖或补充同名字段；数组和标量按新值整体替换。

### `patch` 返回

`patch` 只修改 `patch` 对象中给出的字段，并返回 mutation 响应。推荐传入 `expected_revision`；如果当前版本不匹配，工具返回 `Error: expected_revision mismatch...`，不会写入。

```json
{
  "session_id": "session_20260508_001",
  "item": {
    "session_id": "session_20260508_001",
    "workflow_id": "employment-coach",
    "handoff_id": "m_6d23a9f4b1c8402a",
    "title": "资料：抽取售后 SOP",
    "kind": "handoff_todo",
    "stage": "material",
    "target_skill": "ontology-extraction",
    "intent": "从售后 SOP 中抽取退货规则和流程节点",
    "category": "流程 SOP",
    "payload": {
      "objective": "抽出退货场景里的流程节点、判定规则和边界约束",
      "source_files": ["return-sop.md"],
      "scene_hint": "客服",
      "mode": "incremental"
    },
    "source": "用户上传 return-sop.md，并补充退货例外说明",
    "acceptance": "ontology-extraction 回传的切片能覆盖退货节点、判定规则和例外条件",
    "status": "ready_to_dispatch",
    "fingerprint": "material:return-sop",
    "related_todos": [],
    "related_files": ["return-sop.md"],
    "revision": 3,
    "created_at": "2026-05-08T02:10:00Z",
    "updated_at": "2026-05-08T02:20:00Z",
    "dispatch_id": null,
    "callback_summary": null
  },
  "items": []
}
```

字段含义：

- `item`：patch 后的完整 item；不是只返回 patch delta。
- `items`：patch 后当前 workflow 的完整列表。
- `patch.payload` 和 `upsert.payload` 一样按对象递归合并；未出现在 `patch` 里的字段保持不变。
- 如果 `patch.status` 改变状态，仍必须符合状态机合法转移。
- 如果 `patch.fingerprint` 改到另一个已存在指纹，工具会返回错误，避免两个 item 合并成歧义状态。

### `transition` 返回

`transition` 只做状态流转，并可同时写入 `dispatch_id` 或 `callback_summary`。它不会修改 `title`、`payload`、`source`、`acceptance` 等内容字段。

```json
{
  "session_id": "session_20260508_001",
  "item": {
    "session_id": "session_20260508_001",
    "workflow_id": "employment-coach",
    "handoff_id": "s_7c7f4dc9101d44fb",
    "title": "技能：退货资格初判",
    "kind": "handoff_todo",
    "stage": "skill",
    "target_skill": "skill-generation",
    "intent": "生成退货资格初判技能",
    "category": "判定",
    "payload": {
      "skills": []
    },
    "source": "material 阶段回传和用户确认",
    "acceptance": "skill-generation 产出的 skill 文件能匹配该技能定义",
    "status": "dispatched",
    "fingerprint": "skill:return-qualification",
    "related_todos": ["m_6d23a9f4b1c8402a"],
    "related_files": [],
    "revision": 2,
    "created_at": "2026-05-08T03:00:00Z",
    "updated_at": "2026-05-08T03:06:00Z",
    "dispatch_id": "dispatch_20260508_030600",
    "callback_summary": null
  },
  "items": []
}
```

字段含义：

- `item.status`：流转后的状态；只有合法转移会写入。
- `item.dispatch_id`：发出 dispatch 后写入的调度 id；未传时保留旧值。
- `item.callback_summary`：收到下游回传、用户确认后写入的摘要；未传时保留旧值。
- `item.revision`：每次成功 transition 都递增 1。

### `remove` 返回

`remove` 只在用户撤销且 UI 不需要继续保留追溯时使用。它从当前 workflow 的 session Handoff 列表中删除目标 item，并返回删除后的列表。

```json
{
  "session_id": "session_20260508_001",
  "handoff_id": "e_9f4d2ab17c8840f1",
  "removed": true,
  "reason": "用户明确取消外部系统对接，UI 不再展示该投影",
  "items": []
}
```

字段含义：

- `handoff_id`：被删除的 Handoff todo id。
- `removed`：成功删除时固定为 `true`。
- `reason`：调用方传入的删除原因，会随响应原样返回。
- `items`：删除后当前 workflow 剩余的完整 Handoff todo 列表。


## 通用结构

Handoff todo 至少包含以下字段。字段名使用 snake_case。

```json
{
  "session_id": "session_20260508_001",
  "workflow_id": "employment-coach",
  "handoff_id": "s_refund_init_001",
  "title": "技能：退货资格初判",
  "kind": "handoff_todo",
  "stage": "skill",
  "target_skill": "skill-generation",
  "intent": "生成退货资格初判技能",
  "category": "判定",
  "payload": {},
  "source": "用户描述退货咨询主线",
  "acceptance": "skill-generation 产出的 skill 文件能匹配该 Handoff todo 的 name + description",
  "status": "drafting",
  "fingerprint": "skill:return-qualification",
  "related_todos": [],
  "related_files": [],
  "revision": 1,
  "created_at": "2026-05-07T10:30:00Z",
  "updated_at": "2026-05-07T10:30:00Z"
}
```

字段说明：

- `session_id`：当前会话 session id，是 Handoff todo 的存储边界；由宿主注入并随 item 返回。
- `workflow_id`：当前交接流程作用域；本文件固定为 `employment-coach`，用于与其他通用 Handoff workflow 隔离。
- `handoff_id`：Handoff tool 返回的稳定 id，是 canonical id。
- `title`：给用户和 UI 看的短标题。
- `kind`：流程交接项固定使用 `handoff_todo`。
- `stage`：`material` / `skill` / `external` / `cross_stage`。
- `target_skill`：`ontology-extraction` / `skill-generation` / `external-config`。
- `intent`：一句话目标，给用户可读。
- `category`：阶段相关分类。
- `payload`：阶段相关结构化字段。
- `source`：来自对话、上传文件或系统回传的依据。
- `acceptance`：下游完成后如何判断可确认。
- `status`：见状态机。
- `fingerprint`：当前 session 范围内基于 `stage` + `target_skill` + 核心意图生成的稳定指纹。同一意图无论说法如何变化都应该保持一致，供 `upsert` 合并使用；skill 不需要把 `session_id` 写进 fingerprint。
- `revision`：工具维护的并发版本；多轮更新时递增。

## 活跃项、阻塞项与合流

- 活跃 Handoff todo：同一 `stage` / `target_skill` 下，`status` 不是 `confirmed` / `dismissed` 的条目。
- 阻塞 Handoff todo：活跃项中 `status = drafting` / `dispatched` / `dirty` / `needs_review` 的条目；进入下一阶段前还要把 `ready_to_dispatch` 发出并确认，不能把它当完成态。
- 每次 `upsert` 前必须先 `list` 当前阶段活跃项，检查 `fingerprint`、`payload.source_files`、`payload.objective`、核心意图是否已存在。
- 新信息补齐同一意图时，优先 `patch` 原 `handoff_id` 并保留原 `fingerprint`；不要靠换标题或扩大描述创建第二条。
- 新信息覆盖旧草稿时，先把旧草稿补齐后转为 `ready_to_dispatch`；只有用户明确撤销旧范围时，才把旧草稿转为 `dismissed`。
- 不允许同一阶段中存在“旧草稿仍 `drafting`，新完整项已 `ready_to_dispatch`”且两者指向同一资料、同一来源文件或父子包含关系。

## 状态机

| 状态 | 含义 |
| --- | --- |
| `drafting` | 还在引导中，明确度不够，不能 dispatch |
| `ready_to_dispatch` | 明确度达标，可在合适时机发 dispatch |
| `dispatched` | 已发 dispatch，等下游回传 |
| `dirty` | 在 `dispatched` 期间被用户改动，回传到达后需重发 |
| `confirmed` | 下游回传 + 用户确认通过 |
| `needs_review` | 上游配置文件改动后需要复核 |
| `dismissed` | 用户主动撤销，保留 id 不再 dispatch |

合法转移：

- `drafting` → `ready_to_dispatch` / `dismissed`
- `ready_to_dispatch` → `drafting` / `dispatched` / `dismissed`
- `dispatched` → `dirty` / `confirmed`
- `dirty` → `ready_to_dispatch`
- `confirmed` → `needs_review` / `dismissed`
- `needs_review` → `confirmed` / `ready_to_dispatch`

## 单条 Handoff todo 的完成判断

- `drafting`：只是草稿，说明信息还没谈够，既不能 dispatch，也不能计入阶段完成
- `ready_to_dispatch`：说明该条交接单已经达到下游可消化明确度，但仍然只是“可发出”，不是“已完成”
- `dispatched`：说明已经发给下游，正在等回传；等待期间依然不能把这条算作完成
- `dirty`：说明这条在等待回传或回传后又被用户改动了，需要重新整理或重发；显然不能计入完成
- `confirmed`：这是单条 Handoff todo 的**完成态**。必须同时满足“下游有回传结果”和“用户认可这次回传可用”
- `needs_review`：曾经完成过，但被上游规则、边界或配置改动影响，需要复核；复核完成前不要继续把它当成稳定完成项
- `dismissed`：只有在用户明确撤销、明确不再需要这条交接时才成立。`dismissed` 不是“自动视作完成”，它只是“停止继续推进这条”

额外约束：

- 不要把“已经创建 Handoff todo”误当成“已经完成工作”
- 不要把“已经发出 dispatch”误当成“已经完成工作”
- 只有 `confirmed` 才代表这条 Handoff todo 对应的交接闭环已经完成
- 如果一条 required Handoff todo 被 `dismissed`，必须是用户明确改变了范围、取消了需求，或切换到了 skip 分支；否则不能靠 `dismissed` 偷渡阶段完成
- 用户追问“完了吗 / 下一步”时，必须先 `list` 查 Handoff todo 状态；`dispatched` 只能答“已发出，等回传或确认”，不能答“完成”。
- 已收到下游 `dispatch_callback` 时，也要先把摘要给用户确认；用户确认后 transition 到 `confirmed`，再创建下一阶段 Handoff todo。

## ID 与字段规范

- Handoff tool 返回的 `session_id` + `handoff_id` 是存储主键；不要在对话里伪造不存在的 id。
- `<dispatch>` 和 `<dispatch_callback>` 只使用 `handoff_ids` / `handoff_id` 表达主键；系统层不得生成非 Handoff 主键字段，skill 文档、payload 与 artifact 一律以 Handoff id 为主键。
- 宿主必须以 `SessionMetadata.HandoffItems` 作为唯一 workflow 主存储；不允许重新把通用系统 todo 工具或其他投影层变成 canonical 数据源。
- 下游 skill 必须消费 `handoff_todos` 数组里的完整结构，并校验每条 `session_id` 属于当前会话；不能只靠 id 重新猜 payload。


## 阶段 1：material

`target_skill = ontology-extraction`

**最低门槛**：至少 1 份资料被指认归类，且对应 Handoff todo 明确写出“要从中抽什么分类的本体 + 目标”。


**首轮初始化**：首次进入会话时必须先创建或更新一条 material Handoff todo，状态为 `drafting`。这条 todo 表达“等待用户提供第一批业务资料后抽取本体”，不是可 dispatch 的完成项。建议字段：

- `title`: `资料：补充第一批业务资料`
- `category`: `资料收集`
- `payload.objective`: `等待用户上传或描述第一批业务资料后，抽取业务对象、流程、规则、字段和边界约束`
- `payload.scene_hint`: 从模板摘要推断，无法判断时写 `unknown`
- `payload.mode`: `incremental`
- `payload.missing_inputs`: [`source_files 或 source_content`]
- `source`: `冷启动开场，尚未收到用户业务资料`
- `status`: `drafting`
- `fingerprint`: `material:first-batch`

用户后续上传文件或补充正文时，必须先 `list` 当前 material 活跃项；如果存在首轮 `material:first-batch` 草稿，或已有条目与新资料属于同一来源、同一目标或父子包含关系，优先 `patch` 原 `handoff_id`，补齐 `payload.source_files` / `payload.source_content`、资料分类和抽取目标。只有确认是全新资料范围时才 `upsert` 新条目；不要另起一条完整资料工单，把首轮草稿留在 `drafting`。

**核心字段**：

- `category`: 资料类型（业务对象定义 / 决策规则 / 流程 SOP / 案例库 / 边界与约束 / 风格语料 / 其他）
- `payload.objective`: 一句话目标，例如“抽出退货场景里所有可能的退款节点和对应的判定规则”
- `payload.source_files`: 已上传的文件名列表；如果资料完全来自对话正文而非上传文件，使用 `payload.source_content` 或 `payload.source_summary`，并在 `source` 里说明来源
- `payload.scene_hint`: 场景类型（客服 / 销售 / 内勤 / ...）
- `payload.mode`: `incremental`（默认）/ `full_replace`

**明确度达标后**：状态可从 `drafting` 转为 `ready_to_dispatch`。上传资料路径必须具备 `category`、`payload.objective`、`payload.source_files`、`payload.scene_hint`；对话资料路径必须具备 `category`、`payload.objective`、`payload.source_content` 或 `payload.source_summary`、`payload.scene_hint`。

**单条 todo 何时可记为 `confirmed`**：

- 下游已经基于这条 Handoff todo 产出可复述的抽取结果或切片更新结果
- 回传摘要能对应到这条 Handoff todo 的 `objective`、`source_files` 和当前资料批次
- 用户已经接受“这批资料先这样”的结果，不再要求立刻补改这条 material Handoff todo

**阶段完成条件**：

- 至少 1 份真实业务资料已经被纳入当前轮 material Handoff todo
- 当前轮要处理的上传资料都已经被覆盖，不存在“用户明确要处理但还没进入任何 material Handoff todo”的文件
- 每条参与当前批次推进的 material Handoff todo 都具备分类、目标和来源文件，并已进入 `confirmed`
- 当前批次不再存在阻塞进入技能阶段的 material Handoff todo：`drafting` / `ready_to_dispatch` / `dispatched` / `dirty`
- 用户已经明确表达“先这些”“这一批先这样”或等价意思，允许以当前批次作为后续技能阶段输入

不要用一条泛化的“补资料” Handoff todo 代替整个资料阶段；资料阶段完成必须对应到真实文件覆盖和真实回传确认。

---

## 阶段 2：skill

`target_skill = skill-generation`

**最低门槛**：`payload.skills` 必须是 Skill 数组，且至少 1 个元素；数组必须覆盖初始数字员工模板包里已有的 skill，以及本轮用户新增、需要生成的 skill。每个 Skill 都必须用 `generation_action` 区分“已有复用”还是“需要新生成”，并具备明确的 `skill_name` + `skill_description`，能说清触发条件和期望输出。

**进入阶段时的 upsert 规则**：material 阶段所有参与当前批次的 Handoff todo 必须已是 `confirmed`。用户表达继续后，先 `upsert` 一条 skill 阶段 Handoff todo，再向用户反馈进入技能阶段；如果根据 material 回传已经能定义 `payload.skills[]`，状态设为 `ready_to_dispatch`，否则设为 `drafting` 并追问缺口。不要只在对话里说“接下来进入技能阶段”而不创建 `target_skill = skill-generation` 的 Handoff todo。

**核心字段**：

- `payload.skills`: Skill 数组，`minItems = 1`
- `payload.skills[].origin`: `template_package` / `conversation` / `upload` 之一，且和真实来源一致
- `payload.skills[].generation_action`: `reuse_existing` / `generate_new` 之一；已有 skill 用 `reuse_existing`，本轮新增 skill 用 `generate_new`
- `payload.skills[].skill_name`: 技能名称，例如“退货资格初判”
- `payload.skills[].skill_description`
- `payload.skills[].trigger`
- `payload.skills[].expected_output`
- `payload.skills[].from_upload`
- `payload.skills[].existing_skill_slug`: `generation_action = reuse_existing` 时必填
- `payload.skills[].existing_artifact_path`: `generation_action = reuse_existing` 时必填，指向模板包已有 skill 产物
- `payload.skills[].template_package_id`: 来自模板包时填写模板包 id，例如 `customer-service-starter`；非模板来源可为空
- `payload.skills[].template_package_version`: 来自模板包时填写

`generation_action = reuse_existing` 的条目表示该 skill 已在初始数字员工模板包或上传的现成 skill 中存在，不要求 `skill-generation` 重新生成文件；`generation_action = generate_new` 的条目才是本轮需要生成的新业务 skill。

**payload 示例**：

```json
{
  "skills": [
    {
      "origin": "template_package",
      "generation_action": "reuse_existing",
      "skill_name": "订单状态查询",
      "skill_description": "根据订单号查询订单状态、物流进度和基础异常原因，并给出下一步指引",
      "trigger": "用户询问订单状态 / 物流进度 / 订单到哪了，且能匹配到订单号",
      "expected_output": "一条订单状态回复，以及必要时的人工转接建议",
      "from_upload": false,
      "existing_skill_slug": "order-status-query",
      "existing_artifact_path": "skills/order-status-query/SKILL.md",
      "template_package_id": "customer-service-starter",
      "template_package_version": "1.0.0"
    },
    {
      "origin": "conversation",
      "generation_action": "generate_new",
      "skill_name": "退货资格初判",
      "skill_description": "在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户",
      "trigger": "用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单",
      "expected_output": "一条回复消息（含结论 + 依据），以及一条工单流转建议（如需要人工介入）",
      "from_upload": false
    }
  ] 
}
```

**字段明确度对照**：

| 字段 | 不够明确的样子 | 够明确的样子 |
| --- | --- | --- |
| `payload.skills` | 缺失、不是数组，或只放本轮新能力、漏掉模板包已有 skill | Skill 数组，至少 1 项；同时覆盖初始数字员工模板包已有 skill 与本轮新增 skill |
| `origin` | 空、随手都写 `conversation`，无法判断来自模板、对话还是上传 | `template_package` / `conversation` / `upload` 之一，且和真实来源一致 |
| `generation_action` | 空、都写成 `generate_new`，导致已有 skill 被重复生成 | `reuse_existing` / `generate_new` 之一；已有 skill 用 `reuse_existing`，本轮新增 skill 用 `generate_new` |
| `skill_name` | “处理售后” | “退货资格初判” |
| `skill_description` | “用户问退货怎么办时回应一下” | “在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户” |
| `trigger` | “用户问起来” | “用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单” |
| `expected_output` | “回复用户” | “一条回复消息（含结论 + 依据），以及一条工单流转建议（如需要人工介入）” |
| `from_upload` | 缺失，或与 `origin` 冲突（例如 `origin = upload` 但 `from_upload = false`） | 布尔值；上传现成 skill 时为 `true`，非上传来源为 `false` |
| `existing_skill_slug` | `reuse_existing` 时为空，或写成中文显示名 | `reuse_existing` 时填写已有 skill slug，例如 `order-status-query`；`generate_new` 可为空 |
| `existing_artifact_path` | `reuse_existing` 时为空，或写成绝对路径 / 不存在路径 | `reuse_existing` 时填写模板包内相对路径，例如 `skills/order-status-query/SKILL.md`；`generate_new` 可为空 |
| `template_package_id` | `origin = template_package` 时为空，无法追溯来自哪个初始模板包 | 来自模板包时填写模板包 id，例如 `customer-service-starter`；非模板来源可为空 |
| `template_package_version` | `origin = template_package` 时为空，无法追溯版本 | 来自模板包时填写模板包版本，例如 `1.0.0`；非模板来源可为空 |

**单条 todo 何时可记为 `confirmed`**：

- 下游回传的 skill 结果能对应到这条 Handoff todo 的 `payload.skills[]`
- 需要新生成的条目已经生成出可用产物；需要复用的条目已经正确保留引用，不发生重复生成或错误覆盖
- 用户认可这次技能定义已经可用，不要求当场继续补 trigger、边界或输出格式

**阶段完成条件**：

- 默认技能基线已经盘清，用户和教练都明确哪些能力复用、哪些能力新增
- 所有真正需要推进的 skill Handoff todo 都已经进入 `confirmed`
- 如果没有新增项，也必须得到用户对“当前技能基线已经足够”的明确确认，不能因为“没有待办”就自动视为完成
- 当前不再存在阻塞进入 external 阶段的 skill Handoff todo：`drafting` / `ready_to_dispatch` / `dispatched` / `dirty`

模板已有 skill 不是天然的“待完成工单”；只有真正需要补充、重做或新增的能力才进入 skill 阶段的完成统计。

---

## 阶段 3：external

`target_skill = external-config`

**最低门槛**：`payload.external_capabilities` 必须是外部能力数组，且至少 1 个元素；每个普通外部能力都明确 `category` + `objective` + `target_system` + `auth_kind` + 非空 `linked_skills`。`integration_methods` 是推荐字段，不是宿主 readiness 的硬门槛；或者用户明确表达“不需要外部系统”（数组内写 1 个 `kind = skip` 的跳过项）。

**核心字段**：

| 字段 | 说明 |
| --- | --- |
| `payload.external_capabilities` | 外部能力数组，`minItems = 1` |
| `payload.external_capabilities[].kind` | `normal` / `skip` |
| `payload.external_capabilities[].category` | `read` / `write` / `notify` / `search` / `transform` 中之一；`kind = skip` 时可为空 |
| `payload.external_capabilities[].objective` | 一句话目标，例如“在用户咨询时，从 CRM 拉到该用户的最近 3 个订单” |
| `payload.external_capabilities[].target_system` | 目标系统名（CRM / ERP / 企微 / 钉钉 / 自有 OA 等） |
| `payload.external_capabilities[].integration_methods` | 对接方式数组，表示计划通过哪些接入通道实现该能力；建议值为 `mcp` / `cli` / `http_api` / `sdk` / `webhook` / `manual` / `unknown`，不写真实 endpoint、命令参数或凭据 |
| `payload.external_capabilities[].linked_skills` | 这个能力被哪条 skill 用到，使用 skill 阶段 Handoff id |
| `payload.external_capabilities[].auth_kind` | 凭据形式（OAuth / Bearer Token / 长期 Key 等），不含凭据值 |
| `payload.external_capabilities[].credential_slot` | `auth_kind != none` 时建议填写安全表单槽位名，例如 `crm_order_read_api_key`；不写真实凭据值 |
| `payload.external_capabilities[].required_fields` | 需要读取、写入、通知或转换的字段列表 |

**payload 示例**：

```json
{
  "external_capabilities": [
    {
      "kind": "normal",
      "category": "read",
      "objective": "在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型",
      "target_system": "销售易 CRM",
      "integration_methods": ["mcp"],
      "linked_skills": ["s_seven_day_init_001", "s_nonstandard_assessment_001"],
      "auth_kind": "API Key",
      "credential_slot": "crm_order_read_api_key",
      "required_fields": ["order_id", "created_at", "status", "customer_tier", "product_category"]
    }
  ]
}
```

**单条 todo 何时可记为 `confirmed`**：

- 下游已经基于这条 Handoff todo 产出可追溯的 external 配置草案、skip 记录或其他约定产物
- 回传结果与这条 Handoff todo 的 `external_capabilities[]` 对得上，不是泛泛而谈
- 如果该条能力需要凭据，至少已经把凭据类型和槽位约束说明清楚；用户也理解凭据需要走安全表单而不是聊天框
- 用户认可这条外部能力的配置结果或 skip 结果可接受

**阶段完成条件**：

- 每条 required external Handoff todo 都已经形成明确能力定义，并进入 `confirmed`，或者在用户明确不接外部系统时走完 skip 分支
- 当前批次不再存在阻塞出口的 external Handoff todo：`drafting` / `ready_to_dispatch` / `dispatched` / `dirty`
- 对 `auth_kind != none` 的能力，不要把“已经生成配置草案”误当成“已经彻底完成”；进入下一阶段前要确认凭据绑定路径已经收口
- 一个系统若被拆成多条 external Handoff todo，也必须逐条确认，不要因为其中一条成功就把整个外部阶段视为完成

外部阶段完成看的是“能力是否闭环”，不是“配置文件是否先被写出一版”。

## 写入红线

- 不在会话、Handoff payload、source、acceptance、callback 或 artifact 摘要中保存真实 token、密钥、密码、API Key、连接串。
- 不把 Handoff todo 当作阶段占位物；每条都必须有可交给下游执行的目标。
- 不绕过 Handoff tool 自建文件、内存清单或对话内清单。
- 不让下游 skill 修改 `employment-coach-conversation` 维护的流程 Handoff todo；下游只回传 callback，确认和状态合流由上游完成。
