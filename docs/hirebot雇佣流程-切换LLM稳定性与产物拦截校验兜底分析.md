# hirebot 雇佣流程：切换 LLM 稳定性 & 越界产物拦截 / 完整性校验 / 兜底策略分析

> 分析对象：`hirebot` 后端（`develop` 分支，.NET 10）
> 分析日期：2026-06-29
> 适用提交基线：`3618e6d`（最近一次相关重构为 `bf4b657 清理雇佣代码`，2026-06-04）

---

## 0. 一句话结论（TL;DR）

1. **切换 LLM 是否会导致“没输出测试用例”？** 在**当前代码**下基本不会。因为测试用例已经**不再由 LLM 实时生成**，而是在评估阶段从「模板包 / 产物包 / 模板定义」里读取**预置文件**，读不到还有默认连通性用例托底。真正受 LLM 影响的是「对话采集质量」和「产物包内容质量」，而不是「有没有测试用例」。
2. **旧链路已下线**：原本「解析 AI 回复推进流程 + LLM 实时生成测试用例 + 越界产物拦截」的逻辑，已在 `bf4b657` 重构中整体移除，相关函数（`ParseAssistantReply`、`IsAllowedArtifactPath`、`ContainsSensitiveValue`、`TryExtractPackagingTestCasesBundle`）当前**只剩定义和单元测试，无生产调用方**。
3. **三类机制的落点**：集中在 `Services/Hiring/`（`HiringWorkflowSupport`、`PackagingTestCasesJsonValidator`、`Artifacts/`）和 `Services/Evaluation/`；但其中相当一部分已随旧链路下线，**当前真正生效**的拦截/校验/兜底主要在 `HiringArtifactPackageService`（打包服务）和 `EvaluationService`（评估服务）。

---

## 1. 背景：测试用例到底从哪来

理解问题 1 的前提，是先分清「测试用例」在两套设计里的来源差异。

### 1.1 旧设计（已下线）——LLM 实时生成

```
hirebot 派发任务  ──HTTP──▶  kingcrab 的 Agent（跑 LLM）
                                   │
                                   ▼
                         LLM 输出带结构化标签的文本
                  <dispatch_callback>{...evaluation_test_cases_json...}</dispatch_callback>
                                   │
                                   ▼
   hirebot 用 ParseAssistantReply 正则抠标签 → 反序列化 JSON
                                   │
                                   ▼
   PackagingTestCasesJsonValidator 严格校验 → 入库 / 兜底
```

这套强依赖「LLM 老实按格式吐结构化标签」。**已在 `bf4b657` 删除主流程调用方。**

### 1.2 当前设计（生效中）——模板预置 + 评估期读取

```
评估阶段 EvaluationService.LoadTestcaseSourcesAsync 按三级顺序“取现成的”：

  ① 最终雇佣产物包里的 testcases/*.json        （LoadTestcaseSourcesFromArtifactPackageAsync）
        │ 取不到
        ▼
  ② 上传的模板包 testcase 文件
        │ 取不到
        ▼
  ③ 模板定义里的 testcase 文件                   （LoadTestcaseSourcesFromTemplateDefinitionAsync）
        │ 都取不到
        ▼
  ④ 默认连通性用例 default_connectivity_testcases.json（数据兜底，2 条用例）
```

**关键差异**：当前测试用例来源是「读预置文件」，不是「等 LLM 当场生成」，因此对 LLM 的模型选择**不敏感**。

---

## 2. 问题 1：切换 LLM 会不会导致输出不稳定？

### 2.1 旧链路（已下线）：会，且脆弱点明确

