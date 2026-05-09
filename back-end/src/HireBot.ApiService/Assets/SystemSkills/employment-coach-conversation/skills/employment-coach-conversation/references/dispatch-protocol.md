# 下游调用信号格式与等回传期间的会话行为

## 信号格式

发起调用时，在对话输出中嵌入一段 JSON 约定块。Handoff tool 是清单和状态的主入口；`<dispatch>` 只发调用信号。

```json
<dispatch>{
  "target": "ontology-extraction",
  "handoff_ids": ["m_cs_nonstandard_rules_001"],
  "mode": "incremental",
  "note": "用户表示这批资料先这些"
}</dispatch>
```

字段：

- `target`: 目标下游 skill（`ontology-extraction` / `skill-generation` / `external-config` / `stage_transition`）。
- `handoff_ids`: 本次 dispatch 的 Handoff todo id 列表。
- `mode`（可选）: 阶段相关模式标记（如本体提取的 `incremental` / `full_replace`）。
- `note`（可选）: 给系统 / 下游的简短上下文。

系统层只按 `handoff_ids` / `handoff_id` 传递和回收主键；skill 文档、payload 与 artifact 一律以 Handoff id 为主键，不再传递非 Handoff 主键字段。

系统层调起下游时，应把这些 Handoff todo 的完整结构放入下游输入的 `handoff_todos` 中，并保留当前会话 `session_id`；下游不得只靠 id 重新猜 payload。

## dispatch 前检查

- 先调用 `handoff`，`action = list` 读取本轮候选 Handoff todo。
- 候选范围不是“模型刚想到的几条”，而是当前阶段、当前 target_skill 下所有未 `confirmed` / 未 `dismissed` 的活跃 Handoff todo。
- 只 dispatch `status = ready_to_dispatch` 或需要重发的 `status = dirty` 条目；如果同阶段还有 `drafting` / `dispatched` / `needs_review`，先合流、等待回传或复核，不发新的 dispatch。
- 确认每条 Handoff todo 的 `session_id` 属于当前会话，且 `stage`、`target_skill`、`payload`、`acceptance` 达到对应下游可消化明确度。
- 用户正在表达异议、修改某条 Handoff todo，或处于配置治理反问待确认状态时，不发 dispatch。

## dispatch 后的对话动作

- 在对话里只用一行告诉用户“我让 X 去处理了，处理完会告诉你结果”，不重复念清单。
- 立即调用 `handoff`，`action = transition`，把本次 dispatch 的 Handoff todo 状态改为 `dispatched`，并记录 `dispatch_id`。
- 等待系统传回下游产出 + `user_summary`。
- 收到回传后，把 `user_summary` 用一两句话复述给用户并请确认。
- 用户确认后，调用 `handoff`，`action = transition`，把对应 Handoff todo 状态改为 `confirmed`。

## 何时不发 dispatch

- 当前阶段任一活跃 Handoff todo 仍是 `drafting`，明确度不够；即使另有条目已经 `ready_to_dispatch`，也不能绕过这条草稿。
- 当前阶段任一活跃 Handoff todo 处于 `dispatched` / `needs_review`，需要等回传合流或完成复核。
- 前置阶段仍存在未闭环的活跃 Handoff todo；先完成前置阶段，不创建或 dispatch 后续阶段条目。
- 用户当前正在表达异议或修改某条 Handoff todo。
- 用户处于反问待确认状态（见 [config-file-governance.md](./config-file-governance.md)）。
- 本轮 dispatch 的目标下游仍有同阶段未合流回传，且用户不是在修改已走过阶段。

## dispatch 等回传期间的会话行为

发出 dispatch 后到下游 `user_summary` 回传之间存在一个空窗。这段时间用户可能继续说话，按下表处理，不进入“在等结果”的静默状态：

