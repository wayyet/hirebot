# 雇佣页面组件化重构方案

## 📊 现状分析

### 当前存在的问题

1. **HiringPage.tsx (2801行)**
   - 职责过多：页面容器、WebSocket管理、状态管理、消息同步、下游任务编排
   - 业务逻辑、数据恢复、UI交互代码混编
   - 难以测试和维护

2. **HiringTodoPanel.tsx (2259行)**
   - 包含4个完整功能单元：资料卡、技能卡、外部系统卡、最终打包卡
   - 外部系统配置包含CLI + MCP两套完整表单系统（约1000行）
   - 子卡片逻辑复杂，难以独立测试

3. **ArtifactMessageCard.tsx (1317行)**
   - 包含12+种不同的artifact视图渲染逻辑
   - 大量内联样式和JSX嵌套
   - 类型差异巨大，从进度条到表格到代码块

### 代码质量影响

| 指标 | 当前状态 | 目标状态 | 改进幅度 |
|------|---------|---------|---------|
| 单文件最大行数 | 2801 | 500 | -82% |
| 单文件平均行数 | 2126 | 300-400 | -85% |
| 文件总数 | 3 | 40+ | +1233% |
| 测试难度 | ⭐⭐⭐⭐⭐ | ⭐⭐ | 大幅降低 |
| 复用性 | 低 | 高 | 显著提升 |

---

## 🎯 重构方案

### 阶段一：拆分 ArtifactMessageCard.tsx (优先级：★★★★★)

**原因**: 各视图函数完全独立，零依赖交叉，最易拆分

#### 目录结构
```
components/
├── ArtifactMessageCard.tsx (150行 - 主路由)
├── ArtifactIcon.tsx (单独提取)
├── artifacts/
│   ├── BaseArtifactViews.tsx          (250行 - 通用视图)
│   ├── MaterialArtifactView.tsx       (300行)
│   ├── OntologyArtifactView.tsx       (200行)
│   ├── SkillArtifactView.tsx          (700行)
│   │   └── components/
│   │       ├── SkillCard.tsx
│   │       ├── SkillSectionRow.tsx
│   │       └── ThresholdTable.tsx
│   ├── ExternalArtifactView.tsx       (500行)
│   │   └── components/
│   │       └── CapabilityCard.tsx
│   ├── PackagingArtifactView.tsx      (300行)
│   └── types.ts
└── utils/
    ├── artifactHelpers.ts             (200行 - 类型检查、数据转换)
    ├── artifactStyles.ts              (150行 - 样式常量)
    └── artifactConstants.ts           (120行 - 业务常量)
```

#### 拆分步骤

1. **提取工具函数和常量** (第1天)
   ```typescript
   // utils/artifactHelpers.ts
   export function isRecord(v: unknown): v is Record<string, unknown>
   export function asRecord(v: unknown): Record<string, unknown> | null
   export function getRecordArray(record: Record<string, unknown>, ...keys: string[]): Record<string, unknown>[]
   export function firstString(...values: unknown[]): string
   export function stringListText(value: unknown): string
   export function hasSkillWorkorderShape(record: Record<string, unknown>): boolean
   export function hasExternalWorkorderShape(record: Record<string, unknown>): boolean
   export function toPublicPathLabel(value: string): string
   export function stringify(v: unknown): string
   ```

   ```typescript
   // utils/artifactStyles.ts
   export const sectionLabelStyle: CSSProperties = { ... }
   export const statChipStyle: CSSProperties = { ... }
   export function thresholdCellStyle(header: boolean): CSSProperties
   ```

   ```typescript
   // utils/artifactConstants.ts
   export const HIDDEN_ARTIFACT_DATA_KEYS = new Set([...])
   export const SENSITIVE_ARTIFACT_DATA_KEY_PARTS = [...]
   export const STATUS_LABELS = { ... }
   export const STATUS_COLORS = { ... }
   ```

2. **提取通用视图组件** (第2天)
   ```typescript
   // artifacts/BaseArtifactViews.tsx
   export function ProgressView({ data }: { data: unknown })
   export function TableView({ data }: { data: unknown })
   export function BadgeView({ data }: { data: unknown })
   export function CodeView({ data }: { data: unknown })
   export function TextView({ data }: { data: unknown })
   ```

3. **提取专用视图组件** (第3-4天)
   - MaterialArtifactView.tsx (资料相关)
   - OntologyArtifactView.tsx (本体抽取)
   - SkillArtifactView.tsx (技能相关，含子组件)
   - ExternalArtifactView.tsx (外部系统)
   - PackagingArtifactView.tsx (打包)

