# 雇佣流程切换 LLM 稳定性与产物防护分析

> 分析对象：`hirebot` 项目的「雇佣流程」（employment-coach-conversation 数字员工装配流程）
> 关注点：① 切换底层 LLM 是否导致输出不稳定（某个 skill 未执行 / 执行结果不符合预期）；② 项目是否有「越界产物拦截 / 完整性校验 / 兜底策略」，分别在哪个模块实现。
> 示例 skill：`back-end/HireBot.ApiService/Assets/DigitalEmployeeTemplates/employment-coach-conversation/skills/packaging-test-cases`

---

## 0. 先看懂整体架构（三层协作）

要回答「换 LLM 会不会乱」，必须先搞清楚一件事：**skill 到底是谁在「执行」的。**

雇佣流程是一个三层协作的系统，数据从上到下流动：

```
┌─────────────────────────────────────────────────────────────┐
│ ① 前端 React（front-end/src/features/hiring）                 │
│    确定性"编排骨架"：关键词确认门 + 状态机 route +            │
│    向 LLM 注入强约束提示词 + 消费 artifact 事件驱动阶段胶囊    │
└───────────────▲───────────────────────────┬──────────────────┘
                │ 用户消息 / 确认            │ artifact 事件流
┌───────────────┴───────────────────────────▼──────────────────┐
│ ② hirebot 后端（HireBot.ApiService + HireBot.Core）           │
│    业务编排层：会话/资料/模板管理、沙箱 provision、            │
│    产物落盘/打包/校验工具、阶段完成判定                        │
└───────────────▲───────────────────────────┬──────────────────┘
                │ HTTP / WebSocket / MCP     │ 注入环境变量(含LLM配置)
┌───────────────┴───────────────────────────▼──────────────────┐
│ ③ kingcrab 沙箱（OpenClaw.Gateway，独立容器）                 │
│    Agent 执行平台：真正加载 SKILL.md → 调用 LLM →             │
│    在 /workspace 写文件 → emit artifact 回流                   │
│    ★ LLM 就在这一层运行，skill 的"执行"=LLM 读提示词干活      │
└───────────────────────────────────────────────────────────────┘
```

**最关键的一句话**：skill（包括示例的 `packaging-test-cases`）的「执行」本质是 **LLM 在沙箱里按 `SKILL.md`（自然语言提示词）自主完成的工作**，不是一段确定性的 C# / TS 代码。`SKILL.md` 头部的 `autonomy: 75` 说明它是「高自主度的提示词驱动 Agent」。

这就决定了：**换 LLM ≈ 换一个"员工"去读同一份工作手册（SKILL.md）干活**。手册没变，但员工的理解力、执行力会变。

---

## 1. LLM 是怎么配置和切换的

LLM 不是写死在代码里的，而是通过配置注入到沙箱容器。

实现位置：[back-end/HireBot.Core/Services/Sandbox/SandboxProvisioningSettings.cs](../back-end/HireBot.Core/Services/Sandbox/SandboxProvisioningSettings.cs)

- `FromConfiguration()`（第 33 行）从配置读取一组 LLM 参数：
  `OpenSandbox:KingCrab:LlmProvider / LlmModel / LlmEndpoint / LlmApiKey / LlmTemperature / LlmEnableThinking`（第 129-134 行）。
- `BuildRuntimeEnv()`（第 164 行）把这些参数转成环境变量，注入沙箱容器内的 `OpenClaw.Gateway`：
  `MODEL_PROVIDER_KEY / MODEL_PROVIDER_MODEL / MODEL_PROVIDER_ENDPOINT` 以及 `OpenClaw__Llm__*`（第 204-213 行）。
- `ValidateGatewayModelProviderConfiguration()`（第 225 行）做配置完整性校验：`Model / Endpoint / ApiKey` 必须三者同时配置，否则启动即抛异常。

**结论**：所谓「切换 LLM」，就是改这几个配置项。它是**全局单一模型配置**——整个雇佣流程的所有 skill 共用同一个模型，没有「按 skill 指定不同模型」的能力。

---

## 2. 问题①：切换 LLM 会不会导致输出不稳定？

### 2.1 结论先行

- **流程"骨架"不会垮**：阶段怎么走、什么时候进下一步、确认词怎么识别——这些是前端**确定性代码**，不依赖 LLM 的判断。
- **产物"质量与一次成功率"会受影响**：skill 是否被正确触发、是否写了文件、是否发了正确的 artifact、JSON 字段是否合规——这些**强依赖 LLM 的指令遵循能力**，换弱模型会明显变差。
- 所以：**换 LLM 不会让系统"乱套"，但会让"卡住 / 走降级 / 需要重试"的概率上升。**

### 2.2 为什么会不稳定——具体故障模式

