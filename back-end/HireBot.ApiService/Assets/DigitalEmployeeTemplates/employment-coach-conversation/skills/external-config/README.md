# external-config 规范包

`external-config` 是 External 阶段的语义与结构规范包。

当前主链路中：

1. `employment-coach-conversation` 负责引导并发出 `external_workorder_summary`，表示外部需求已收口。
2. 右侧卡片负责真实保存或跳过。
3. 系统层在保存成功后发出 `external_config_committed`，并同步 `external/` 到沙箱和最终实例包。

因此，`external-config` 的职责是：

- 约束 External 阶段的收口字段
- 约束提交完成语义
- 约束 `external/` 目录结构
- 约束敏感字段的安全规则

它不负责：

- 对话引导本身
- 真实凭据采集
- 系统保存
- 沙箱落盘
- 打包执行

## 目录结构

- `SKILL.md`：定义 External 阶段语义与完成条件。
- `contracts/artifacts.json`：定义系统提交完成 artifact。
- `references/output-layout.md`：定义 `external/` 目录布局。
- `references/security-and-validation.md`：定义安全与字段校验要求。
- `templates/`：给出 `external/` 目录中文件的示例结构。

## 当前主链路

1. 上游 coach 发出 `external_workorder_summary`。
2. 用户通过右侧卡片保存或跳过外部系统配置。
3. 系统层对敏感字段进行保护并持久化统一状态。
4. 系统层发出 `external_config_committed`。
5. 系统层将同一份状态写入沙箱 `external/`，并在打包时再次生成一致快照。

## 安全原则

- 真实凭据不进入对话消息。
- 真实凭据不写入 `external/*.json` 或 `external/README.md`。
- `external/` 中仅保留加密后的受保护值或安全引用。