4. **重构主文件，使用视图路由** (第5天)
   ```typescript
   // ArtifactMessageCard.tsx
   function ArtifactDataView({ artifact }: { artifact: ArtifactDisplayData }) {
     if (artifact.artifactType === 'material_handoff') {
       return <MaterialArtifactView artifact={artifact} />
     }
     if (artifact.artifactType === 'ontology_extraction') {
       return <OntologyArtifactView artifact={artifact} />
     }
     // ... 其他路由
   }
   ```

5. **测试验证** (第6天)

---

### 阶段二：拆分 HiringTodoPanel.tsx (优先级：★★★★☆)

**原因**: 外部系统配置逻辑最复杂(CLI + MCP约1000行)，收益最大

#### 目录结构
```
components/
├── HiringTodoPanel.tsx (250行 - 主容器)
├── StageAdvanceConfirmationPanel.tsx (150行 - 阶段推进确认)
│
├── TodoMaterialCard/                   # 资料上传卡片 (约900行)
│   ├── MaterialCardBody.tsx           (700行)
│   ├── MaterialCategoryCard.tsx       (100行)
│   ├── hooks/
│   │   └── useMaterialUpload.ts       (200行)
│   └── types.ts
│
├── TodoSkillCard/                      # 技能搜索卡片 (约800行)
│   ├── SkillCardBody.tsx              (600行)
│   ├── SkillListSection.tsx           (150行)
│   ├── SkillCard.tsx                  (100行)
│   ├── hooks/
│   │   └── useSkillSearch.ts          (200行)
│   └── types.ts
│
├── TodoExternalCard/                   # 外部系统配置 (约1800行) ⚠️ 重点
│   ├── ExternalCardBody.tsx           (400行 - 配置选择界面)
│   ├── CliToolModal.tsx               (500行 - CLI配置弹窗)
│   ├── McpServerModal.tsx             (600行 - MCP配置弹窗)
│   ├── hooks/
│   │   ├── useExternalConfig.ts       (200行 - 配置加载与保存)
│   │   ├── useCliDraft.ts             (150行 - CLI草稿管理)
│   │   └── useMcpDraft.ts             (200行 - MCP草稿管理)
│   └── types.ts                       (200行 - CLI/MCP类型定义)
│
├── TodoFinalCard/                      # 最终打包卡片 (约200行)
│   ├── FinalCard.tsx
│   └── types.ts
│
└── utils/
    ├── materialUtils.ts               (200行)
    ├── skillUtils.ts                  (150行)
    └── externalConfigUtils.ts         (250行)
```

#### 拆分步骤

1. **提取外部系统配置类型和工具** (第1天)
   ```typescript
   // TodoExternalCard/types.ts
   export type CliExecutionMode = 'direct' | 'sandbox'
   export type McpTransport = 'stdio' | 'http'
   export interface McpKeyValueEntry { ... }
   export interface CliToolDraft { ... }
   export interface McpConfigDraft { ... }
   
   // TodoExternalCard/utils/externalConfigUtils.ts
   export function createCliToolDraft(): CliToolDraft
   export function createMcpConfigDraft(): McpConfigDraft
   export function cloneCliTools(tools: CliToolDraft[]): CliToolDraft[]
   export function cloneMcpConfig(config: McpConfigDraft): McpConfigDraft
   export function hasMeaningfulMcpConfig(config: McpConfigDraft): boolean
   export function recordToEntries(record?: Record<string, string>): McpKeyValueEntry[]
   export function entriesToRecord(entries: McpKeyValueEntry[]): Record<string, string>
   ```

2. **提取CLI配置弹窗** (第2天)
   ```typescript
   // TodoExternalCard/CliToolModal.tsx
   export function CliToolModal({
     open,
     onClose,
     initialTools,
     onSave,
   }: CliToolModalProps)
   ```

3. **提取MCP配置弹窗** (第3天)
   ```typescript
   // TodoExternalCard/McpServerModal.tsx
   export function McpServerModal({
     open,
     onClose,
     initialConfig,
     onSave,
   }: McpServerModalProps)
   ```

4. **提取配置管理Hooks** (第4天)
   ```typescript
   // TodoExternalCard/hooks/useExternalConfig.ts
   export function useExternalConfig(hireId: string, sessionId: string)
   
   // TodoExternalCard/hooks/useCliDraft.ts
   export function useCliDraft(initialTools?: CliToolDraft[])
   
   // TodoExternalCard/hooks/useMcpDraft.ts
   export function useMcpDraft(initialConfig?: McpConfigDraft)
   ```