skill 的执行依赖 LLM 读懂并严格遵守 `SKILL.md`。能力不同的模型，容易在以下环节出问题（以 `packaging-test-cases` 为例）：

| 故障模式 | 说明 | 触发后果 |
|---|---|---|
| **门禁误判** | `SKILL.md` 要求只在收到后端 `<invoke_packaging_testcases>` 或 `trigger_after==external_config_committed` 时才执行；弱模型可能用户随口说"生成测试"就抢跑 | 越权执行 / 流程错乱 |
| **skill 没切换 / 切错** | 该 switch 到 `packaging-test-cases` 却继续留在 coach；或乱切到别的 skill | 该做的事没做 |
| **漏发 artifact** | 没 emit `packaging_testcases_progress` / `..._done` | **前端状态机卡死**（见 2.3） |
| **artifact 结构不合规** | 字段缺失、没用 snake_case、`test_case_id`/`workspace_root` 漏了 | 后端校验失败 → 走降级或拒收 |
| **只说不做** | 只在回复里"口头描述"测试用例，不调用写文件工具（`SKILL.md` 反复强调禁止 narrative-only） | 工作区没有实际文件 |
| **越界产物** | 写到白名单外的目录、打包 `/workspace`、把 coach 系统包混进实例包、编造用例 | 被拦截 / 报错 |
| **slug 漂移** | 把 skill slug 同义改写（如"打包测试"），导致 `ontology/projections/<slug>/` 路径错配 | projection 与 skill 对不上 |

### 2.3 最典型的"卡住"机制（必须理解）

前端用 artifact 类型驱动整个阶段状态机：
[front-end/src/features/hiring/pages/hiringArtifactState.ts](../front-end/src/features/hiring/pages/hiringArtifactState.ts) 第 399-416 行 `DOWNSTREAM_ARTIFACT_TRACKS`：

```
xxx_ready    → waiting_confirm （等用户确认）
xxx_progress → running        （进行中）
xxx_done     → completed       （完成，可进下一步）
```

**含义**：阶段能否推进，取决于 LLM 是否吐出了正确类型的 artifact。
- 如果 LLM 漏发 `xxx_done`，前端就一直停在 `running`，**流程卡住**；
- 如果发了错误的 `artifactType` 或字段，前端解析失败，状态不更新。

这就是"换 LLM 后某个 skill 看起来没执行"的最常见根因——不是流程逻辑错了，而是**弱模型没按 artifact 协议吐出正确信号**。

### 2.4 系统如何把"不稳定"收敛——三道防线

系统并不是"全靠 LLM 自由发挥"，而是用三层手段把不确定性压下来：

1. **前端确定性编排骨架**（不让 LLM 决定流程走向）
   [front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts)
   - 用户确认用**关键词/正则匹配**识别，而非让 LLM 判断：`isPackagingTestCasesApprovalMessage`（第 781 行）、`isSkillGenerationApprovalMessage`（第 629 行）等。
   - 用**状态机 route** 决定下一步：`resolvePackagingRequestRoute`（第 1065 行）、`resolveSkillStageApprovalRoute`（第 659 行）。

2. **注入式强约束提示词**（给任意 LLM 一份"强制 checklist"）
   `buildDownstreamPrompt`（第 337 行）针对每个下游 skill 注入一段写死的指令，明确：
   - 必须切到哪个 skill、必须发哪些 `required_artifacts`；
   - 必须用写文件工具、必须 `read_file` 回读校验（如 projection 第 384-388 行）；
   - 失败如何降级（如 `slices_not_ready`、protocol failure fallback）。
   等于把"该做什么/禁止什么/失败怎么办"从 LLM 的自由发挥变成硬性约束。

3. **后端契约校验 + 沙箱沙盒边界**（把不合规产物拦在落盘/打包之前）
   详见第 3 节。

### 2.5 给"换模型"的实操建议

- 换模型后，优先回归关键 skill 的产物契约。仓库已有现成测试可复用：
  - [back-end/HireBot.Core.Tests/PackagingTestCasesJsonValidatorTests.cs](../back-end/HireBot.Core.Tests/PackagingTestCasesJsonValidatorTests.cs)
  - [back-end/HireBot.Core.Tests/FinalPackageZipAcceptanceTests.cs](../back-end/HireBot.Core.Tests/FinalPackageZipAcceptanceTests.cs)
- 重点观察新模型的两项能力：**artifact 协议遵循度**（是否稳定吐 `progress`/`done`）和 **JSON 结构遵循度**（字段/命名是否合规）。
- 优先选「指令遵循 + 结构化输出」强的模型；`LlmTemperature`（默认 0.7）可适当调低以提高稳定性。
- 注意 `LlmEnableThinking` 等开关对不同模型行为影响较大，换模型时需一并验证。

