# 阶段产物 data 字段结构

本文件定义 `emit_artifact` 调用中各 `artifactType` 的 `data` 字段结构，供 `employment-coach-conversation` skill 构造 artifact payload 时参考。

---

## ⛔ 禁止字段与禁止模式

以下内容**绝对不能**出现在任何 `data` 字段中：

| 禁止的字段名 / 值 | 来源说明 | 应改用 |
|-----------------|---------|-------|
| 顶层 `status` 字段（除确认门和打包相关 artifact） | 旧 handoff 状态机残留 | 非确认门、非打包 artifact 不需要顶层 status；确认门只允许 `status: "waiting_confirm"`；各 item 内部有自己的 `status: pending/ready` |
| `status: "ready_to_dispatch"` | 旧 dispatch 协议 | 用 `isTerminal: true` 表示阶段完成 |
| `status: "dispatched"` / `"confirmed"` / `"needs_review"` / `"dirty"` | 旧 handoff 状态机 | 同上 |
| `capabilities` 字段 | 旧格式 | 改用 `items[]` |
| `materials` 字段（顶层） | 旧格式 | 改用 `items[]` |
| `scene_hint` 字段 | 旧格式 | 不需要，schema 中无此字段 |
| `dispatch_payload` / `handoff_todos` / `dispatch_target` | 旧 dispatch 协议 | 全部删除 |
| `skill_generation_trigger` / `stage2_analysis` / `stage3_skills` / `skills_pipeline` | 自造阶段 / 自造技能流水线 | 用 `skill_generation_ready`、`ontology_projection_done`、`skill_generation_progress` 等协议内 artifact |

**data 字段的合法顶层 key 只有下方各 artifactType 示例中明确列出的字段。任何不在示例中的 key 均视为错误。**

**唯一例外**：确认门 artifact（`material_handoff_ready`、`skill_definition_entry_ready`、`skill_definition_ready`、`ontology_projection_ready`、`skill_generation_ready`、`external_system_entry_ready`、`packaging_testcases_ready`、`review_readiness`）允许顶层 `status: "waiting_confirm"`；`stage4_packaging` 的打包/审查相关 artifact（`packaging_progress`、`packaging_testcases_progress`、`packaging_testcases_done`、`review_progress`、`review_report`）允许顶层 `status`；其中 `packaging_progress.status` 仅允许 `waiting_downstream` / `packing`，`review_report.status` 仅允许 `PASS` / `PASS_WITH_CONCERNS` / `FAIL`。

**对话回复中同样禁止出现以下词语**：`dispatch 闭环`、`dispatch 信号`、`handoff 工单`、`ready_to_dispatch`、`dispatch 给下游`、`实例包`、`产物包`、`本体切片`、`ontology`、`projection`、`artifact`、`workorder`。内部协议字段可以保留这些词，但面向用户的 `label` 与自然语言回复必须使用术语表里的用户侧说法。

---

## 阶段 1：资料（stage1_material）

### material_collection_progress（进度更新，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "summary": "已进入资料阶段，等待用户上传或描述业务资料",
  "requested_categories": [
    {
      "title": "历史工单",
      "description": "优先上传最近处理不顺的真实案例",
      "examples": ["投诉工单", "售后记录"]
    }
  ],
  "collected_count": 2,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "source_path": "uploads/非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件与处置路径",
      "status": "pending"
    },
    {
      "title": "客服话术风格",
      "source_hint": "用户描述",
      "source_path": null,
      "category": "话术风格",
      "objective": "抽取标准化服务语言特征",
      "status": "pending"
    }
  ],
  "notes": "用户还在补充，尚未确认"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 初始化时必填 | 当前会话工作区真实绝对路径，供下游 skill 读取上传文件与写入产物 |