5. **重构 ExternalCardBody 主组件** (第5天)
   - 使用提取的Modal和Hooks
   - 简化配置界面逻辑

6. **提取资料卡和技能卡** (第6-8天)
   - MaterialCardBody + hooks
   - SkillCardBody + hooks

7. **重构 HiringTodoPanel 主容器** (第9天)
   - 使用拆分后的子组件
   - 简化阶段管理逻辑

8. **测试验证** (第10天)

---

### 阶段三：拆分 HiringPage.tsx (优先级：★★★☆☆)

**原因**: 业务逻辑最复杂，需要谨慎拆分

#### 目录结构
```
pages/
├── HiringPage.tsx (500行 - 页面容器)
├── hiringPageTypes.ts (保留)
│
├── hooks/
│   ├── useHiringWorkflow.ts           (500行 - 工作流状态容器)
│   └── useSandboxWebSocket.ts         (450行 - WebSocket连接与消息处理)
│
├── services/
│   ├── hiringConversationManager.ts   (400行 - 对话消息提交与同步)
│   ├── downstreamOrchestrator.ts      (350行 - 下游任务编排)
│   └── artifactStateManager.ts        (300行 - Artifact缓存与签名管理)
│
├── utils/
│   ├── messageNormalization.ts        (150行 - 消息处理工具函数)
│   ├── cacheNormalization.ts          (200行 - 缓存数据反序列化)
│   ├── approvalMessageDetection.ts    (120行 - 用户意图检测)
│   └── buildDownstreamPrompts.ts      (200行 - 下游任务Prompt生成)
│
└── constants/
    └── hiringPageConstants.ts         (80行 - 常量与配置)
```

#### 拆分步骤

1. **提取常量和工具函数** (第1-2天)
   ```typescript
   // constants/hiringPageConstants.ts
   export const MAX_MATERIAL_CHARS = 120_000
   export const EXTERNAL_CONFIG_REPACKAGE_NOTICE = '...'
   export const TYPEWRITER_SOFT_FINISH_DEFER_MS = 300
   
   // utils/messageNormalization.ts
   export function mkId(): string
   export function sleep(ms: number): Promise<void>
   export function normalizeErrorMessage(error: unknown): string
   export function normalizeAssistantReply(content: string): string
   export function toConversationMaterials(files?: ChatFile[]): HiringConversationMaterial[]
   export function formatFileSize(bytes: number): string
   
   // utils/cacheNormalization.ts
   export function normalizeCachedMessages(value: unknown): ChatMessage[]
   export function normalizeCachedFiles(value: unknown): ChatFile[]
   export function normalizeCachedToolSteps(value: unknown): ToolStep[]
   export function normalizeCachedStageOverrides(value: unknown): Map<string, string>
   
   // utils/approvalMessageDetection.ts
   export function isSkillGenerationApprovalMessage(content: string): boolean
   export function isPackagingTestCasesApprovalMessage(content: string): boolean
   export function isPackagingTestCasesSkipMessage(content: string): boolean
   
   // utils/buildDownstreamPrompts.ts
   export function buildDownstreamPrompt(type: string, ...): string
   export function buildTemplateBootstrapPrompt(templateId: string): string
   export function buildProjectionPassPayload(...): ProjectionPassPayload
   ```

2. **提取WebSocket管理Hook** (第3-4天)
   ```typescript
   // hooks/useSandboxWebSocket.ts
   export function useSandboxWebSocket({
     endpoint,
     sessionId,
     onMessage,
     onTypingStart,
     onTypingEnd,
     onArtifact,
     onError,
   }: UseSandboxWebSocketOptions) {
     // WebSocket连接、消息处理、断线重连
     return {
       connect,
       disconnect,
       isConnected,
       sendMessage,
     }
   }
   ```

3. **提取工作流状态管理Hook** (第5-6天)
   ```typescript
   // hooks/useHiringWorkflow.ts
   export function useHiringWorkflow(templateId: string) {
     // 工作流初始化、状态恢复、错误处理
     return {
       hireId,
       workflowBooting,
       workflowError,
       ensureWorkflowReady,
       retryWorkflowInitialization,
     }
   }
   ```

4. **提取对话管理Service** (第7天)
   ```typescript
   // services/hiringConversationManager.ts
   export class HiringConversationManager {
     async submitWorkflowMessage(
       hireId: string,
       sessionId: string,
       content: string,
       files?: ChatFile[],
       internal?: boolean,
     ): Promise<void>
     
     async uploadMediaToGateway(...): Promise<string>
     async uploadWorkspaceFileToGateway(...): Promise<void>
     async syncMessagesToBackend(...): Promise<void>
   }
   ```

