# HireBot Standard Skill Template

当你创建的是“标准业务 skill”而不是 projection consumer 时，从这个模板开始。

如果目标 skill 要直接消费 `ontology-extraction` projection contract，不要用这个模板起步；改用 `../ontology-extraction/templates/CONSUMER_SKILL_SCAFFOLD.md`。

```md
---
name: <skill-slug>
description: <一句话写清它做什么，以及用户在什么场景下会触发它>
metadata: {"openclaw":{"emoji":"<emoji>"}}
---

# <skill-title>

## 核心职责

你负责 <主要职责>。

## 触发信号

当用户提到以下意图或表达时使用本 skill：

- <触发短语 1>
- <触发短语 2>
- <触发短语 3>

## 处理流程

1. 先确认目标输出、输入边界和缺失信息。
2. 读取当前任务真正依赖的文件、事实或上游产物。
3. 只生成本 skill 负责的交付物。
4. 输出前检查边界、术语一致性和失败回退。

## 边界

- 不做 <不负责事项 1>
- 不做 <不负责事项 2>

## 失败与回退

- 当 <关键信息缺失> 时，说明缺口并要求补充，不要猜测。
- 当请求超出边界时，明确拒绝或转交到正确 skill。

## References

- `<相对引用 1>`
- `<相对引用 2>`
```

## 使用规则

- `name` 必须与目录名一致，使用小写字母、数字、短横线。
- `description` 必须可发现，至少包含用户可能说的话或任务场景。
- 不要把模板里的占位符留在最终文件。
- 如果某个长说明会超过正文的必要长度，把它拆到 `references/`。