| `template_slug` | 初始化时必填 | 当前模板 slug |
| `summary` | 否 | 当前进度摘要 |
| `collected_count` | 已有资料后必填 | 当前已收集的资料条目数；开场亮灯但尚无资料时可省略 |
| `requested_categories[]` | 否 | 开场阶段建议用户优先上传的资料分类，最多 3 项；仅用于右侧资料阶段展示，不代表已收集资料 |
| `requested_categories[].title` | 是 | 资料分类名称，对用户可读，如"历史工单""SOP""产品手册" |
| `requested_categories[].description` | 否 | 为什么需要这类资料，一句话即可 |
| `requested_categories[].examples[]` | 否 | 该分类下的示例文件或来源，最多 2 个 |
| `items[]` | 已有资料后必填 | 已整理的资料清单；开场亮灯但尚无资料时可省略 |
| `items[].title` | 是 | 资料标题，对用户可读 |
| `items[].source_hint` | 是 | 来源描述（对用户可读，如"用户上传：sales.csv"或"用户描述"） |
| `items[].source_path` | 否 | 可被下游直接读取的实际文件路径。工作区资料填相对路径（如 `uploads/<文件名>`）；Gateway 直传资料必须先读 `/app/memory/media-cache/{mediaId}.json`，再填元数据 `path` 字段中的真实路径；纯描述来源填 `null`。不要填 `[FILE_URL:...]`、`/media/{mediaId}` 或无扩展名的 `/app/memory/media-cache/{mediaId}` 标记。**只要是上传文件，这个字段就是事实上的必填项；缺失时该条不能进入 terminal handoff。若是刚上传后的短暂同步窗口，可先等待最多 5 秒再决定是否阻断。** |
| `items[].category` | 是 | 资料分类：决策规则 / 话术风格 / 业务流程 / 数据字段 / 其他 |
| `items[].objective` | 是 | 本条资料要抽取的目标 |
| `items[].status` | 是 | `pending`（待处理）/ `ready`（已就绪）。**上传文件若缺少 `source_path`、文件不存在或内容未读到，只能保持 `pending`。** |
| `notes` | 否 | 补充说明 |

---

### material_handoff_ready（资料收口确认门，isTerminal: false）

```json
{
  "context_signature": "material-upload-batch-20260615143000",
  "status": "waiting_confirm",
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "total_items": 1,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "source_path": "uploads/非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件、处置档位和人工分流触发节点",
      "status": "ready"
    }
  ],
  "summary": "已上传历史排产与插单案例，等待确认是否开始分析业务资料",
  "next_artifact": "material_handoff_summary",
  "message": "资料已上传完成，请确认是否按当前资料开始分析业务资料。"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `context_signature` | 是 | 当前资料上下文签名；同一签名只展示一次，补充资料后必须变化 |
| `status` | 是 | 固定为 `waiting_confirm` |
| `workspace_root` | 建议 | 当前数字员工工作区真实路径；应与最近一次 `material_collection_progress` 一致 |
| `template_slug` | 建议 | 当前模板 slug；应与最近一次 `material_collection_progress` 一致 |
| `total_items` | 建议 | 当前已整理资料数量 |
| `items[]` | 建议 | 当前已整理资料清单；如果已有上传资料，建议原样透传 `source_path`，便于用户确认后生成 terminal 摘要 |
| `summary` | 是 | 对用户可读的资料收口摘要 |
| `next_artifact` | 是 | 固定为 `material_handoff_summary` |
| `message` | 否 | 面向用户的确认说明 |

补充约束：普通 assistant 文本不能替代该确认门。用户确认前不得发出 `material_handoff_summary`。`data` 禁止为空对象 `{}`；如果已经有资料条目，确认门必须能通过自身字段或最近一次 `material_collection_progress` 推导出同一批资料。

---

### material_handoff_summary（资料收口 / R1 输入，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "total_items": 3,
  "items": [
    {
      "title": "退货处理规则",
      "source_hint": "用户上传：非标退货处理规则.docx",
      "source_path": "uploads/非标退货处理规则.docx",
      "category": "决策规则",
      "objective": "抽取退货判定条件、处置档位和人工分流触发节点",
      "status": "ready"
    }
  ],
  "summary": "共整理 3 份业务资料，已确认抽取方向，准备分析业务资料",
  "force_advanced": false,
  "deferred_gaps": [],
  "notes": ""
}
```

