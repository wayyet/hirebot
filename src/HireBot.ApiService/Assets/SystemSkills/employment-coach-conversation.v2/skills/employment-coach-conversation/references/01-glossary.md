# 雇佣流程名词表

本文档是雇佣教练流程中所有特有名词的**唯一定义来源**。每个术语直接关联到后端代码中的实际类型、字段或方法。

---

## 沙箱与包

### 模板包
> 代码: `TemplatePackageDefinition` (`HireBot.Core/Services/Hiring/TemplatePackages/TemplatePackageModels.cs`)

数字员工的完整文件集合。包含 `config/`（身份配置）、`skills/`（能力定义）、`ontology/`（领域知识）、`external/`（外部对接）、`uploads/`（上传资料）等目录。以 zip 形式从文件系统加载，在沙箱中以目录形式存在。

### 参考模板包
> 代码: `HiringRuntimeContext.ReferenceTemplatePackage`

原始模板的只读副本。用户从模板池选择的模板（来自平台一或构建端）以 Reference 包的形式加载到沙箱中。雇佣教练可以阅读它来理解模板已有的能力，但**不能修改**它。

### 角色模板包
> 代码: `HiringRuntimeContext.RoleTemplatePackage`

雇佣教练**自身**的五件套。当沙箱启动时，系统将雇佣教练包上传到沙箱，使之成为沙箱中的对话主体。用户感知到的所有交互都来自这个包。

### 工作模板包
> 代码: `HiringRuntimeContext.WorkingTemplatePackage`

雇佣教练写入产物的**工作区**。所有对话中产生的中间产物（本体切片、技能文件、外部配置）都写入此包。它是最终实例打包的来源。系统层会自动将 `structured-data.json` 和 `materials.json` 写入此包的 `ontology/hiring-session/` 路径。

### 模板包状态
> 代码: `WorkingTemplatePackage.PackageFiles` 字典 + 各子目录的文件系统

模板包中每个目录/文件在**当前时刻**的实际内容。雇佣教练通过读取文件系统和 `todo.list` 来评估当前状态。

---

## TODO 体系

### TODO
> 代码: KingCrab `TodoTool.cs` → `SessionTodoItem` (Id, Text, Notes, Completed, CreatedAtUtc, UpdatedAtUtc)

模板包**当前状态**与**预期状态**之间差距的结构化记录。每条 TODO 对应一个具体的缺口（如"还缺一份决策规则类资料"）。TODO 通过 KingCrab 原生的 `todo` 工具进行增删改查，存储在 Session Metadata 中，随会话持久化。

### notes
> 代码: `SessionTodoItem.Notes`（string 字段，承载 JSON）

TODO 的**结构化上下文字段**。是系统层（后端 `EmployeeHiringService.ProjectTodoItem()` / 诊断 skill）解析 TODO 的唯一入口。内容为 JSON 字符串，包含 `stage`、`kind`、`gap_type`、`current_state`、`expected_state`、`acceptance_criteria`、`status`、`priority` 等字段。详见 `03-todo-guide.md`。

### gap（缺口 TODO）
> 代码: `notes.kind = "gap"`

表示模板包状态缺口的 TODO。由雇佣教练创建和维护。每个 gap TODO 描述"当前缺什么、预期是什么、怎么判断补齐了"。

### 诊断 TODO
> 代码: `notes.kind = "diagnosis"`

表示完备性诊断结果的 TODO。由诊断 skill 创建和维护。回答"还差什么"，带有 `level`（必需/推荐/可选）。不直接执行任何修改，只作为阶段推进的参考依据。

### TODO 状态
> 代码: `notes.status`（open / in_progress / done / dismissed）+ `SessionTodoItem.Completed`（系统层）

- `open`: 缺口已识别，尚未开始处理。`todo.add` 时的默认状态。
- `in_progress`: 正在处理中。通过 `todo.update` 设置。
- `done`: 缺口已解决。通过 `todo.complete` 设置（同时将系统层 `Completed` 置为 `true`）。
- `dismissed`: 缺口被用户明确跳过或撤回。通过 `todo.update` + 可选的 `todo.remove`。

---

## 阶段与推进

### 阶段
> 代码: `HiringCollectionStage` (Material / Skill / External)

雇佣流程的三个阶段：
- **资料阶段（material）**: 上传业务资料，提取本体知识
- **技能阶段（skill）**: 定义数字员工的能力清单
- **外部阶段（external）**: 配置外部系统对接

三个阶段按顺序推进，不可跳阶段。推进由 TODO 完成状态 + 诊断校验驱动。

### 阶段规则
> 代码: `DiscoveryStageRule`（Stage + SkillName + RequiredFields）

每个阶段的完成条件——即该阶段必须收集的**字段集合**。来自雇佣教练的 `manifest.json` 中的 `stage_rules` 定义。例如资料阶段可能需要 `business_domain`、`scenario_description` 等字段。

