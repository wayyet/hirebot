import { ApiClientError, buildUrl, httpClient } from '../httpClient'
import type { ApiResponseEnvelope } from '../types'
import { tokenService } from '@/infra/auth/token-service'

export const HiringCollectionPhase = {
  NotStarted: 'NOT_STARTED',
  InProgress: 'IN_PROGRESS',
  ReadyForFinalize: 'READY_FOR_FINALIZE',
  Finalized: 'FINALIZED',
} as const

export const HiringCollectionStage = {
  Material: 'material',
  Skill: 'skill',
  External: 'external',
  ReadyForPackaging: 'ready_for_packaging',
} as const

export const HiringTodoStatus = {
  Open: 'open',
  InProgress: 'in_progress',
  Done: 'done',
  NeedsReview: 'needs_review',
  Dismissed: 'dismissed',
  Resolved: 'resolved',
} as const

export const HiringStageReadinessStatus = {
  Missing: 'missing',
  Partial: 'partial',
  Complete: 'complete',
  Skipped: 'skipped',
} as const

export const HiringAuditDecision = {
  Approve: 'APPROVE',
  RequestChanges: 'REQUEST_CHANGES',
  RollbackToStage: 'ROLLBACK_TO_STAGE',
  ForceOverride: 'FORCE_OVERRIDE',
} as const

export type HiringCollectionPhaseType =
  typeof HiringCollectionPhase[keyof typeof HiringCollectionPhase]

export type HiringCollectionStageType =
  typeof HiringCollectionStage[keyof typeof HiringCollectionStage]

export type HiringAuditDecisionType =
  typeof HiringAuditDecision[keyof typeof HiringAuditDecision]

export interface StageSkillMapping {
  stage: string
  skillName: string
  requiredFields: string[]
  description: string
}

export interface HiringStageCompletion {
  stage: string
  requiredFieldCount: number
  satisfiedFieldCount: number
  completionRate: number
  satisfiedFields: string[]
  blockingFields: string[]
  readyForNextStage: boolean
}

export interface WorkflowTodo {
  id: string
  title: string
  stage: string
  kind: string
  status: string
  gapType?: string | null
  priority?: string | null
  currentState?: string | null
  expectedState?: string | null
  acceptanceCriteria?: string | null
  acceptanceEvidence?: string | null
  source: string
  fingerprint?: string | null
  category?: string | null
  payload?: Record<string, unknown> | null
  level?: string | null
  question?: string | null
  evidence?: string | null
  suggestedAction?: string | null
  relatedTodoIds: string[]
  relatedFiles: string[]
  createdAtUtc: string
  updatedAtUtc: string
}

/** 对应后端 HiringWorkflowHandoffDto 的 JSON 输出 (snake_case) */
export interface HandoffItem {
  session_id: string
  workflow_id: string
  handoff_id: string
  title: string
  kind: string
  stage: string
  target_skill: string
  intent?: string | null
  category?: string | null
  payload?: Record<string, unknown> | null
  source?: string | null
  acceptance?: string | null
  status: string
  fingerprint: string
  related_todos: string[]
  related_files: string[]
  revision: number
  created_at: string
  updated_at: string
  dispatch_id?: string | null
  callback_summary?: string | null
}

export interface DispatchArtifact {
  path: string
  kind: string
  encoding: string
  sha256: string
  /** 对应 contracts/artifacts.json 中声明的渲染类型 */
  display?: 'progress' | 'tree' | 'table' | 'code' | 'badge' | null
  /** 是否为阶段终结产物 */
  terminal?: boolean | null
}

export interface DispatchTodoResult {
  todoId: string
  status: string
  artifacts: DispatchArtifact[]
  errors: string[]
}

export interface DispatchCallback {
  dispatchId: string
  target: string
  status: string
  todoIds: string[]
  note?: string | null
  userSummary?: string | null
  artifacts: DispatchArtifact[]
  todoResults: DispatchTodoResult[]
  createdAtUtc: string
  completedAtUtc?: string | null
  errors: string[]
}