字段说明：与 `material_collection_progress` 相同；terminal 时顶层必须透传会话初始化阶段锁定的真实 `workspace_root` 与 `template_slug`，`status` 全部为 `ready`，`source_path` 必须尽可能补全（有上传文件的条目**必填**），并补充 `summary` 字段。`ontology-slice-extraction` skill 将以 `workspace_root` 与 `source_path` 为准定位实际文件，`source_hint` 仅供人工阅读。**如果缺少 `workspace_root` / `template_slug`，或上传条目只有 `source_hint`、没有 `source_path`，或已经知道内容未能读取到，则不得发出 `material_handoff_summary`。**

强制推进相关字段：

| 字段 | 必填 | 说明 |
|------|------|------|
| `force_advanced` | 否 | `true` 表示用户选择了在资料缺口存在的情况下强制推进。默认 `false`。当用户满足最低门槛（≥1 份相关资料）但拒绝了教练的资料补充追问时设为 `true`。 |
| `deferred_gaps[]` | `force_advanced: true` 时建议填写 | 用户选择暂不补充的资料缺口列表（如 `["排产规则与约束清单", "齐套校验口径"]`），供后续阶段回补参考。 |
| `notes` | 否 | 补充说明。强制推进时建议记录缺口摘要，如 `"用户选择在排产规则和齐套口径暂缺的情况下强制推进。缺口已记录，后续阶段可回补。"` |

`material_handoff_summary` 发出后必须立即触发 R1；资料阶段整体完成还需要收到成功形态的 `ontology_slice_extraction_done`（`data.status === "completed"` 且 `completed_slices > 0`）。在成功形态的 `ontology_slice_extraction_done` 到达前，不得发 `skill_workorder_progress`，也不得进入技能定义收集。若收到 `data.status === "blocked"` 的 `ontology_slice_extraction_done`，表示 R1 已终止但资料阶段未完成，应停留在资料阶段并根据 `diagnostic` / `diagnostic_detail` 引导用户补充资料。

### ontology_slice_extraction_done（业务资料分析结果，isTerminal: true）

成功形态示例：

```json
{
  "status": "completed",
  "total_sources": 2,
  "completed_slices": 2,
  "slice_paths": ["ontology/return-policy.slice.json", "ontology/dialogue-style.slice.json"],
  "validation": "PASS"
}
```

阻断形态示例：