| 脆弱点 | 说明 | 代码位置 |
|---|---|---|
| 强格式依赖 | 靠正则抠 `<dispatch>`/`<dispatch_callback>`/`<diagnostic_report>` 标签，内部还得是 snake_case JSON。换 LLM 不守格式 → 抠不到 | `HiringWorkflowSupport.ParseAssistantReply`（`HiringWorkflowSupport.cs:17`）|
| 解析失败静默吞 | JSON 反序列化失败直接 `return null`，不报错、不重试 → **流程“成功”但测试用例悄悄变空** | `TryDeserialize`（`HiringWorkflowSupport.cs:153`）|
| 结构契约严格 | `test_cases` 必须非空数组；每条须有 `test_case_id`、`scenario_name`、`input.user_request`；字段别名仅认少数几个 | `TryValidateTestCasesJson`（`PackagingTestCasesJsonValidator.cs:74`）|

> 旧链路并非裸奔：它带多级兜底（严格 → legacy 只取 merged → 空数组占位），最坏结果通常是「降级成空/占位用例」而非「整个流程崩」。这部分兜底代码仍保留，但已无生产调用方。

### 2.2 当前链路（生效）：基本不会“没有测试用例”

- 测试用例改为「读预置文件」，由 `EvaluationService.LoadTestcaseSourcesAsync` 三级 fallback + 默认用例托底，**与 LLM 模型选择解耦**。
- 仍受 LLM 影响的是：雇佣对话的「信息采集质量」、产物包内容的「优化质量」——但这不会表现为「没输出测试用例」。
- **注意**：hirebot 侧未见控制 LLM `temperature`/采样参数的代码（推理在 kingcrab 侧），所以「对话生成」层面的稳定性取决于 kingcrab 的模型与参数配置。

### 2.3 结论

> 就当前 `develop` 代码而言，**切换 LLM 不会导致“没有测试用例”**；该风险属于已下线的旧链路。若未来重新接回「LLM 实时生成测试用例」，则 2.1 的三个脆弱点会重新成立，需配套「格式校验失败显式告警 + 重试/降级」才稳。

---

## 3. 问题 2：越界产物拦截 / 完整性校验 / 兜底策略在哪个模块

> ✅ = 当前生产链路在用；⚠️ = 已随旧链路下线，仅剩定义/单测。

### 3.1 越界产物拦截

| 手段 | 实现 | 现状 |
|---|---|---|
| 路径白名单（仅放行 `ontology/` `skills/` `external/` `testcases/` `config/`）| `HiringWorkflowSupport.IsAllowedArtifactPath`（`HiringWorkflowSupport.cs:97`）| ⚠️ 无调用方 |
| 敏感信息拦截（token/api_key/secret/password/connection_string 正则）| `HiringWorkflowSupport.ContainsSensitiveValue`（`HiringWorkflowSupport.cs:87`）| ⚠️ 无调用方 |
| 路径穿越拦截（拦 `.` `..` `:` `\0`）| `HiringArtifactPackageService.TryNormalizeArtifactPath`（`HiringArtifactPackageService.cs:517`）| ✅ 打包/下载在用 |
| 来源目录过滤（只采 `testcases/`）| `EvaluationService.IsTemplateTestcaseEntry`（`EvaluationService.SessionAndTestcases.cs:502`）| ✅ 评估阶段在用 |

### 3.2 完整性校验

| 手段 | 实现 | 现状 |
|---|---|---|
| 包级 SHA256（打包入库即算、读取校验）| `HiringArtifactPackageService.PersistPackageAsync`（`HiringArtifactPackageService.cs:256`）| ✅ 在用 |
| 内容哈希（sourceContentHash / enrichedContentHash）| `PlaceholderArtifactSerializer.ComputeContentHash`（`PlaceholderArtifactSerializer.cs:285`）| ✅ 序列化产物在用 |
| 回调产物哈希 | `HiringWorkflowSupport.ComputeSha256`（`HiringWorkflowSupport.cs:82`）| ⚠️ 调用面极窄 |
| 测试用例结构校验 | `PackagingTestCasesJsonValidator.TryValidate*`（`PackagingTestCasesJsonValidator.cs:74`）| ⚠️ 仅单测 |

### 3.3 兜底策略