export interface StageReadiness {
  stage: string
  status: string
  reason: string
  blockingTodoIds: string[]
}

export interface DiagnosticTodo {
  id: string
  stage: string
  level: string
  category: string
  question: string
  evidence: string
  suggestedAction: string
  relatedTodoIds: string[]
}

export interface DiagnosticReport {
  status: string
  confidence: string
  currentStage: string
  readyForPackaging: boolean
  stageReadiness: StageReadiness[]
  diagnosticTodos: DiagnosticTodo[]
  todoCorrelation: string[]
  openQuestions: string[]
  userSummary: string
  generatedAtUtc: string
}

export interface WorkflowRuntimeFacts {
  materialReady: boolean
  materialClassifiedFiles: string[]
  materialExtractionTargets: Record<string, string>
  skillBaselineReviewed: boolean
  skillBaselineConfirmed: boolean
}

export interface ConfigGovernanceFile {
  configKey: string
  displayName: string
  relativePath: string
  content: string
  summary: string
  updatedAtUtc: string
  affectedTodoIds: string[]
}

export interface ConfigGovernanceState {
  files: ConfigGovernanceFile[]
  pendingReviewTodoIds: string[]
  updatedAtUtc?: string | null
}

export interface HiringCliToolConfig {
  /** 工具唯一标识 */
  name: string
  /** 可执行文件路径 */
  command: string
  /** AI 理解何时调用的描述 */
  description: string
  /** 输入参数 JSON Schema */
  parameters: Record<string, unknown>
  /** sandbox = 沙箱隔离执行, direct = 直接执行 */
  executionMode: 'sandbox' | 'direct' | string
}

export interface HiringMcpServerConfig {
  /** 传输方式: stdio = 启动本地进程, http = 连接远程服务器 */
  transport: 'stdio' | 'http' | string
  /** MCP Server 标识名称 */
  name: string
  // ── stdio 字段 ──
  /** 启动 MCP Server 的命令 */
  command?: string | null
  /** 启动参数 */
  args?: string[] | null
  /** 显式设置的环境变量 */
  env?: Record<string, string> | null
  /** 透传给子进程的宿主机环境变量名列表 */
  envPassThrough?: string[] | null
  /** 工作目录 */
  cwd?: string | null
  // ── http 字段 ──
  /** MCP Server URL */
  url?: string | null
  /** 包含 Bearer Token 的环境变量名（非明文 token） */
  bearerTokenEnv?: string | null
  /** 静态 HTTP 请求头 */
  headers?: Record<string, string> | null
  /** 值从环境变量读取的请求头（key=header 名, value=环境变量名） */
  headersFromEnv?: Record<string, string> | null
}

export interface HiringExternalSystemConfig {
  submissionMode?: 'pending' | 'configured' | 'skipped' | string
  cliTools: HiringCliToolConfig[]
  mcpServer?: HiringMcpServerConfig | null
  updatedAtUtc?: string | null
}

export interface DownstreamRunInfo {
  status: string
  result?: unknown
  error?: string
}

export interface RuntimeStateSnapshot {
  stageOverrides?: Record<string, unknown>
  downstreamRuns?: Record<string, DownstreamRunInfo>
}

export interface RuntimeStateSaveRequest {
  stageOverrides?: Record<string, unknown>
  downstreamRuns?: Record<string, DownstreamRunInfo>
}

export interface StartHiringConversationResult {
  hireId: string
  sessionId: string
  currentStage: string
  requiresAudit: boolean
  stageSkills: StageSkillMapping[]
  isConversationPaused?: boolean
  isConversationResponding?: boolean
}

export interface HiringConversationMaterial {
  type: 'text' | 'file' | 'skill' | string
  name: string
  content?: string | null
  contentHash?: string | null
  size?: number | null
  mimeType?: string | null
  metadata?: Record<string, string>
}