| 用户在等回传期间的动作 | 处理方式 |
| --- | --- |
| 抛出同阶段的新意图（又一份资料 / 又一条 skill） | 先调用 `handoff` list 判断是否是在补充已有草稿；若是，`patch` 原 Handoff todo 并按明确度转为 `ready_to_dispatch`；确认为全新意图时才 `upsert` 新 Handoff todo（`status = drafting`）。不立刻发新的 dispatch；等当前 dispatch 回传后合并下一批一起发，避免下游撞车 |
| 修改正在 `dispatched` 状态的某条 Handoff todo | 用 `handoff`，`action = patch` 更新 payload，再用 `handoff`，`action = transition` 把状态切到 `dirty`；回传到达后告诉用户“这条你刚改过，我让那边重新走一次”，再发一次 dispatch |
| 想去下一阶段 | 拉回：“这边的结果还没回来，回来咱们一起看一眼，再去下一步会更稳。”用户坚持的话允许，但当前阶段保持 `dispatched` 不强制 confirm |
| 想跳回走过的阶段做修改 | 允许，由系统提供跳转入口；dispatch 等回传不阻塞这种回跳 |
| 触发配置文件治理（soul / identity / agent 修改意图） | 治理路径独立运行，与 dispatch 等待不互斥；按混合反问机制照常处理 |
| 长时间没说话 | 不主动追问“是否还有补充”。等回传到达后再推进 |

## 回传到达时的合流

1. 把 `user_summary` 用一两句话向用户复述，请确认。
2. 如果期间有 `dirty` 的 Handoff todo，告诉用户那条要重新走一次，用 `handoff`，`action = transition` 把状态从 `dirty` 切回 `ready_to_dispatch`。
3. 如果期间用户提了新 Handoff todo，问一句“刚才你提的那几条要一起合进去吗？”，肯定后再发新一轮 dispatch。
4. 没有 `dirty` 也没有新 Handoff todo，则推进到下一阶段引导或解锁判定。

用户在回传前后追问“完了吗”“结果出来了吗”“继续下一步”时，按同一套合流规则处理：

- 先用 `handoff`，`action = list` 查当前阶段所有活跃 Handoff todo。
- 如果相关 todo 仍是 `dispatched` 且当前上下文没有对应 `dispatch_callback`，只能回复“已经发出，结果还没回来”，不要创建后续阶段 Handoff todo。
- 如果已经有对应 `dispatch_callback`，但相关 todo 还不是 `confirmed`，先复述 `user_summary` 请求用户确认；用户说“继续下一步 / 可以 / 确认 / 先这样”时，先把相关 todo transition 到 `confirmed`。
- transition 成功后再次 `list` 核对前置阶段无 `drafting` / `ready_to_dispatch` / `dispatched` / `dirty` / `needs_review`，再创建或 dispatch 后续阶段 Handoff todo。
- 不得把 `dispatch_callback` 的存在、artifact 文件名或下游摘要本身等同于阶段完成；阶段完成以 Handoff todo 的 `confirmed` 状态和阶段完成条件共同判定。

下游回传的主键也统一为 `handoff_ids` 和 `todo_results[].handoff_id`。

```json
<dispatch_callback>{
  "source_dispatch_target": "skill-generation",
  "handoff_ids": ["s_refund_init_001"],
  "user_summary": "已生成退货资格初判技能草案，覆盖触发条件、判断依据和输出格式。",
  "todo_results": [
    {
      "handoff_id": "s_refund_init_001",
      "status": "success",
      "artifacts": [],
      "errors": []
    }
  ],
  "status": "success",
  "errors": []
}</dispatch_callback>
```

`todo_results` 必须覆盖本次 dispatch 的每个 Handoff id。整体 `status` 规则：全部成功为 `success`，成功与失败 / warning 混合为 `partial`，全部失败为 `failed`。

callback 只允许使用 `handoff_ids` / `handoff_id` 表达主键，且不得写入 Handoff payload 或下游 artifact。

## 出口信号

当三个阶段的最低门槛都达成、且 dispatch 后的下游回传都已由用户确认并写成 `confirmed` 时，输出出口信号：

```json
<dispatch>{
  "target": "stage_transition",
  "to": "instance_packaging",
  "note": "三个阶段的必需项均已完成，可进入打包"
}</dispatch>
```

并以一段简短总结向用户复述：

- 资料里抽到的几类本体（用 `user_summary` 已经讲过的话回引）
- 它会做的几件事（skill name 列表）
- 它能调用的外部能力（category + target_system 列表）
- 下一步是打包成实例包

打包本身不在本 skill 范围内。说完出口信号就停。
