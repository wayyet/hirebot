# HireBot

## 项目目录结构

- `src/`：生产源码目录，包含 API、核心领域、仓储与迁移等项目。
- `tests/`：测试工程目录（单元/集成测试），与 `src/` 物理隔离，避免测试代码与业务源码互相干扰。
- `docs/`：方案与需求文档目录（例如雇佣流程改造清单）。

## 测试说明

- 核心单元测试工程：`tests/HireBot.Core.Tests/`
- 运行该测试工程：
  - `dotnet test tests/HireBot.Core.Tests/HireBot.Core.Tests.csproj`

## 雇佣流程改造关键配置

以下配置位于 `src/HireBot.ApiService/appsettings.json`（开发环境可在 `appsettings.Development.json` 覆盖）：

- 模板包上传通道
  - 说明：模板包上传已固定为调用 Kingcrab `/admin/digital-employee/upload`，不再保留 `Legacy` 上传分支。
- `HireBot:ConversationKickoffPrompt`
  - 作用：会话启动后，当系统检测到尚无 assistant 首条消息时，用于触发 AI 首问的提示词。
  - 建议：按业务场景调整为引导式问题，促使用户在会话中逐步补齐模板包内容。
