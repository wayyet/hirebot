# external_config 规范包

`external_config` 是雇佣教练流程阶段三的下游执行型 skill。它接收 `employment-coach-conversation` 产出的 external handoff todo，把“要接哪个外部系统、做什么能力、需要哪些字段和认证形式”落成沙箱内 `external/` 目录下的配置草案。

它不做对话引导，不收集真实凭据，不修改 handoff todo 状态，也不直接调用外部系统。

## 目录结构

- `SKILL.md`：skill 主入口，定义何时触发、输入合约、输出目录和安全红线。
- `README.md`：当前规范包总览。
- `references/`
  - `handoff-contract.md`：雇佣教练 handoff todo 到 external 配置的字段映射。
  - `output-layout.md`：`external/` 目录与 JSON 结构约定。
  - `security-and-validation.md`：凭据安全、校验规则和失败策略。
- `templates/`
  - `capability.template.json`：单条 external capability 配置模板。
  - `index.template.json`：`external-config.index.json` 模板。

## 最小运行链路

1. 主 skill 拦截 `<dispatch target=external_config>`。
2. 系统层把本次 `todos` 对应的 external handoff todo 传给 `external_config`。
3. `external_config` 校验字段、扫描疑似凭据、生成 capability 与 system 配置草案。
4. 如系统层提供安全表单凭据上下文，只绑定 `credentialSlot` / `secretRef`，不把真实值写入普通产物。
5. 产物写入沙箱 `external/`。
6. `external_config` 返回 `dispatch_callback`，包含 `user_summary`、artifact 相对路径和逐 todo 的 `todo_results`。
7. `employment-coach-conversation` 向用户复述摘要，用户确认后才把 todo 标记为 `confirmed`。

## MVP 完成标准

- 支持 `kind: normal` 与 `kind: skip`。
- 支持 `read/write/notify/search/transform` 五类外部能力。
- 生成 `external/capabilities/<todo-id>.json`。
- 生成或更新 `external/external-config.index.json`。
- 生成或更新 `external/systems/<system-slug>.json`。
- `kind: skip` 标准写入 `external/capabilities/<todo-id>.json` 并登记到 index 的 `skips[]`。
- 对所有真实凭据值执行拒收或脱敏阻断。
- 返回带 `todo_results` 的标准 `dispatch_callback`。

## 与其他 skill 的边界

| skill | 职责 | 是否写 `external/` |
| --- | --- | --- |
| `employment-coach-conversation` | 引导用户，生成 external handoff todo，发 dispatch | 否 |
| `external_config` | 把 external handoff todo 落成配置草案 | 是 |
| `diagnosis` | 只读检查 external 阶段是否完整、安全 | 否 |
| 主 skill / 系统层 | 调度、传递 callback、维护流程状态 | 视系统实现 |

## 安全原则

`external_config` 可以知道“需要 API Key / OAuth / 应用凭据”，但不能知道或保存真实密钥。真实凭据应由系统层通过安全表单和安全存储通道处理，产物里只保留 `secretRef`、`credentialSlot` 或等价引用。

如果当前运行环境还没有安全存储绑定能力，`external_config` 应保留待绑定槽位并返回 `partial`，由系统层或后续流程补齐，不要把真实凭据降级写入 JSON 或 Markdown。
