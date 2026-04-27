# HireBot 雇佣流程对接 Kingcrab 改造需求清单

## 1. 背景与目标

- HireBot 雇佣主流程保持不变（`hire -> conversation -> audit -> finalize`）。
- `TemplatePackages` 来源于构建端标准产物，目录结构保持不变，仅允许内容增量扩展。
- `SystemSkills` 属于系统内置资产，维持独立来源与独立下发逻辑。
- 下游模板包上传能力切换为复用 Kingcrab 接口：`/admin/digital-employee/upload`。
- `/media/upload` 仅作为局部能力复用（会话材料附件），不纳入本次主链路改造。
- 雇佣流程进入会话后由 AI 先发起提问，在人员参与对话过程中持续补充模板包内容，直至满足 finalize 条件。

## 2. 范围定义

### 2.1 In Scope（本次必须完成）

- 新增模板包上传适配层，并固定使用 `DigitalEmployee` 上传模式。
- 雇佣流程中的模板包上传统一使用 `/admin/digital-employee/upload`。
- 保持 `TemplatePackages` 原始结构透传，不改目录层级与相对路径。
- 基于会话过程增量丰富模板包内容（结构不变、内容可追加或更新）。
- 完成联调、回归验证与文档沉淀。

### 2.2 Out of Scope（本次不做）

- 改造雇佣业务状态机、API 路径、响应结构。
- 重构 `SystemSkills` 下发协议。
- 全量接入 `/media/upload` 到主流程。
- 改动构建端产物规范（仅消费既有标准格式）。

## 3. 现状约束

- `TemplatePackages` 目录基准（示例）：`Assets/TemplatePackages/default/NCrewTemplate/`
  - 包含 `config/`、`skills/`、`ontology/`、`manifest.json`。
- `SystemSkills` 目录基准（示例）：`Assets/SystemSkills/digital-employee-discovery/`。
- HireBot 当前通过 `KingCrew` 下游专用接口进行雇佣相关调用。
- Kingcrab `/admin/digital-employee/upload` 为 multipart ZIP 安装接口，语义为“安装数字员工模板包”。

## 4. 需求清单（功能）

## 4.1 模板包上传适配层

- [x] 在 `EmployeeHiringService` 中固定使用 `DigitalEmployee` 上传逻辑（调用 `/admin/digital-employee/upload`），不改变上层调用签名。
- [ ] 抽象独立 `ITemplatePackageUploader`（或等价策略接口）并沉淀到独立目录（当前仍在 `EmployeeHiringService` 内实现）。

## 4.2 DigitalEmployee 上传实现

- [x] 基于 `TemplatePackageDefinition` 构建 ZIP（内存流）。
- [ ] ZIP 内目录结构严格保持构建端标准结构：
  - [x] `skills/**`
  - [x] `manifest.json`
  - [ ] `config/**`（待补充完整校验）
  - [ ] `ontology/**`（待补充完整校验）
- [x] HTTP 请求使用 `multipart/form-data` 且字段名为 `file`。
- [x] 解析 `/admin/digital-employee/upload` 返回体并映射为内部上传结果模型。
- [x] 保持失败场景错误码与错误信息可追踪（401/400/429/500/超时）。

## 4.3 雇佣主流程稳定性（不变更）

- [x] `HireAsync` 入口与返回 DTO 保持不变。
- [x] `api/v1/hirings/*` 路径与对外契约保持不变。
- [x] `SystemSkills` 继续按当前内置逻辑上传。
- [x] `conversation/audit/finalize/artifacts` 主流程未改动。

## 4.4 配置与发布能力

- [x] 默认启用 `DigitalEmployee` 上传逻辑，已移除 `Legacy` 回退分支。
- [ ] 发布前在联调环境完成一次完整链路验证。

## 4.5 `/media/upload` 局部复用预留（非本期主交付）

- [ ] 预留材料附件扩展点（如 `mediaUrl` 或 metadata 约定）。
- [ ] 明确与主流程解耦，不影响当前上线范围。
- [ ] 输出后续版本改造建议（单独任务跟踪）。

