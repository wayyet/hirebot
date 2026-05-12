import { ApiClientError, httpClient } from '../httpClient'
import type { ApiResponseEnvelope } from '../types'
import { tokenService } from '@/infra/auth/token-service'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5280'

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

export const HiringDiagnosticStatus = {
  Pass: 'pass',
  Warning: 'warning',
  Blocked: 'blocked',
} as const

export const HiringStageReadinessStatus = {
  Missing: 'missing',
  Partial: 'partial',
  Complete: 'complete',
  Skipped: 'skipped',
} as const

export const HiringCredentialBindingStatus = {
  Pending: 'pending',
  Bound: 'bound',
  NotRequired: 'not_required',
  Failed: 'failed',
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

export type HiringTodoStatusType =
  typeof HiringTodoStatus[keyof typeof HiringTodoStatus]

export type HiringDiagnosticStatusType =
  typeof HiringDiagnosticStatus[keyof typeof HiringDiagnosticStatus]

export type HiringStageReadinessStatusType =
  typeof HiringStageReadinessStatus[keyof typeof HiringStageReadinessStatus]

export type HiringCredentialBindingStatusType =
  typeof HiringCredentialBindingStatus[keyof typeof HiringCredentialBindingStatus]

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

export interface CredentialSlot {
  credentialSlot: string
  secretRef?: string | null
  authKind?: string | null
  targetSystem?: string | null
  todoId?: string | null
  bindingStatus: string
  updatedAtUtc: string
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

export interface HiringConversationControlResult {
  hireId: string
  currentStage: string
  collectionPhase: string
  isConversationPaused: boolean
  isConversationResponding: boolean
}

export interface HiringConversationTimeline {
  hireId: string
  sessionId: string
  currentStage: string
  requiresAudit: boolean
  collectionPhase: string
  messages: HiringConversationMessage[]
  stageSkills: StageSkillMapping[]
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
  credentialSlots?: CredentialSlot[] | null
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
  return `${API_BASE_URL}${path}`
}

function buildArtifactFileDownloadUrl(hireId: string, artifactName: string): string {
  const safeArtifactPath = artifactName
    .split('/')
    .map(segment => encodeURIComponent(segment))
    .join('/')
  const path = `/api/v1/hirings/${hireId}/artifacts/${safeArtifactPath}`
  return `${API_BASE_URL}${path}`
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

  pauseConversation(hireId: string) {
    return httpClient.post<HiringConversationControlResult>(`/api/v1/hirings/${hireId}/conversation/pause`)
  },

  resumeConversation(hireId: string) {
    return httpClient.post<HiringConversationControlResult>(`/api/v1/hirings/${hireId}/conversation/resume`)
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
    const url = `${API_BASE_URL}${path}`
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

  getConversationTimeline(hireId: string) {
    return httpClient.get<HiringConversationTimeline>(`/api/v1/hirings/${hireId}/conversation/messages`)
  },

  getStagePreview(hireId: string, stage?: string) {
    return httpClient.get<HiringStagePreview>(`/api/v1/hirings/${hireId}/stage-preview`, { stage })
  },

  submitAuditDecision(hireId: string, payload: HiringAuditDecisionRequest) {
    return httpClient.post<HiringAuditDecisionResult, HiringAuditDecisionRequest>(
      `/api/v1/hirings/${hireId}/audit-decisions`,
      payload,
    )
  },

  getAuditLogs(hireId: string) {
    return httpClient.get<HiringAuditLog[]>(`/api/v1/hirings/${hireId}/audit-logs`)
  },

  finalize(hireId: string) {
    return httpClient.post<HiringFinalizeResult>(`/api/v1/hirings/${hireId}/finalize`)
  },

  getArtifactsDownloadUrl(hireId: string) {
    return buildArtifactsDownloadUrl(hireId)
  },

  getArtifactFileDownloadUrl(hireId: string, artifactName: string) {
    return buildArtifactFileDownloadUrl(hireId, artifactName)
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
   */
  async importPackage(hireId: string, packageBlob: Blob, fileName: string): Promise<HiringFinalizeResult> {
    const url = `${API_BASE_URL}/api/v1/hirings/${encodeURIComponent(hireId)}/import-package`
    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    form.append('packageFile', packageBlob, fileName)
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
}