```json
{
  "status": "blocked",
  "total_sources": 1,
  "completed_slices": 0,
  "slice_paths": [],
  "validation": "FAIL",
  "diagnostic": "insufficient_material",
  "diagnostic_detail": "当前资料缺少可抽取的规则、案例、流程或约束正文。",
  "next_step": "补充资料后重新分析业务资料"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `status` | 是 | `completed` 或 `blocked`；只有 `completed` 才允许进入技能定义入口确认门 |
| `total_sources` | 是 | R1 输入资料数量 |
| `completed_slices` | 是 | 已成功产出的 slice 数量；进入技能定义必须大于 0 |
| `slice_paths[]` | 是 | 成功产出的 slice JSON 路径；`blocked` 时为空数组 |
| `validation` | 建议 | `PASS` / `WARNING` / `FAIL` 等校验摘要 |
| `diagnostic` | `blocked` 时必填 | `insufficient_material` / `source_unreadable` / `scan_error` |
| `diagnostic_detail` | `blocked` 时建议 | 面向业务资料缺口的可读说明 |
| `next_step` | `blocked` 时建议 | 下一步建议，通常为补充资料后重新分析 |

补充约束：blocked 形态是 terminal artifact，只结束本轮 R1，不完成资料阶段，不得触发 `skill_definition_entry_ready`。

---

## 阶段 2：技能（stage2_skill）

说明：阶段 2 是”技能”主阶段，技能清单定稿后进入“技能实现子流程”，内部固定拆成三个显式确认子步骤：
- 技能定义确认：通过 `skill_workorder_progress` / `skill_definition_ready` / `skill_workorder_summary` 表达（用户确认门）。
- 匹配技能数据确认：技能定义完成后发出 `ontology_projection_ready`，用户确认后才触发 projection pass（`ontology-slice-extraction` projection_pass 模式）。
- 技能生成确认/执行：`ontology_projection_done` 可消费后发 `skill_generation_ready`（用户确认门），确认后触发下游 `skill-generation`。

### skill_definition_entry_ready（技能定义入口确认门，isTerminal: false）

```json
{
  "context_signature": "material-summary-v1:ontology-slices-v1",
  "status": "waiting_confirm",
  "trigger_after": "ontology_slice_extraction_done",
  "message": "业务资料分析完成，请确认是否进入技能定义环节。"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `context_signature` | 是 | 当前资料摘要与业务资料分析结果的签名；同一签名只展示一次 |
| `status` | 是 | 固定为 `waiting_confirm` |
| `trigger_after` | 是 | 固定为 `ontology_slice_extraction_done` |
| `message` | 否 | 面向用户的确认说明 |

补充约束：用户确认前不得发出 `skill_workorder_progress`。普通 assistant 文本可以解释分析结果，但不能作为“是否进入技能定义”的状态来源。

---

### skill_workorder_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 2,
  "items": [
    {
      "name": "refund-eligibility-check",
      "skill_slug": "refund-eligibility-check",
      "display_name": "退货资格初判",
      "description": "在用户提出退货请求时，根据订单状态、商品类型和时限判断是否符合退货条件",
      "trigger": "用户消息中出现退货 / 退款等关键词且能匹配到订单",
      "expected_output": "输出退货资格判断、原因说明和下一步处理建议",
      "generation_action": "generate_new",
      "status": "pending"
    },
    {
      "name": "order-status-query",
      "skill_slug": "order-status-query",
      "display_name": "订单状态查询",
      "description": "根据订单号查询状态、物流进度和基础异常",
      "trigger": "用户询问订单状态 / 物流进度",
      "expected_output": "输出订单状态、物流节点和异常提示",
      "generation_action": "reuse_existing",
      "status": "ready"
    }
  ],
  "notes": "待用户确认是否还有其他技能"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `collected_count` | 是 | 当前已整理的 skill 数量 |
| `items[]` | 是 | skill 清单 |
| `items[].name` | 是 | skill slug（英文，下划线/短横线）；不得写中文显示名 |
| `items[].skill_slug` | 建议 | 与 `items[].name` 相同的稳定技能目录 slug；若历史数据中 `name` 被误写成显示名，前端会优先使用此字段 |
| `items[].display_name` | 是 | 对用户可读的技能名称 |
| `items[].description` | 是 | 技能能力描述 |
| `items[].trigger` | 是 | 触发条件 |
| `items[].expected_output` | 是 | 技能执行后的预期输出，用于下游生成实现与 projection 消费说明 |
| `items[].generation_action` | 是 | `generate_new`（新生成）/ `reuse_existing`（复用已有） |
| `items[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明 |

---

### skill_definition_ready（技能定义确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "skill_names": ["refund_eligibility_check", "order_status_query"],
  "items": [ "... 同 progress items ..." ],
  "summary": "已整理 4 个技能定义草案，等待确认技能清单",
  "next_step": "等待用户确认技能清单"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 建议 | 当前会话工作区真实绝对路径 |
| `template_slug` | 建议 | 当前模板 slug |
| `pending_skill_count` | 是 | 待确认的技能数量 |
| `skill_names[]` | 是 | 待确认的技能 slug 或名称 |
| `items[]` | 是 | 完整技能定义草案，字段与 `skill_workorder_summary.items[]` 保持一致，供确认后收口和下游流程复用 |
| `summary` | 是 | 对用户可读的技能定义草案摘要 |
| `next_step` | 是 | 固定描述下一步是等待用户确认技能清单 |

用户可见要求：`skill_definition_ready` 只作为内部确认门和状态驱动事件，前端聊天区不依赖 artifact 卡片展示技能清单。发出该 artifact 后，assistant 普通文本必须用编号清单列出待确认技能，每个技能独立成项，至少包含 `技能名`、`能力说明`、`触发条件`、`预期输出` 四段信息。禁止只把多个技能名串成一句后询问确认，并避免“以上 N 项”“见卡片”等悬空指代。

补充约束：用户确认前不得发出 `skill_workorder_summary`，不得触发 projection pass。

---

### skill_workorder_summary（技能定义子步骤完成，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "total_items": 4,
  "new_count": 2,
  "reuse_count": 2,
  "items": [ "... 同 progress items ..." ],
  "summary": "共规划 4 个技能：2 个新生成、2 个复用模板默认能力；技能定义已确认，等待确认是否开始匹配技能数据"
}
```

字段说明（在 `skill_workorder_progress` 基础上新增 / 强化）：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 是 | 当前会话工作区真实绝对路径。Projection pass 启动时必须依赖该字段。 |
| `template_slug` | 是 | 当前模板 slug，用于下游链路一致性与日志定位。 |
| `total_items` | 是 | 本轮技能定义条目总数。 |
| `new_count` | 否 | 本轮 `generate_new` 技能数量。 |
| `reuse_count` | 否 | 本轮 `reuse_existing` 技能数量。 |
| `summary` | 是 | 对用户可读的技能定义收口说明。 |

约束：`workspace_root` 与 `template_slug` 必须直接来自会话初始化阶段锁定的真实值；缺少任一字段时不得发出 `skill_workorder_summary`，也不得依赖前端从其它 artifact 补齐。

---

### ontology_projection_ready（匹配技能数据确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "skill_names": ["refund-eligibility-check", "order-status-query", "refund-priority-routing", "return-progress-track"],
  "next_step": "等待用户确认开始匹配技能数据"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 是 | 当前会话工作区真实绝对路径；R2 projection pass 依赖该字段 |
| `template_slug` | 是 | 当前模板 slug |
| `pending_skill_count` | 是 | 需要匹配数据的技能数量 |
| `skill_names[]` | 是 | 本轮 skill slug 或名称列表 |
| `next_step` | 是 | 固定描述下一步是等待用户确认匹配技能数据 |

补充约束：用户确认前不得触发 `ontology-projection`，不得发 `ontology_projection_progress`。

---

### skill_generation_ready（技能生成确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "skill_names": ["refund-eligibility-check", "order-status-query", "refund-priority-routing", "return-progress-track"],
  "projected_count": 4,
  "projection_paths": [
    "ontology/projections/refund-eligibility-check/refund.workflow-contract.projection.json"
  ],
  "readiness_status": "ready",
  "open_questions": [],
  "summary": "技能数据已匹配完成，等待确认开始生成技能实现。",
  "next_step": "等待用户确认开始生成技能实现"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 是 | 当前会话工作区真实绝对路径 |
| `template_slug` | 是 | 当前模板 slug |
| `pending_skill_count` | 是 | 当前等待进入技能生成子步骤的技能数量 |
| `skill_names[]` | 是 | 本轮进入技能生成子步骤的技能名称或 slug 列表 |
| `projected_count` | 是 | 可消费 projection 的技能数量 |
| `projection_paths[]` | 是 | 已落盘且可消费的 projection 文件路径 |
| `readiness_status` | 建议 | `ready` 或 `ready_with_confirmation_items`；后者表示已可生成但仍需用户确认生成前业务口径 |
| `open_questions[]` | 否 | projection 中透传的生成前确认项；只能作为口径确认，不得解释为资料不足 |
| `summary` | 建议 | 对用户可读的一句话状态。若有 `open_questions`，必须表达为“技能数据已匹配完成，仍有 N 个生成前确认项；确认口径后可直接生成技能实现。” |
| `next_step` | 是 | 固定描述下一步是等待用户确认开始技能生成 |

补充约束：
- 该确认门由系统层根据最近一次可消费的 `ontology_projection_done` 确定性追加；coach 不得重复 emit，也不得用普通文本充当确认门。
- 这个 artifact 只表示 projection 已完成且可消费，等待用户确认进入“技能生成执行”子步骤；前端应保留“技能定义已确认”和“技能数据已匹配”的子状态，但主 `stage2_skill` 在 `skill-generation` 完成前仍保持进行中。
- 如果可消费 projection 带有 `open_questions`，这个 artifact 仍然必须发出；该状态是 `ready_with_confirmation_items`，不是“业务信息不足”。用户侧只确认具体业务口径，不重跑业务信息准备。
- 发出该 artifact 后，若用户未明确同意，不得提前触发 `skill-generation`，也不得进入阶段 3。
- `skill_generation_ready.data` 必须包含 `projection_paths` 与 `projected_count` 摘要；不得包含 `projection_binding_confirmed`、`projection_result`、`projection_contract_mode` 等执行字段，这些字段只属于用户确认后传给 `skill-generation` 的内部触发 payload。

### skill_projection_binding_ready（可选进度通知，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "projection_paths": [
    "ontology/projections/refund-intake/refund.workflow-contract.projection.json",
    "ontology/projections/refund-eligibility/refund.workflow-contract.projection.json"
  ],
  "projected_count": 2,
  "summary": "已为 2 个技能匹配好数据，正在生成技能实现"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 否 | 当前会话工作区路径，便于下游链路透传 |