## 4.6 会话驱动的模板包内容增量

- [x] 启动会话后由 AI 首轮主动提问（不依赖用户先发言）。
- [x] 人员在会话中的输入（文本/结构化答案/附件元数据）映射为模板包增量内容快照。
- [x] 每轮会话后维护“当前模板包快照”（运行时上下文），用于后续流程。
- [x] 阶段完成判定继续沿用现有规则，未满足字段时 AI 继续追问。
- [x] finalize 前沿用既有交付流程输出，不改变下载接口语义。
- [ ] 增量写入仅允许修改既有标准结构下的内容文件，不新增非标准目录层级（需补约束校验）。

## 5. 非功能需求

## 5.1 兼容性

- [ ] 对构建端新增文件/新增技能目录具备前向兼容能力（无需改代码）。
- [ ] 兼容当前 `TemplatePackages` 存量模板。

## 5.2 可观测性

- [ ] 记录模板包上传关键日志（模板ID、hireId、模式、耗时、状态码）。
- [ ] 日志中避免打印敏感 token 与原文大 payload。

## 5.3 安全性

- [ ] 请求透传鉴权符合现有 `KingCrew` 认证机制。
- [ ] ZIP 组包仅来自可信模板资产，不引入路径穿越条目。

## 5.4 性能与稳定性

- [ ] 上传超时、网络抖动具备明确错误返回。
- [ ] 大包场景不导致主线程阻塞或内存异常峰值。

## 6. 影响文件建议清单

- `src/HireBot.Core/Services/Hiring/EmployeeHiringService.cs`
- `src/HireBot.ApiService/appsettings.json`
- `src/HireBot.ApiService/appsettings.*.json`
- `tests/HireBot.Core.Tests/EmployeeHiringServiceTests.cs`
- `README.md` 或 `docs/`（配置与联调说明）

## 7. 验收标准（DoD）

- [ ] 在固定 `DigitalEmployee` 上传逻辑下，联调环境完成一次完整雇佣流程并成功产出 artifacts。
- [ ] 模板包上传成功后，Kingcrab 返回 `success=true` 且安装文件清单符合预期（联调验证）。
- [x] 会话开始后 AI 能自动首问，并根据多轮人机对话持续丰富模板包内容（代码与单测已覆盖核心路径）。
- [ ] 模板包内容增量不破坏标准目录结构，且可在 finalize 产物中验证（待联调）。
- [x] 雇佣流程核心接口响应结构无破坏性变化（代码路径保持）。
- [x] 失败场景返回可读错误（代码分支已覆盖 401/400/429/500/超时映射）。
- [x] 文档已补齐发布与排障说明（后续按联调结果补充记录）。

## 8. 测试清单

## 8.1 功能测试

- [ ] 创建雇佣任务（模板包上传成功）。
- [ ] 对话首轮 AI 主动提问。
- [ ] 多轮对话中模板包内容增量写入与阶段推进。
- [ ] 审核与 finalize。
- [ ] 交付物下载。

## 8.2 异常测试

- [ ] 下游 401/403。
- [ ] 下游 429。
- [ ] 下游 500。
- [ ] 网络超时与取消。

## 9. 风险与对策

- 风险：`/admin/digital-employee/upload` 偏“工作区安装”语义，可能带来覆盖风险。  
  对策：保持模板结构透传前提下，明确命名策略与幂等策略；必要时增加环境隔离。

- 风险：构建端模板体积增长导致上传超时。  
  对策：优化超时参数、补充重试策略（谨慎幂等）、完善日志。

- 风险：上传链路调整后问题定位困难。  
  对策：日志显式打印上传请求追踪 ID 与下游响应摘要。

## 10. 里程碑建议

- M1：完成 uploader 抽象与固定 `DigitalEmployee` 上传接入。
- M2：完成 `DigitalEmployee` 上传实现与单元测试。
- M3：联调环境灰度验证（小流量/指定模板）。
- M4：生产发布与观察。