### 阶段完成度
> 代码: `HiringStageCompletionEvaluator.Evaluate()` → `HiringStageCompletionDto`

各阶段 `RequiredFields` 的收集进度。计算公式：`SatisfiedFieldCount / RequiredFieldCount`。当所有 RequiredFields 都有值时，该阶段的 `ReadyForNextStage = true`。

### 阶段就绪状态
> 代码: `HiringStageReadinessStatus` (missing / partial / complete / skipped)

诊断 skill 对每个阶段的评估结果：
- `missing`: 该阶段尚未开始，无任何 TODO
- `partial`: 有进展但不满足最低门槛
- `complete`: 所有必需 TODO 已完成
- `skipped`: 用户明确跳过（仅外部阶段）

### 结构化数据
> 代码: `HiringRuntimeContext.StructuredData`（`Dictionary<string, string?>`）

系统层自动汇总的各阶段字段键值对。来自雇佣教练在对话中对 `RequiredFields` 的收集。系统层会将其自动写入 `ontology/hiring-session/structured-data.json`。

---

## 配置治理

### 配置文件
> 代码: `HiringConfigFileKeys`（soul → `config/SOUL.md`, identity → `config/IDENTITY.md`, agents → `config/AGENTS.md`）

工作模板包中三个可被雇佣教练修改的配置：
- **SOUL.md**: 数字员工的核心使命和定位（"它是谁、为什么存在"）
- **IDENTITY.md**: 身份声明和对外展示（"它叫什么、什么形象、什么口吻"）
- **AGENTS.md**: 行为规范和边界约束（"它能做什么、绝不能做什么"）
- **MEMORY.md**: 模板预设记忆（**只读，不可修改**）

### 配置文件治理
> 代码: `HiringConfigGovernanceStateDto`

雇佣教练对上述三个配置文件的对话监听和修改机制。雇佣教练持续监听用户对数字员工身份/规则/边界的修改意图，通过"高置信度直接执行、低置信度反问确认"的混合机制更新配置文件。详见 `04-package-rules.md`。

### 凭据槽位
> 代码: `HiringCredentialSlotDto`（CredentialSlot + SecretRef + AuthKind + TargetSystem + BindingStatus）

API Key、Token 等敏感信息的**安全存储引用**。真实的凭据值由用户在独立表单中填写 → 系统层用 `DataProtectionProvider` 加密存入数据库 → 凭据槽位只保留引用（`secretRef`）和认证形式描述（`auth_kind`）。凭据值**绝不**出现在对话、TODO notes 或产物文件中。

---

## 诊断

### 诊断报告
> 代码: `HiringDiagnosticReportDto`（Status + Confidence + CurrentStage + ReadyForPackaging + StageReadiness + DiagnosticTodos）

诊断 skill 输出的完备性评估结果。包含：各阶段就绪状态、还缺什么的诊断项列表、是否可打包的判断。诊断报告由诊断 skill 产出，系统层解析后用于阶段推进判定。

### 完备性清单
> 代码: 模板包中的 completeness checklist（从模板配置中加载）

每个模板自带的阶段要求清单。定义了各阶段必须（required）、推荐（recommended）、可选（optional）的具体项。诊断 skill 以此为最高判断基准。当清单缺失时，使用诊断 skill 内置的默认最小门槛。

---

## 对话控制

### 对话暂停/恢复
> 代码: `HiringRuntimeContext.IsConversationPaused`

用户可随时暂停当前雇佣对话（不销毁沙箱），稍后恢复继续。暂停期间不接收新消息。

### 对话防并发
> 代码: `EmployeeHiringService` 中的 `conversationInFlight`（`ConcurrentDictionary`）

同一雇佣会话同一时间只允许一条消息在处理中。后续消息返回 409 冲突。

### 凭据安全检测
> 代码: `HiringWorkflowSupport.ContainsSensitiveValue()`

对用户输入的正则检测。如果用户在对话中直接输入了疑似 token/key/密码，雇佣教练应拦截并提示"请填写到凭据表单"。

---

## 模板包文件

### 五件套
> 代码: 模板包目录结构（config/ + skills/ + ontology/ + external/ + testcases/）

数字员工实例的五个组成部分：manifest（身份档案）、skill（能力层）、ontology（知识层）、api（工具层）、config（配置层）。在雇佣流程中，这些文件在 WorkingTemplatePackage 中逐步生成和补全。

### 产物文件
> 代码: `HiringRuntimeContext.ArtifactFiles`（`Dictionary<string, byte[]>`）

最终实例包的所有文件。在 `finalize` 时从沙箱下载 zip → 解压 → 合并 WorkingTemplatePackage → 得到最终产物字典。