---

## 3. 问题②：越界拦截 / 完整性校验 / 兜底策略，分别在哪个模块？

项目**确实有**这三类防护，且是"分层冗余"的——前端、后端、沙箱、提示词四个层面都有布点。

### 3.1 越界产物拦截

| 实现点 | 文件:位置 | 作用 |
|---|---|---|
| 产物路径白名单 | [HiringWorkflowSupport.cs](../back-end/HireBot.Core/Services/Hiring/HiringWorkflowSupport.cs) `IsAllowedArtifactPath`（第 97 行） | 只允许 `ontology/ skills/ external/ testcases/ config/` 五个前缀，其余路径拒收 |
| 敏感值拦截 | [HiringWorkflowSupport.cs](../back-end/HireBot.Core/Services/Hiring/HiringWorkflowSupport.cs) `ContainsSensitiveValue`（第 87 行） | 正则匹配 token/api_key/secret/password/connection_string，防止密钥写入产物 |
| 沙箱读写根 / 网络出口 | [SandboxProvisioningSettings.cs](../back-end/HireBot.Core/Services/Sandbox/SandboxProvisioningSettings.cs) `BuildRuntimeEnv`（第 192-199 行）/ `BuildNetworkPolicy`（第 246 行） | `WorkspaceRoot=/workspace`、读写根限制、网络 egress 白名单（`NetworkEgressAllowHosts`） |
| 打包根越界拦截（提示词级） | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildPackageZipInstructionLines`（第 471 行） | 强制：绝不打包 `/workspace`、`employee_package_root` 不得等于 `/workspace`、不得含 `skills/employment-coach-conversation/`（即不得把 coach 系统包打进去） |
| 打包 skill 白名单同步 | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildManifestSyncInstructionLines`（第 485 行） | manifest.skills 只保留「当前生成的业务 skill + 内置模板 skill」，移除 stale 条目 |

### 3.2 完整性校验

| 实现点 | 文件:位置 | 作用 |
|---|---|---|
| 测试用例 JSON 结构校验 | [PackagingTestCasesJsonValidator.cs](../back-end/HireBot.Core/Services/Hiring/PackagingTestCasesJsonValidator.cs) `TryValidateTestCasesJson`（第 74 行）、`TryValidateSourcesIndexJson`（第 257 行）、`TryValidateDerivedTestCasesJson`（第 362 行） | 校验 `test_cases` 非空数组、每条必含 `test_case_id`/`scenario_name`/`input.user_request`；index、各来源子文件分别校验 |
| 转录过滤 | 同上 `PrepareHistoryTranscript`（第 22 行） | 限制 40 轮 / 12000 字符、过滤过短消息与"打包意图"消息，保证输入干净 |
| 产物哈希校验 | [HiringWorkflowSupport.cs](../back-end/HireBot.Core/Services/Hiring/HiringWorkflowSupport.cs) `ComputeSha256`（第 82 行） | 对产物内容计算 SHA256，用于完整性比对 |
| manifest 补齐 / 去重 | [FinalPackageManifestUpdater.cs](../back-end/HireBot.Core/Services/Hiring/FinalPackageManifestUpdater.cs) `AppendLinkedSkills`（第 14 行） | 打包时把关联 skill 写入 manifest、去重、slug 清洗（`SanitizeSkillSlug` 第 159 行） |
| 阶段必填字段判定 | [HiringStageCompletionEvaluator.cs](../back-end/HireBot.Core/Services/Hiring/HiringStageCompletionEvaluator.cs) `Evaluate`（第 8 行） | 每阶段 `RequiredFields` 全部非空才 `ReadyForNextStage=true`，否则列出 `BlockingFields` |
| manifest 回读校验（提示词级） | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildManifestSyncInstructionLines`（第 485-494 行） | 写完 manifest 必须 read-back 验证 entry_skill 可解析、每个 skill 已声明，失败则不许进审查/打包 |
| projection 写后回读校验（提示词级） | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildDownstreamPrompt`（第 384-388 行） | 每个 projection 文件写完必须 `read_file` 回读确认 JSON 完整字段，才计入 projected |
| ontology 抽取成功形态判定 | [hiringArtifactState.ts](../front-end/src/features/hiring/pages/hiringArtifactState.ts)（第 424-448 行） | 只有 `status=completed 且 completed_slices>0` 才算成功，`blocked` 形态停在资料阶段 |

### 3.3 兜底（降级 / fallback）策略

