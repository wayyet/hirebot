# 下游调用信号与回传合流

## 发起顺序

发起下游调用时，按这个顺序组织当前轮回复：

1. 面向业务用户的一句自然语言反馈
2. 如需调起下游，再追加一个 `<dispatch>...</dispatch>`

阶段工单本身不要放进回复文本，必须提前通过 `todo` 工具维护完成。

## `<dispatch>` 结构

```xml
<dispatch>
{"target":"ontology_extraction","todoIds":["todo_material_001"],"mode":"incremental","note":"用户确认这批资料先这些"}
</dispatch>
```

字段：

- `target`: 目标下游 skill
- `todoIds`: 本次交接的阶段工单 id 列表
- `mode`: 可选，阶段相关模式
- `note`: 可选，给下游的简短上下文

## 什么时候可以发

- 相关工单已经通过 `todo` 工具写成可执行状态
- 对应 `todo.notes.status` 已达到 `ready_to_dispatch` 或 `dirty`
- 用户当前没有继续修改这批工单

## 什么时候不能发

- 任何相关工单还处于 `drafting`
- 用户仍在修改、反对或撤销这批工单
- 正在等待配置治理确认

## 回传期间的会话行为

- 告诉用户“我让那边处理了，处理完我会告诉你结果”
- 用户继续补充同阶段内容时，继续维护新的 todo，但不要立刻并发新的 dispatch
- 用户修改已 dispatch 的工单时，把该工单更新成 `dirty`
- 收到回传后，用 1-2 句复述 `user_summary`
- 如果需要重新派发，先更新 todo 状态，再发下一轮 `<dispatch>`

## 出口信号

三个阶段都完成后，允许发送：

```xml
<dispatch>
{"target":"stage_transition","todoIds":[],"note":"三个阶段的必需项均已完成，可进入打包"}
</dispatch>
```
