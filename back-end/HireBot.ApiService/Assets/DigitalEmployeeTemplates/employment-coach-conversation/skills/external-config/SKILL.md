---
name: external-config
description: 定义 External 阶段的语义边界、完成条件和 external/ 目录结构约束。该 skill 负责外部系统接入需求的收口规范与提交完成语义，不负责真实凭据收集、系统保存、密钥加密或 external/ 目录落盘。
compatibility: HireBot employment-coach-conversation v1.0
license: Proprietary. NCrew employment-coach internal flow.
metadata:
  openclaw:
    emoji: "🔌"
  category: orchestration
  autonomy: 40
  trigger: hiring-session-external, external-stage-active
  input: external-capability-workorder, external-config-commit-state
  output: external-stage-contract, emit-artifact
---

# External Config

`external-config` 是 External 阶段的语义规范包。

它负责 4 件事：

1. 定义外部系统接入需求需要收集哪些信息。
2. 定义 External 阶段什么情况下算“需求已收口”。
3. 定义右侧卡片保存或跳过后，什么情况下算“系统提交已完成”。
4. 定义最终 `external/` 目录的结构、字段语义和安全边界。

它不负责 4 件事：

1. 不直接向用户收集真实密钥、Token、密码或连接串。
2. 不直接保存外部系统配置。
3. 不直接加密敏感字段。
4. 不直接把 `external/` 目录写入沙箱或实例包。

这些动作统一由系统层负责。

## 当前阶段语义

External 阶段有两个不同信号：

- `external_workorder_summary`
  - 由 `employment-coach-conversation` 发出。
  - 表示外部系统需求已经收口清楚。
  - 只代表“该配什么”已经明确。

- `external_config_committed`
  - 由系统层在右侧卡片保存或跳过成功后发出。
  - 表示配置结果已经持久化成功，并已进入共享/打包链路。
  - 这是 External 阶段真正完成的提交信号。

因此：

- `external_workorder_summary` 不是最终提交完成信号。
- External 阶段是否完成，应以 `external_config_committed` 为准。

## 最小输入语义

上游在进入 External 阶段时，需要把每条外部能力收口成结构化需求。每条能力至少应包含：

- `category`
- `target_system`
- `objective`
- `linked_skills`
- `integration_methods`
- `auth_kind`

可接受的 `category` 只有：

- `read`
- `write`
- `notify`
- `search`
- `transform`

如果用户明确表示当前无需对接任何外部系统，则允许进入 skip 分支，并在 `external_workorder_summary.data` 中给出明确 skip 原因。

## 提交完成语义

右侧卡片提交成功后，系统层必须发出 `external_config_committed`，并在 `data` 中至少包含：

```json
{
  "submissionMode": "configured",
  "updatedAtUtc": "2026-05-28T10:00:00Z",
  "cliTools": [],
  "mcpServer": null
}
```

或：

```json
{
  "submissionMode": "skipped",
  "updatedAtUtc": "2026-05-28T10:00:00Z",
  "cliTools": [],
  "mcpServer": null
}
```

规则如下：

- `submissionMode = configured` 表示用户已保存有效外部配置。
- `submissionMode = skipped` 表示用户明确跳过外部系统接入。
- 只有系统层持久化成功后，才允许发出 `external_config_committed`。
- 任何“仅对话确认、尚未保存”的状态，都不能视为 External 阶段完成。

## external/ 目录唯一来源

`external/` 目录只能由系统层生成。

生成时机包括：

1. 用户在右侧卡片点击保存或跳过，并且后端持久化成功后，同步到沙箱工作区。
2. 最终实例包生成时，从同一份受保护状态再次生成 `external/` 快照。

禁止以下做法：

- 由对话层直接写 `external/`
- 由本 skill 直接写 `external/`
- 由多套来源分别写出不同版本的 `external/`

## 安全红线

- 不在对话中收集真实密钥、Token、密码、连接串。
- 不在 artifact、README、JSON 模板或摘要中写明文凭据。
- 产物里只允许出现加密后的受保护值、`secretRef`、`credentialSlot` 或等价安全引用。
- 如果安全存储尚未绑定完成，应保留待绑定槽位并标记为 `pending` 或等价状态，而不是降级写入明文。

## 参考文件

- [README.md](README.md)
- [contracts/artifacts.json](contracts/artifacts.json)
- [references/output-layout.md](references/output-layout.md)
- [references/security-and-validation.md](references/security-and-validation.md)
- [templates/index.template.json](templates/index.template.json)