| 实现点 | 文件:位置 | 作用 |
|---|---|---|
| 测试用例降级输出 | [packaging-test-cases/SKILL.md](../back-end/HireBot.ApiService/Assets/DigitalEmployeeTemplates/employment-coach-conversation/skills/packaging-test-cases/SKILL.md)「降级」段（第 120-127 行） | 三类输入皆空/校验失败时，写占位 JSON（`test_cases:[]`），`source=packaging-fallback`、`status=fallback` |
| 降级 JSON 校验 | [PackagingTestCasesJsonValidator.cs](../back-end/HireBot.Core/Services/Hiring/PackagingTestCasesJsonValidator.cs) `TryValidateFallbackTestCasesJson`（第 126 行） | 允许 `test_cases` 为空数组，接受降级产物 |
| 多级回退提取 | 同上 `TryExtractPackagingTestCasesBundle`（第 186 行） | 完整 bundle → 仅 merged → packaging-fallback → legacy merged-only（`TryExtractLegacyMergedOnly` 第 402 行），逐级降级仍尽量取到可用产物 |
| projection 失败降级 | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildDownstreamPrompt`（第 388-390 行） | 写文件工具不可用/回读失败 → 标记 `slices_not_ready` 跳过，不发成功 done |
| 打包失败降级 | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `buildPackageZipInstructionLines`（第 481 行） | 工具路径与 shell zip 都产不出包时，发 protocol failure fallback 并报具体阻塞原因 |
| 跳过 testcase 不阻塞打包 | [hiringDownstreamTriggers.ts](../front-end/src/features/hiring/pages/utils/hiringDownstreamTriggers.ts) `resolvePackagingRequestRoute`（第 1065 行）+ manifest.json `stage_rules` | 用户跳过/文件缺失时仍允许 `instance_packaging` 继续 |
| 关联 skill 兜底 | [EmployeeHiringService.cs](../back-end/HireBot.Core/Services/Hiring/EmployeeHiringService.cs)（第 1801-1815 行 `fallbackSkillIds`） | 链路缺失时回退到默认 skill 集合 |

---

## 4. 一个重要的工程事实（影响"在哪个模块"的答案）

经代码检索（`HiringWorkflowSupport`、`PackagingTestCasesJsonValidator` 的方法调用方）发现：

- 后端 C# 里的 `ParseAssistantReply` / `IsAllowedArtifactPath` / `ContainsSensitiveValue` / `ComputeSha256`，以及 `PackagingTestCasesJsonValidator` 的提取与校验方法，**在 hirebot 后端的业务主链路中目前几乎只被单元测试引用**，没有被业务代码主动调用。
- 这说明：**解析 LLM 流式回复标签、消费 dispatch_callback、驱动阶段推进的"主循环"主要在前端（React）+ 沙箱内的 Agent runtime（kingcrab）**；hirebot 后端的这些方法更多是「产物落盘 / 打包 / 回调同步」用的工具方法 + 契约定义，并由单测充当护栏。

**对回答的影响**：
- 「越界拦截 / 完整性校验 / 兜底」的**最前沿、对用户最直接生效的一层在前端 TS（提示词约束 + 状态机判定）和沙箱沙盒边界**；
- 后端 C# 提供的是**产物落盘与打包阶段的二次校验工具 + 契约 + 测试护栏**；
- 二者配合，而不是只有后端一处。

> 备注：以上为静态代码分析结论。若需 100% 确认后端这些工具方法的实际接入点（例如是否经由某个 Controller / MCP 工具在运行时调用），建议结合一次真实雇佣流程的运行日志做动态验证。

---

## 5. 总结

1. **切换 LLM 会影响稳定性，但影响的是"产物质量与一次成功率"，不是"流程骨架"。** 流程骨架是前端确定性代码 + 注入式强约束提示词，与具体模型解耦；而 skill 的实际执行（读 SKILL.md、写文件、发 artifact）由 LLM 在 kingcrab 沙箱完成，弱模型更容易出现「漏发 artifact 卡住 / 产物不合规走降级 / 越权被拦」。
2. **最典型的"skill 没执行"现象**，根因通常是 LLM 没按 artifact 协议吐出 `xxx_done`，导致前端状态机停在 `running`。
3. **三类防护齐备且分层冗余**：
   - 越界拦截：后端路径白名单/敏感值正则 + 沙箱读写根/网络白名单 + 提示词级打包根校验；
   - 完整性校验：后端 JSON 结构校验/SHA256/manifest 补齐/阶段必填判定 + 提示词级 manifest 与 projection 回读校验 + 前端成功形态判定；
   - 兜底：SKILL.md 降级输出 + 校验器多级回退 + 提示词级失败降级 + 跳过不阻塞打包 + 后端 fallbackSkillIds。
4. **换模型的安全做法**：跑现有契约测试回归，重点验证新模型的 artifact 协议遵循度与 JSON 结构遵循度，适当下调温度。

---

*本文档由代码静态分析整理，文件行号以分析时仓库状态为准，后续重构可能变动。*