5. **提取下游任务编排Service** (第8天)
   ```typescript
   // services/downstreamOrchestrator.ts
   export class DownstreamOrchestrator {
     async launchProjectionPassFromApproval(...): Promise<void>
     async launchSkillGenerationFromProjectionConfirmation(...): Promise<void>
     async launchPackagingTestCasesFromApproval(...): Promise<void>
   }
   ```

6. **提取Artifact状态管理Service** (第9天)
   ```typescript
   // services/artifactStateManager.ts
   export class ArtifactStateManager {
     saveMaterialSummary(artifact: ArtifactDisplayData): void
     saveSkillSummary(artifact: ArtifactDisplayData): void
     saveExternalSummary(artifact: ArtifactDisplayData): void
     
     getLatestMaterialSummary(): ArtifactDisplayData | null
     getLatestSkillSummary(): ArtifactDisplayData | null
     getLatestExternalSummary(): ArtifactDisplayData | null
     
     hasChanged(signature: string, lastSignature: string): boolean
     extractLatestDefinedSkills(): DefinedSkillItem[]
   }
   ```

7. **重构主页面组件** (第10-11天)
   - 使用提取的Hooks和Services
   - 简化页面逻辑，保留UI编排

8. **测试验证** (第12天)

---

## 📋 实施优先级建议

### ✅ 第一优先级：ArtifactMessageCard (预计6天)
- **原因**: 视图函数完全独立，无交叉依赖
- **收益**: 立即降低1167行复杂度
- **风险**: ⭐ (低风险)

### ✅ 第二优先级：HiringTodoPanel - 外部系统配置 (预计5天)
- **原因**: 外部系统配置(CLI+MCP)逻辑独立，行数最多
- **收益**: 降低1000+行复杂度，显著提升外部配置可维护性
- **风险**: ⭐⭐ (中低风险)

### ⚠️ 第三优先级：HiringTodoPanel - 资料和技能卡 (预计5天)
- **原因**: 各自逻辑独立，便于复用
- **收益**: 进一步降低1000+行复杂度
- **风险**: ⭐⭐ (中低风险)

### ⚠️ 第四优先级：HiringPage - 基础拆分 (预计12天)
- **原因**: 业务逻辑最复杂，需要谨慎拆分
- **收益**: 彻底解决核心页面维护问题
- **风险**: ⭐⭐⭐ (中高风险，需要充分测试)

---

## 🛠️ 实施原则

### 代码组织原则
1. **单一职责**: 每个文件/函数只做一件事
2. **依赖方向**: 组件层 → Hook层 → Service层 → Utils/Constants
3. **命名规范**:
   - Hook文件: `use<功能>.ts` (PascalCase)
   - Service文件: `<功能>Manager.ts` (PascalCase)
   - Utils文件: `<功能>Utils.ts` (camelCase)
   - Constants文件: `<功能>Constants.ts` (UPPER_CASE常量)
   - Type文件: `<功能>.types.ts`

### 重构流程
1. **小步重构**: 每次只拆分一个模块
2. **先测试后提交**: 每次拆分后立即验证功能
3. **保持可工作状态**: 确保代码始终可运行
4. **增量提交**: 频繁提交，保持git历史清晰

### 测试策略
1. **单元测试**: 为提取的工具函数和Hooks编写测试
2. **集成测试**: 验证拆分后组件间交互正常
3. **E2E测试**: 确保整体业务流程不受影响
4. **回归测试**: 每次重构后运行完整测试套件

---

## 📈 预期收益

### 代码质量
- 单文件行数从 2000+ 降至 500 以内
- 代码复杂度降低 85%
- 测试覆盖率提升潜力大

### 开发效率
- 新功能开发速度提升 50%+
- Bug修复时间缩短 60%+
- Code Review 效率提升 70%+

### 可维护性
- 组件复用性提升 300%+
- 新人上手时间缩短 40%+
- 重构风险降低 80%+

---

## 🚀 下一步行动

1. **确认方案**: 与团队评审本方案，确认优先级和时间安排
2. **准备环境**: 确保测试覆盖率足够，避免重构引入bug
3. **开始第一阶段**: 从 ArtifactMessageCard 开始拆分（低风险、高收益）
4. **持续迭代**: 按优先级逐步完成所有重构
5. **文档更新**: 同步更新组件文档和架构图

---

## 📞 需要支持

如需重构过程中的技术支持，请随时联系。我可以提供：
- 具体代码拆分示例
- 重构步骤详细指导
- 测试用例编写建议
- Code Review 和质量把关