export interface UploadedMaterial {
  type: string
  name: string
  content?: string | null
  contentHash?: string | null
  size?: number | null
  mimeType?: string | null
  metadata?: Record<string, string>
}

export interface HiringMaterialFile {
  materialFileId: string
  relativePath: string
  originalFileName: string
  sizeBytes: number
  format: string
  mimeType?: string | null
  sha256: string
  requestedCategoryTitle?: string | null
  workspaceRelativePath?: string | null
  uploadedAtUtc: string
  updatedAtUtc: string
}

export interface HiringConversationMessageRequest {
  content: string
  structuredAnswers?: Record<string, string>
  materials?: HiringConversationMaterial[]
}

export interface HiringConversationSyncRequest {
  userMessage: string
  assistantReply: string
  materials?: HiringConversationMaterial[]
}

export interface HiringConversationMessage {
  messageId: string
  role: string
  content: string
  createdAt: string
}

export interface HiringStagePreview {
  hireId: string
  stage: string
  skillName: string
  summary: string
  structuredData: Record<string, string | null>
  missingFields: string[]
  riskNotes: string[]
  readyForAudit: boolean
  generatedAt: string
}

export interface HiringConversationResult {
  hireId: string
  sessionId: string
  currentStage: string
  requiresAudit: boolean
  assistantMessage: HiringConversationMessage
  latestPreview: HiringStagePreview
  isConversationPaused?: boolean
  isConversationResponding?: boolean
}

export interface HiringAuditDecisionRequest {
  stage: string
  decision: HiringAuditDecisionType | string
  comment?: string
  rollbackTargetStage?: string
}

export interface HiringAuditDecisionResult {
  hireId: string
  stage: string
  decision: string
  currentStage: string
  requiresAudit: boolean
  collectionPhase: string
}

export interface HiringAuditLog {
  logId: string
  stage: string
  skillName: string
  decision: string
  actor: string
  comment?: string | null
  inputDigest: string
  outputDigest: string
  timestampUtc: string
}

export interface HiringFinalizeResult {
  hireId: string
  currentStage: string
  collectionPhase: string
  generatedFiles: string[]
  downloadUrl: string
  employeeId?: string | null
}

export interface HiringWorkflowState {
  hireId: string
  sessionId: string
  currentStage: string
  requiresAudit: boolean
  collectionPhase: string
  stageSkills: StageSkillMapping[]
  auditLogs: HiringAuditLog[]
  templatePackageId?: string | null
  templatePackageVersion?: string | null
  discoverySkillId?: string | null
  discoverySkillVersion?: string | null
  stageCompletion?: HiringStageCompletion[] | null
  workflowTodos?: WorkflowTodo[] | null
  handoffItems?: HandoffItem[] | null
  latestDispatches?: DispatchCallback[] | null
  latestDiagnosticReport?: DiagnosticReport | null
  configGovernance?: ConfigGovernanceState | null
  stageReadiness?: StageReadiness[] | null
  runtimeFacts?: WorkflowRuntimeFacts | null
  isConversationPaused?: boolean
  isConversationResponding?: boolean
}

export interface HiringArtifactsDownloadData {
  fileName: string
  blob: Blob
}

function buildArtifactsDownloadUrl(hireId: string): string {
  const path = `/api/v1/hirings/${hireId}/artifacts/download`
  return buildUrl(path)
}

function buildArtifactFileDownloadUrl(hireId: string, artifactName: string): string {
  const safeArtifactPath = artifactName
    .split('/')
    .map(segment => encodeURIComponent(segment))
    .join('/')
  const path = `/api/v1/hirings/${hireId}/artifacts/${safeArtifactPath}`
  return buildUrl(path)
}

function parseFileName(contentDisposition?: string | null): string | null {
  if (!contentDisposition) {
    return null
  }

  const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(contentDisposition)
  if (!match) {
    return null
  }

  const encoded = match[1] ?? match[2]
  if (!encoded) {
    return null
  }

  try {
    return decodeURIComponent(encoded)
  } catch {
    return encoded
  }
}