| 手段 | 实现 | 现状 |
|---|---|---|
| 测试用例三级降级（严格 → legacy → 空数组占位）| `PackagingTestCasesJsonValidator.TryExtractPackagingTestCasesBundle`（`PackagingTestCasesJsonValidator.cs:186`）| ⚠️ 仅单测 |
| 评估期取用例三级 fallback（产物包 → 上传模板包 → 模板定义）| `EvaluationService.LoadTestcaseSourcesAsync`（`EvaluationService.SessionAndTestcases.cs:241`）| ✅ **当前主力兜底** |
| 默认连通性用例（“你好” + “你能帮我做什么”）| `_defaults/testcases/default_connectivity_testcases.json` | ✅ 数据兜底 |
| 取包三级回退（FinalPackageId → 按时间最新 → 反向索引）| `HiringArtifactPackageService.GetLatestPackageByEmployeeIdAsync`（`HiringArtifactPackageService.cs:98`）| ✅ 在用 |
| 阶段状态容错（not_asked/waiting_confirm/generating/generated/skipped/failed）| `PackagingTestCasesGenerationStatuses`（`PackagingTestCasesGenerationStatuses.cs`）+ `HiringStageService.UpdateStageProgressAsync`（`HiringStageService.cs:37`）| 状态机在用 |

---

## 4. 关键发现：`bf4b657 清理雇佣代码` 做了什么

该提交（2026-06-04）删除约 8238 行，移除了下列承载旧链路的 partial 文件，使上述 ⚠️ 标记的函数失去生产调用方：

- `EmployeeHiringService.PackagingTestCases.cs`（853 行）
- `EmployeeHiringService.ConversationOrchestration.cs`（306 行）
- `EmployeeHiringService.DispatchAndCredentials.cs`（497 行）
- `EmployeeHiringService.DataHelpers.cs`（1091 行）
- 以及对应单测 `ImportPackageTestCasesTests.cs` / `PackagingTestCasesFromHistoryTests.cs` / `PackagingTestCasesTests.cs`

被删代码中包含对 `ParseAssistantReply`、`TryExtractPackagingTestCasesBundle`、`IsAllowedArtifactPath`、`ContainsSensitiveValue` 的调用。重构保留了这些底层工具函数本身（及 `PackagingTestCasesJsonValidatorTests.cs`），但主流程改为「模板预置 + 评估期 fallback」。

> 形象比喻：这些校验/拦截函数现在像**拆了电线的开关面板**——开关还在墙上、单测还在按，但通往主流程的电线已被抽走。

---

## 5. 核心文件索引

| 主题 | 文件 |
|---|---|
| AI 回复解析 / 产物拦截 / 哈希（旧链路工具，多数已下线）| `back-end/HireBot.Core/Services/Hiring/HiringWorkflowSupport.cs` |
| 测试用例 JSON 校验 / 多级提取兜底（仅单测）| `back-end/HireBot.Core/Services/Hiring/PackagingTestCasesJsonValidator.cs` |
| 测试用例生成状态枚举 | `back-end/HireBot.Core/Services/Hiring/PackagingTestCasesGenerationStatuses.cs` |
| 阶段状态机（落库 PackagingTestCasesStatus）| `back-end/HireBot.Core/Services/Hiring/HiringStageService.cs` |
| 产物打包：SHA256 + 路径穿越拦截 + 取包兜底 | `back-end/HireBot.Core/Services/Hiring/Artifacts/HiringArtifactPackageService.cs` |
| 占位产物序列化 + 内容哈希 | `back-end/HireBot.Core/Services/Hiring/Artifacts/PlaceholderArtifactSerializer.cs` |
| 评估期测试用例来源三级 fallback（当前主力）| `back-end/HireBot.Core/Services/Evaluation/EvaluationService.SessionAndTestcases.cs` |
| 雇佣主编排 | `back-end/HireBot.Core/Services/Hiring/EmployeeHiringService.cs` |
| MCP 工具（kingcrab 反向调用读上传资料）| `back-end/HireBot.ApiService/McpTools/HiringTodoMcpTools.cs` |
| 默认连通性用例（数据兜底）| `.../Assets/DigitalEmployeeTemplates/_defaults/testcases/default_connectivity_testcases.json` |