| `template_slug` | 否 | 当前模板 slug |
| `projection_paths` | 否 | 已落盘的 projection 文件路径列表，来源为 `ontology_projection_done.data.projection_paths[]` |
| `projected_count` | 否 | 成功生成 projection 的技能数量 |
| `summary` | 建议 | 对用户可读的一句话状态，如"已为 N 个技能匹配好数据，正在生成技能实现" |

补充约束：
- 该 artifact 不是用户确认门，不得包含 `projection_binding_confirmed`、`projection_result` 等字段。
- 只有 `ontology_projection_done.data.projected_count > 0` 且 `projection_paths[]` 非空时才允许发出。
- 发出后不得自动进入 `skill-generation`；必须等待 `skill_generation_ready` 用户确认门通过。
- 若 `projected_count === 0` 或 `projection_paths[]` 为空，不得发出此 artifact。

### Projection Pass 执行轨（ontology-projection，下游）

> 以下 artifact 由下游 `ontology-slice-extraction` 发出，前端运行轨道名为 `ontology-projection`；`emit_artifact.stage` 固定为主流程阶段 `stage2_skill`。

#### ontology_projection_progress（进度更新，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "pending_skill_count": 4,
  "summary": "正在匹配技能数据"
}
```

#### ontology_projection_done（子步骤完成，isTerminal: true）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "projected_count": 3,
  "projection_paths": [
    "ontology/projections/refund-intake/refund.workflow-contract.projection.json",
    "ontology/projections/refund-eligibility/refund.workflow-contract.projection.json",
    "ontology/projections/refund-notification/refund.workflow-contract.projection.json"
  ],
  "skipped_count": 1,
  "skipped_skills": ["refund-escalation"],
  "skip_reasons": {
    "refund-escalation": "no_matching_slice"
  },
  "summary": "已为 3 个技能匹配好数据，可开始生成技能实现"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `workspace_root` | 否 | 当前会话工作区路径，便于下游链路透传 |
| `template_slug` | 否 | 当前模板 slug |
| `pending_skill_count` | progress 时建议填写 | 参与 projection pass 的技能数量 |
| `projected_count` | done 时必填 | 成功生成 projection 的技能数 |
| `projection_paths` | `projected_count > 0` 时必填 | 已落盘的 projection 文件路径；必须是 `ontology/projections/<skill-slug>/...projection.json` |
| `skipped_count` | done 时必填 | 未生成 projection 的技能数 |
| `skipped_skills` | done 时可选 | 未生成 projection 的技能 slug |
| `skip_reasons` | done 时可选 | 未生成原因，key 为 skill slug |
| `summary` | 否 | 对用户可读的一句话状态 |

当 `projected_count > 0` 时，`projection_paths.length` 必须大于 0，且每条路径必须已经写入文件系统；只有 `slice_paths`、自然语言摘要或 `validation: "NOT_RUN"` 不构成可消费结果。当前端无法从 `projection_paths[]` 解析出与 `skill_workorder_summary.items[].name` 一致的 skill slug 时，不会启动技能生成。

---

## 阶段 3：外部（stage3_external）

### external_system_entry_ready（外部系统入口确认门，isTerminal: false）

```json
{
  "context_signature": "skill-generation-done-v1",
  "status": "waiting_confirm",
  "trigger_after": "skill_generation_done",
  "options": ["enter_external_system", "skip_external_system"],
  "message": "技能实现已生成，请确认进入外部系统配置，或跳过外部系统直接进入打包前确认。"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `context_signature` | 是 | 当前技能生成结果签名；同一签名只展示一次 |
| `status` | 是 | 固定为 `waiting_confirm` |
| `trigger_after` | 是 | 固定为 `skill_generation_done` |
| `options[]` | 是 | 固定包含 `enter_external_system` 和 `skip_external_system` |
| `message` | 否 | 面向用户的确认说明 |

补充约束：用户选择进入外部系统配置后，Coach 才允许发出 `external_workorder_progress`。用户选择跳过外部系统时，skip 形态的 `external_workorder_summary` 与 `external_config_committed` 由系统层确定性写入，不交给模型自由生成。

---

### external_workorder_progress（进度更新，isTerminal: false）

```json
{
  "collected_count": 1,
  "external_capabilities": [
    {
      "name": "crm-order-read",
      "display_name": "CRM 订单查询",
      "category": "read",
      "objective": "根据订单号读取订单状态和物流信息",
      "target_system": "CRM 系统",
      "auth_kind": "bearer_token",
      "linked_skills": ["order-status-query"],
      "status": "pending"
    }
  ],
  "notes": "凭据由用户在右侧表单填写"
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `collected_count` | 是 | 当前已整理的外部能力数量 |
| `external_capabilities[]` | 是 | 外部能力清单 |
| `external_capabilities[].name` | 是 | 外部能力 slug |
| `external_capabilities[].display_name` | 是 | 对用户可读的名称 |
| `external_capabilities[].category` | 是 | `read` / `write` / `notify` / `search` / `transform` / `skip` |
| `external_capabilities[].objective` | 是 | 调用目的 |
| `external_capabilities[].target_system` | 是 | 目标系统名称 |
| `external_capabilities[].auth_kind` | 是 | `none` / `oauth2` / `bearer_token` / `api_key` / `basic` |
| `external_capabilities[].linked_skills` | 是 | 关联的 skill name 列表（非空） |
| `external_capabilities[].status` | 是 | `pending` / `ready` |
| `notes` | 否 | 补充说明（不得包含凭据值） |

---

### external_workorder_summary（阶段完成，isTerminal: true）

```json
{
  "total_capabilities": 2,
  "skip": false,
  "external_capabilities": [ "... 同 progress external_capabilities ..." ],
  "summary": "共规划 2 项外部能力接入，凭据配置待表单填写，外部阶段已确认"
}
```

如果用户明确表示不需要外部系统：

```json
{
  "context_signature": "skill-generation-done-v1",
  "total_capabilities": 0,
  "skip": true,
  "submissionMode": "skipped",
  "external_capabilities": [],
  "reason": "user_skipped_external_system",
  "trigger_after": "external_system_entry_ready",
  "summary": "用户明确声明不需要外部系统接入，外部阶段已跳过"
}
```

---

## 通用约束

- **data 中禁止写入凭据值**：token / 密钥 / 密码 / API Key / 连接串一律不得出现在 `data` 字段中
- **凭据形式可以描述**：`auth_kind` 描述鉴权方式（如 `oauth2`、`bearer_token`），不写具体凭据值
- **status 字段**：除确认门 artifact（固定 `waiting_confirm`）和打包/审查相关 artifact（`packaging_progress`、`packaging_testcases_progress`、`packaging_testcases_done`、`review_progress`、`review_report`）外，不允许 `data` 顶层 `status`。条目级 `items[].status` / `external_capabilities[].status` 仍按 `pending` / `ready` 使用；terminal artifact 中条目状态应全部为 `ready`
- **summary 字段**：terminal artifact 必须包含对用户可读的 `summary`，进度 artifact 可选

---

## 阶段 4：生成数字员工（stage4_packaging）

### packaging_testcases_ready（确认门，isTerminal: false）

```json
{
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "next_step": "等待用户确认是否生成评估测试用例"
}
```

### packaging_testcases_progress（进度更新，isTerminal: false）

```json
{
  "status": "generating_testcases",
  "target_path": "testcases/evaluation-test-cases.json",
  "summary": "正在生成评估测试用例"
}
```

> 说明：`packaging_testcases_progress` 的 `status` 为打包相关 artifact 的允许例外，用于前端展示子步骤进度。

### packaging_testcases_done（子步骤完成，isTerminal: true）

```json
{
  "status": "testcases_ready",
  "target_path": "testcases/evaluation-test-cases.json",
  "summary": "评估测试用例已生成并写入工作区"
}
```

### review_readiness（审查确认门，isTerminal: false）

```json
{
  "status": "ready_for_review_decision",
  "workspace_root": "/workspace/refund-agent-20260518103000",
  "template_slug": "refund-agent",
  "summary": "数字员工内容已同步完成，可选择是否先做完整性审查"
}
```

### review_progress（审查进度，isTerminal: false）

```json
{
  "status": "running",
  "target_path": "reports/package-completeness-review.md",
  "summary": "正在检查技能文件、业务资料和外部配置是否齐全"
}
```

### review_report（审查报告，isTerminal: true）

```json
{
  "status": "PASS_WITH_CONCERNS",
  "release_readiness": "beta-ready",
  "score_average": 8.5,
  "p0_blockers": [],
  "p1_warnings": ["skill.metadata_projection_path.missing"],
  "summary": "数字员工包整体结构完整，1 个 P1 警告建议修复但不阻塞继续生成",
  "report_path": "reports/package-completeness-review.md"
}
```

### packaging_progress（打包进度，isTerminal: false）

等待下游时：

```json
{
  "status": "waiting_downstream",
  "pending_downstream_skills": ["skill-generation"],
  "included": ["ontology/", "skills/", "external/", "config/", "manifest.json"]
}
```

真正打包时：

```json
{
  "status": "packing",
  "included": ["ontology/", "skills/", "external/", "config/", "manifest.json"]
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `status` | 是 | 仅允许 `waiting_downstream` / `packing` |
| `pending_downstream_skills[]` | `waiting_downstream` 时必填 | 尚未完成的下游 skill，仅允许 `ontology-slice-extraction` / `skill-generation` |
| `included[]` | 否 | 计划打包包含的目录白名单提示 |

### template_package（文件产物，isTerminal: true）

`template_package` 的 `kind` 必须为 `file`，关键字段 `fileUrl` / `fileName` 位于 artifact 顶层参数，不在 `data` 内。
