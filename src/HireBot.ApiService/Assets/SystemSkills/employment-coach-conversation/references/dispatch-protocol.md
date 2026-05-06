# 下游调用信号格式与等回传期间的会话行为

## 信号格式

发起调用时，在对话输出中嵌入一段约定块：

```
<dispatch>
target: ontology-extraction
todos: [todo_id_1, todo_id_2]
mode: incremental
note: 用户表示这批资料先这些
</dispatch>
```

字段：
- `target`: 目标下游 skill（`ontology-extraction` / `skill-generation` / `external-config` / `stage_transition`）
- `todos`: 本次 dispatch 的系统 `todo` 工具 id 列表；下游通过这些 id 读取 `notes` 中的结构化 JSON
- `mode`（可选）: 阶段相关模式标记（如本体提取的 `incremental` / `full_replace`）
- `note`（可选）: 给系统/下游的简短上下文

## dispatch 后的对话动作

- 在对话里**只用一行**告诉用户"我让 X 去处理了，处理完会告诉你结果"，不重复念清单
- 立即用 `todo.update` 把本次 dispatch 的 todo `notes.status` 改为 `dispatched`
- 等待系统传回下游产出 + user_summary
- 收到回传后，把 user_summary 用一两句话复述给用户并请确认
- 用户确认后，用 `todo.update` 把对应 todo `notes.status` 改为 `confirmed`，再用 `todo.complete` 标记系统 todo 完成

## 何时不发 dispatch

- 任何 todo 的 `notes.status` 还在 `drafting` 状态（明确度不够）
- 用户当前正在表达异议或修改某条 todo
- 用户处于反问待确认状态（见 [config-file-governance.md](./config-file-governance.md)）

## dispatch 等回传期间的会话行为

发出 dispatch 后到下游 user_summary 回传之间存在一个空窗。这段时间用户可能继续说话，按下表处理，**不要进入"在等结果"的静默状态**：

| 用户在等回传期间的动作 | 处理方式 |
|---|---|
| 抛出同阶段的新意图（又一份资料 / 又一条 skill） | 正常接住，通过 `todo.add` 形成新 todo（`notes.status = drafting`），但**不立刻发新的 dispatch**——等当前 dispatch 回传后合并下一批一起发，避免下游撞车 |
| 修改正在 `dispatched` 状态的某条 todo | 用 `todo.update` 把该 todo 的 `notes.status` 切到 `dirty`；回传到达后告知用户"这条你刚改过，我让那边重新走一次"，再发一次 dispatch |
| 想去下一阶段 | 拉回："这边的结果还没回来，回来咱们一起看一眼，再去下一步会更稳。" 用户坚持的话允许，但当前阶段保持 `dispatched` 不强制 confirm |
| 想跳回走过的阶段做修改 | 允许，由系统提供跳转入口——dispatch 等回传不阻塞这种回跳 |
| 触发配置文件治理（soul / identity / agent 修改意图） | 治理路径独立运行，与 dispatch 等待不互斥；按混合反问机制照常处理 |
| 长时间没说话 | 不要主动追问"是否还有补充"。等回传到达后再推进 |

## 回传到达时的合流

1. 把 user_summary 用一两句话向用户复述，请确认
2. 如果期间有 `dirty` 的 todo，告诉用户那条要重新走一次，用 `todo.update` 把 `notes.status` 从 `dirty` 切回 `ready_to_dispatch`
3. 如果期间用户提了新 todo，问一句"刚才你提的那几条要一起合进去吗？"，肯定后再发新一轮 dispatch
4. 没有 `dirty` 也没有新 todo → 推进到下一阶段引导或解锁判定

## 出口信号

当三个阶段的最低门槛都达成、且 dispatch 后的下游回传都已 confirmed 时，输出出口信号：

```
<dispatch>
target: stage_transition
to: instance_packaging
note: 三个阶段的必需项均已完成，可进入打包
</dispatch>
```

并以一段简短总结向用户复述：
- 资料里抽到的几类本体（用 user_summary 已经讲过的话回引）
- 它会做的几件事（skill name 列表）
- 它能调用的外部能力（category + target_system 列表）
- 下一步是打包成实例包

打包本身不在本 skill 范围内。说完出口信号就停。