export const hiringWorkflowApi = {
  startConversation(hireId: string) {
    return httpClient.post<StartHiringConversationResult>(`/api/v1/hirings/${hireId}/conversation/start`)
  },

  resetConversation(hireId: string) {
    return httpClient.post<StartHiringConversationResult>(`/api/v1/hirings/${hireId}/conversation/reset`)
  },

  sendConversationMessage(hireId: string, payload: HiringConversationMessageRequest) {
    return httpClient.post<HiringConversationResult, HiringConversationMessageRequest>(
      `/api/v1/hirings/${hireId}/conversation/messages`,
      payload,
    )
  },

  syncConversationTurn(hireId: string, payload: HiringConversationSyncRequest) {
    return httpClient.post<HiringConversationResult, HiringConversationSyncRequest>(
      `/api/v1/hirings/${hireId}/conversation/sync`,
      payload,
    )
  },

  async uploadMaterialFile(
    hireId: string,
    file: File,
    metadata: Record<string, string>,
  ): Promise<UploadedMaterial> {
    const path = `/api/v1/hirings/${hireId}/materials/upload`
    const url = buildUrl(path)
    const accessToken = await tokenService.ensureFresh()

    const form = new FormData()
    form.append('file', file)
    Object.entries(metadata ?? {}).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        form.append(key, String(value))
      }
    })

    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken
        ? {
          Authorization: `Bearer ${accessToken}`,
        }
        : undefined,
      body: form,
    })

    const text = await response.text()
    if (!response.ok) {
      try {
        const payload = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(
          payload.message?.trim() || `请求失败（HTTP ${response.status}）`,
          response.status,
          payload.code,
          payload,
        )
      } catch {
        throw new ApiClientError(`请求失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }

    const envelope = JSON.parse(text) as ApiResponseEnvelope<UploadedMaterial>
    if (!envelope?.data) {
      throw new ApiClientError('上传返回数据为空', response.status, envelope?.code, envelope)
    }

    return envelope.data
  },

  submitAuditDecision(hireId: string, payload: HiringAuditDecisionRequest) {
    return httpClient.post<HiringAuditDecisionResult, HiringAuditDecisionRequest>(
      `/api/v1/hirings/${hireId}/audit-decisions`,
      payload,
    )
  },

  async downloadArtifacts(hireId: string): Promise<HiringArtifactsDownloadData> {
    const url = buildArtifactsDownloadUrl(hireId)
    const accessToken = await tokenService.ensureFresh()
    const response = await fetch(url, {
      method: 'GET',
      headers: accessToken
        ? {
          Authorization: `Bearer ${accessToken}`,
        }
        : undefined,
    })

    if (!response.ok) {
      const text = await response.text()
      try {
        const payload = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(
          payload.message?.trim() || `请求失败（HTTP ${response.status}）`,
          response.status,
          payload.code,
          payload,
        )
      } catch {
        throw new ApiClientError(`请求失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }

    const fileName = parseFileName(response.headers.get('content-disposition')) ?? `${hireId}_artifacts.zip`
    const blob = await response.blob()

    return {
      fileName,
      blob,
    }
  },

  async downloadArtifactFile(hireId: string, artifactName: string): Promise<HiringArtifactsDownloadData> {
    const url = buildArtifactFileDownloadUrl(hireId, artifactName)
    const accessToken = await tokenService.ensureFresh()
    const response = await fetch(url, {
      method: 'GET',
      headers: accessToken
        ? {
          Authorization: `Bearer ${accessToken}`,
        }
        : undefined,
    })

    if (!response.ok) {
      const text = await response.text()
      try {
        const payload = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(
          payload.message?.trim() || `请求失败（HTTP ${response.status}）`,
          response.status,
          payload.code,
          payload,
        )
      } catch {
        throw new ApiClientError(`请求失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }

    const fileName = parseFileName(response.headers.get('content-disposition')) ?? artifactName
    const blob = await response.blob()

    return {
      fileName,
      blob,
    }
  },

  /**
   * 前端从沙箱网关下载产物包后，直接上传至后端保存为数字员工，绕过 KingCrab 依赖。
   * @param hireId 雇佣 ID
   * @param packageBlob 从沙箱网关下载的 ZIP 包
   * @param fileName 原始文件名（用于后端存储和日志）
   * @param skillIds 用户在 TODO 面板关联的 store skill UUID 列表；后端会从 ncrew-builder 下载并合并到最终产物。
   */
  async importPackage(
    hireId: string,
    packageBlob: Blob,
    fileName: string,
    skillIds?: readonly string[],
  ): Promise<HiringFinalizeResult> {
    const url = buildUrl(`/api/v1/hirings/${encodeURIComponent(hireId)}/import-package`)
    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    form.append('packageFile', packageBlob, fileName)
    // multipart 重复字段：后端 [FromForm] string[]? skillIds 会聚合成数组
    if (skillIds && skillIds.length > 0) {
      const unique = new Set<string>()
      for (const id of skillIds) {
        const trimmed = id?.trim()
        if (trimmed && !unique.has(trimmed)) {
          unique.add(trimmed)
          form.append('skillIds', trimmed)
        }
      }
    }
    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      body: form,
    })

    if (!response.ok) {
      const text = await response.text()
      try {
        const payload = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(
          payload.message?.trim() || `请求失败（HTTP ${response.status}）`,
          response.status,
          payload.code,
          payload,
        )
      } catch {
        throw new ApiClientError(`请求失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }

    const envelope = await response.json() as ApiResponseEnvelope<HiringFinalizeResult>
    if (!envelope.success || !envelope.data) {
      throw new ApiClientError(envelope.message?.trim() || '导入产物包失败', envelope.code ?? response.status, envelope.code, envelope)
    }
    return envelope.data
  },

  /** 获取运行时状态（阶段覆盖 + 下游运行记录）。*/
  async getRuntimeState(hireId: string): Promise<RuntimeStateSnapshot> {
    return httpClient.get<RuntimeStateSnapshot>(`/api/v1/hirings/${encodeURIComponent(hireId)}/runtime-state`)
  },

  /** 保存运行时状态（阶段覆盖 + 下游运行记录）。*/
  async saveRuntimeState(hireId: string, state: RuntimeStateSaveRequest): Promise<void> {
    await httpClient.put<boolean>(`/api/v1/hirings/${encodeURIComponent(hireId)}/runtime-state`, state)
  },

  async getExternalConfig(hireId: string): Promise<HiringExternalSystemConfig> {
    return httpClient.get<HiringExternalSystemConfig>(`/api/v1/hirings/${encodeURIComponent(hireId)}/external-config`)
  },

  async saveExternalConfig(hireId: string, payload: HiringExternalSystemConfig): Promise<HiringExternalSystemConfig> {
    return httpClient.put<HiringExternalSystemConfig>(
      `/api/v1/hirings/${encodeURIComponent(hireId)}/external-config`,
      payload,
    )
  },

  /**
   * 上传 TODO 资料文件（仅支持 md / json）到 wwwroot/resources/todo-files/{sessionId}/{folder?}/
   * 由 MCP 工具 hiring.parse_uploaded_files 读取并交给大模型解析。
   */
  async uploadTodoFiles(
    sessionId: string,
    files: File[],
    folder?: string,
  ): Promise<Array<{ relativePath: string; sizeBytes: number; format: string }>> {
    const path = `/api/v1/hiring-todos/${encodeURIComponent(sessionId)}/files/upload`
    const url = buildUrl(path)
    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    if (folder && folder.trim()) form.append('folder', folder.trim())
    for (const f of files) form.append('files', f, f.name)

    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      body: form,
    })
    const text = await response.text()
    if (!response.ok) {
      try {
        const p = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(p.message?.trim() || `上传失败（HTTP ${response.status}）`, response.status, p.code, p)
      } catch {
        throw new ApiClientError(`上传失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }
    const env = JSON.parse(text) as ApiResponseEnvelope<Array<{ relativePath: string; sizeBytes: number; format: string }>>
    return env?.data ?? []
  },

  /** 列出已上传的 TODO 资料文件（仅元信息）。*/
  async listTodoFiles(sessionId: string): Promise<Array<{ relativePath: string; sizeBytes: number; format: string }>> {
    const path = `/api/v1/hiring-todos/${encodeURIComponent(sessionId)}/files`
    return httpClient.get<Array<{ relativePath: string; sizeBytes: number; format: string }>>(path)
  },

  /**
   * 上传资料阶段文件：文件内容落盘，元数据绑定 hireId + sessionId 入库。
   */
  async uploadMaterialFiles(
    hireId: string,
    sessionId: string,
    files: File[],
    options?: {
      folder?: string
      requestedCategoryTitle?: string
    },
  ): Promise<HiringMaterialFile[]> {
    const url = buildUrl(`/api/v1/hirings/${encodeURIComponent(hireId)}/material-files/upload`)
    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    form.append('session_id', sessionId)
    if (options?.folder?.trim()) form.append('folder', options.folder.trim())
    if (options?.requestedCategoryTitle?.trim()) {
      form.append('requested_category_title', options.requestedCategoryTitle.trim())
    }
    for (const f of files) form.append('files', f, f.name)

    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      body: form,
    })
    const text = await response.text()
    if (!response.ok) {
      try {
        const p = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(p.message?.trim() || `上传失败（HTTP ${response.status}）`, response.status, p.code, p)
      } catch {
        throw new ApiClientError(`上传失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }

    const env = JSON.parse(text) as ApiResponseEnvelope<HiringMaterialFile[]>
    return env?.data ?? []
  },

  /** 从数据库列出当前雇佣会话已上传的资料阶段文件。*/
  async listMaterialFiles(hireId: string, sessionId: string): Promise<HiringMaterialFile[]> {
    const query = new URLSearchParams({ session_id: sessionId })
    return httpClient.get<HiringMaterialFile[]>(
      `/api/v1/hirings/${encodeURIComponent(hireId)}/material-files?${query.toString()}`,
    )
  },

  /**
   * 上传模板包 ZIP 到雇佣沙箱工作区（走后端中转，不直接访问 gateway）。
   * 返回沙箱内文件路径和可嵌入 WS 消息的 [FILE_URL:...] 标记。
   */
  async uploadTemplatePackage(
    hireId: string,
    packageFile: File,
  ): Promise<{
    workspaceDir: string
    fileName: string
    workspacePath: string
    fileMarker: string
    sizeBytes: number
  }> {
    const url = buildUrl(`/api/v1/hirings/${encodeURIComponent(hireId)}/template-package`)
    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    form.append('templatePackage', packageFile, packageFile.name)
    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      body: form,
    })
    const text = await response.text()
    if (!response.ok) {
      try {
        const p = JSON.parse(text) as Partial<ApiResponseEnvelope<unknown>>
        throw new ApiClientError(p.message?.trim() || `模板包上传失败（HTTP ${response.status}）`, response.status, p.code, p)
      } catch {
        throw new ApiClientError(`模板包上传失败（HTTP ${response.status}）`, response.status, undefined, text)
      }
    }
    const env = JSON.parse(text) as ApiResponseEnvelope<{
      workspaceDir: string
      fileName: string
      workspacePath: string
      fileMarker: string
      sizeBytes: number
    }>
    if (!env.data) {
      throw new ApiClientError('模板包上传响应数据为空', response.status, env.code, env)
    }
    return env.data
  },
}


