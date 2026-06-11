import { ApiClientError } from '../httpClient'
import { httpClient } from '../httpClient'
import { tokenService } from '@/infra/auth/token-service'
import type { HiringConversationMessage } from './hiringWorkflowApi'

export interface CreatorRef {
  username?: string
  displayName?: string
  familyName?: string
  givenName?: string
}

export interface EmployeeCapability {
  name: string
  ready: boolean
}

export interface EmployeeSummary {
  employeeId: string
  nickname: string
  roleName: string
  sourceTemplate: string
  sourceTemplateId: string
  instanceType: 'department' | 'personal_clone' | 'private_branch'
  status: 'hiring' | 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'
  basedOnTemplateId?: string | null
  fromInstanceId?: string | null
  ownerUserId: string
  departmentId: string
  lifecycleStatus: string
  stageSummary: string
  primarySignal: string
  signalLevel: string
  owningTeam: string
  createdAt: string
  tasksDone: number
  tasksTotal: number
  pendingActions: string[]
  isConfigured: boolean
  cardIntro?: string | null
  description?: string | null
  createdBy?: CreatorRef | null
}

export interface EmployeeDetail extends EmployeeSummary {
  internshipStartAt?: string | null
  graduatedAt?: string | null
  satisfactionScore?: number | null
  capabilities: EmployeeCapability[]
  evalPhase?: string | null
  evalIteration?: number | null
  evalMaxIterations?: number | null
  sandboxId?: string | null
  gatewayEndpoint?: string | null
  sandboxStatus?: string | null
  /** 最新评估报告摘要，由详情接口实时查询，可用于直接展示评分或作为决策依据 */
  latestReport?: EvaluationReportSummary | null
}

export interface UpdateEmployeeLifecycleRequest {
  status?: 'hiring' | 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'
  lifecycleStatus?: string
  stageSummary?: string
  primarySignal?: string
  signalLevel?: string
  internshipStartAt?: string
  graduatedAt?: string
}

export interface CreatePersonalCloneRequest {
  displayName: string
  displayAvatar?: string | null
  displayDescription?: string | null
}

export interface CreatePrivateBranchRequest {
  displayName: string
  displayDescription?: string | null
  selectedStations: Array<'persona' | 'knowledge' | 'ability' | 'external'>
}

export interface PrivateBranchResult {
  branchId: string
  displayName: string
  status: string
  fromInstanceId: string
  imRoutingSwitched: boolean
}

export interface InstanceChatMessage {
  messageId: string
  role: 'user' | 'assistant' | string
  content: string
  createdAt: string
}

export type ImPlatformId = 'feishu' | 'dingtalk' | 'wecom'
export type ImConnectionMode = 'websocket' | 'url_callback'

export interface ImConfigRequest {
  connectionMode: ImConnectionMode
  appId?: string
  appSecret?: string
  appKey?: string
  appIdRef?: string
  appKeyRef?: string
  appSecretRef?: string
  encryptKey?: string
  token?: string
  aesKey?: string
  verificationToken?: string
  corpId?: string
  agentId?: string
  agentSecret?: string
  botId?: string
  botSecret?: string
  robotCode?: string
  robotCodeRef?: string
  groupPolicy?: string
  allowedFromUserIds?: string
  allowedGroupIds?: string
  maxInboundChars?: string
  requireMentionInGroup?: string
  exposeInboundMediaUrls?: string
  streamPollIntervalMs?: string
}

export interface ImConfigResult {
  platform: string
  connectionMode: ImConnectionMode
  status: string
  message: string
  configuredAt?: string | null
}

export interface FeishuChannelEffectiveConfig {
  status?: string | null
  connectionMode?: ImConnectionMode | null
  webhookPath?: string | null
  configuredAt?: string | null
  lastError?: string | null
  appId?: string | null
  appSecret?: string | null
  appIdRef?: string | null
  appSecretRef?: string | null
}

export interface DingTalkChannelEffectiveConfig {
  enabled?: boolean
  appId?: string | null
  appIdRef?: string | null
  appKey?: string | null
  appKeyRef?: string | null
  appSecret?: string | null
  appSecretRef?: string | null
  robotCode?: string | null
  robotCodeRef?: string | null
  groupPolicy?: string | null
  allowedFromUserIds?: string[] | null
  allowedGroupIds?: string[] | null
  maxInboundChars?: number | null
  requireMentionInGroup?: boolean | null
  exposeInboundMediaUrls?: boolean | null
  streamPollIntervalMs?: number | null
}

export interface WeComChannelEffectiveConfig {
  enabled?: boolean
  botId?: string | null
  botIdRef?: string | null
  botSecret?: string | null
  botSecretRef?: string | null
}

export interface DingTalkChannelConfigRequest {
  enabled: boolean
  appId?: string | null
  appIdRef?: string | null
  appKey?: string | null
  appKeyRef?: string | null
  appSecret?: string | null
  appSecretRef?: string | null
  robotCode?: string | null
  robotCodeRef?: string | null
  groupPolicy?: string | null
  allowedFromUserIds?: string[] | null
  allowedGroupIds?: string[] | null
  maxInboundChars?: number | null
  requireMentionInGroup?: boolean | null
  exposeInboundMediaUrls?: boolean | null
  streamPollIntervalMs?: number | null
}

export interface EvaluationScenario {
  scenarioId: string
  scenarioName: string
  status: string
  verdict?: string | null
  verdictComment?: string | null
  messageCount: number
  startedAt: string
  completedAt?: string | null
}

export interface EvaluationReadiness {
  testcasesReady: boolean
  ontologyReady: boolean
  status: string
  message?: string | null
  recommendedAction?: string | null
}

export interface EvaluationQuestionCard {
  testcaseId: string
  title: string
  prompt: string
  scoringHint: string
  steps: string[]
  sourceFile: string
}

export interface EvaluationReportSummary {
  reportId: string
  iteration: number
  overallScore: number
  passed: boolean
  reportJsonUrl: string
  reportHtmlUrl?: string | null
  createdAtUtc: string
  dimensionScores?: EvaluationDimensionScore[] | null
}

export interface EvaluationAssetRef {
  assetType: string
  relatedKey: string
  relativePath: string
  publicUrl: string
  createdAtUtc: string
}

export interface EvaluationTestcaseOutline {
  testcaseId: string
  title: string
  userRequest: string
}

export interface EvaluationState {
  employeeId: string
  overallStatus: string
  scenarios: EvaluationScenario[]
  recommendation: string
  sessionId?: string | null
  readiness?: EvaluationReadiness | null
  questionCards?: EvaluationQuestionCard[] | null
  latestReport?: EvaluationReportSummary | null
  assetRefs?: EvaluationAssetRef[] | null
  testcaseOutlines?: EvaluationTestcaseOutline[] | null
}


export interface EvaluationSandboxConversationState {
  employeeId: string
  evalPhase: string
  targetHireId: string
  targetRuntimeId: string
  targetSandboxId: string
  evaluatorHireId: string
  evaluatorRuntimeId: string
  evaluatorSandboxId: string
  sessionId: string
  skillLoadedAtUtc?: string | null
  messages: HiringConversationMessage[]
  questionCards?: EvaluationQuestionCard[] | null
}

export interface EvaluationSandboxMessageRequest {
  content: string
  structuredAnswers?: Record<string, string>
  skillRootPath?: string
}
export interface EvaluationOnboardingDecisionRequest {
  decision: 'ONBOARD' | 'REJECT' | 'FORCE'
  comment?: string
}

export interface AiEvaluationDecisionRequest {
  decision: 'START' | 'LOAD_SKILL' | 'RUN' | 'PASS' | 'FAIL'
  comment?: string
}

export interface EvaluationSandboxConnectionResult {
  gatewayEndpoint: string
  sandboxToken: string
  evaluatorSandboxId: string
  targetSandboxId: string
  sessionId: string
  targetHireId: string
  targetGatewayEndpoint: string
  evaluationPayloadJson?: string | null
}

export interface EvaluationDimensionScore {
  dimension: string
  score: number
  comment: string
  evidenceRefs: string[]
}

export interface EvaluationVerdictPayload {
  verdict: string
  overallScore: number
  summary: string
  dimensionScores: EvaluationDimensionScore[]
}

export interface EvaluationVerdictSyncRequest {
  sessionId: string
  verdict: EvaluationVerdictPayload
}


export interface EvaluationTraceContentResult {
  sessionId: string
  assetId: string
  traceJsonUrl: string
  traceJsonContent: string
}

export interface EvaluationVerdictSyncResult {
  employeeId: string
  sessionId: string
  passed: boolean
  overallScore: number
  summary: string
  status: string
  latestReport?: EvaluationReportSummary | null
}

export interface EvaluationWorkspaceStep {
  step: string
  status: 'pending' | 'running' | 'completed' | 'failed'
  detail?: string | null
}

export interface EvaluationWorkspaceStatus {
  employeeId: string
  overallStatus: 'not_started' | 'creating' | 'ready' | 'failed'
  targetSandboxId?: string | null
  evaluatorSandboxId?: string | null
  evaluatorRuntimeId?: string | null
  targetRuntimeId?: string | null
  sessionId?: string | null
  targetGatewayEndpoint?: string | null
  evaluatorGatewayEndpoint?: string | null
  steps: EvaluationWorkspaceStep[]
  errorMessage?: string | null
}

export const employeeRuntimeApi = {
  getEmployees() {
    return httpClient.get<EmployeeSummary[]>('/api/v1/employees')
  },

  getMyEmployees() {
    return httpClient.get<EmployeeSummary[]>('/api/v1/employees/mine')
  },

  getDepartmentEmployees() {
    return httpClient.get<EmployeeSummary[]>('/api/v1/employees/department')
  },

  deleteEmployee(employeeId: string) {
    return httpClient.delete<boolean>(`/api/v1/employees/${encodeURIComponent(employeeId)}`)
  },

  getEmployee(employeeId: string) {
    return httpClient.get<EmployeeDetail>(`/api/v1/employees/${employeeId}`)
  },

  getSandboxGatewayEndpoint(employeeId: string) {
    return httpClient.get<string>(`/api/v1/employees/${employeeId}/sandbox/gateway-endpoint`)
  },

  createPersonalClone(employeeId: string, payload: CreatePersonalCloneRequest) {
    return httpClient.post<EmployeeDetail, CreatePersonalCloneRequest>(
      `/api/v1/employees/${employeeId}/personal-clones`,
      payload,
    )
  },

  createPrivateBranch(sourceInstanceId: string, payload: CreatePrivateBranchRequest) {
    return httpClient.post<PrivateBranchResult, CreatePrivateBranchRequest>(
      `/api/v1/instances/${sourceInstanceId}/private-branch`,
      payload,
    )
  },

  abandonPrivateBranch(branchId: string) {
    return httpClient.post<EmployeeDetail>(`/api/v1/instances/${branchId}/abandon-branch`)
  },

  updateLifecycle(employeeId: string, payload: UpdateEmployeeLifecycleRequest) {
    return httpClient.post<EmployeeDetail, UpdateEmployeeLifecycleRequest>(
      `/api/v1/employees/${employeeId}/lifecycle`,
      payload,
    )
  },

  rehire(employeeId: string) {
    return httpClient.post<EmployeeDetail>(`/api/v1/employees/${employeeId}/rehire`)
  },

  getFeishuEffectiveImConfig(instanceId: string) {
    return httpClient.get<FeishuChannelEffectiveConfig>(`/api/v1/instances/${instanceId}/im-config/feishu/effective`)
  },

  getDingTalkEffectiveImConfig(instanceId: string) {
    return httpClient.get<DingTalkChannelEffectiveConfig>(`/api/v1/instances/${instanceId}/im-config/dingtalk/effective`)
  },

  getWeComEffectiveImConfig(instanceId: string) {
    return httpClient.get<WeComChannelEffectiveConfig>(`/api/v1/instances/${instanceId}/im-config/wecom/effective`)
  },

  updateDingTalkImConfig(instanceId: string, payload: DingTalkChannelConfigRequest) {
    return httpClient.put<ImConfigResult, DingTalkChannelConfigRequest>(
      `/api/v1/instances/${instanceId}/im-config/dingtalk`,
      payload,
    )
  },

  upsertImConfig(instanceId: string, platform: ImPlatformId, payload: ImConfigRequest) {
    return httpClient.put<ImConfigResult, ImConfigRequest>(
      `/api/v1/instances/${instanceId}/im-config/${platform}`,
      payload,
    )
  },

  deleteImConfig(instanceId: string, platform: ImPlatformId) {
    return httpClient.delete<boolean>(`/api/v1/instances/${instanceId}/im-config/${platform}`)
  },

  deleteDingTalkImConfig(instanceId: string) {
    return httpClient.delete<boolean>(`/api/v1/instances/${instanceId}/im-config/dingtalk`)
  },

  getEvaluationState(employeeId: string) {
    return httpClient.get<EvaluationState>(`/api/v1/employees/${employeeId}/evaluation/state`)
  },

  getEvaluationSandboxConversation(employeeId: string, since?: string | null) {
    const params = since ? `?since=${encodeURIComponent(since)}` : ''
    return httpClient.get<EvaluationSandboxConversationState>(
      `/api/v1/employees/${employeeId}/evaluation/sandbox/conversation${params}`,
    )
  },

  sendEvaluationSandboxMessage(employeeId: string, payload: EvaluationSandboxMessageRequest) {
    return httpClient.post<EvaluationSandboxConversationState, EvaluationSandboxMessageRequest>(
      `/api/v1/employees/${employeeId}/evaluation/sandbox/messages`,
      payload,
    )
  },

  submitAiEvaluationDecision(employeeId: string, payload: AiEvaluationDecisionRequest) {
    return httpClient.post<EmployeeDetail, AiEvaluationDecisionRequest>(
      `/api/v1/employees/${employeeId}/evaluation/ai-decision`,
      payload,
    )
  },

  submitOnboardingDecision(employeeId: string, payload: EvaluationOnboardingDecisionRequest) {
    return httpClient.post<EmployeeDetail, EvaluationOnboardingDecisionRequest>(
      `/api/v1/employees/${employeeId}/evaluation/onboarding-decision`,
      payload,
    )
  },

  getEvaluationWorkspaceStatus(employeeId: string) {
    return httpClient.get<EvaluationWorkspaceStatus>(
      `/api/v1/employees/${employeeId}/evaluation/workspace-status`,
    )
  },

  getSandboxConnection(employeeId: string) {
    return httpClient.get<EvaluationSandboxConnectionResult>(
      `/api/v1/employees/${employeeId}/evaluation/sandbox-connection`,
    )
  },

  syncVerdict(employeeId: string, payload: EvaluationVerdictSyncRequest) {
    return httpClient.post<EvaluationVerdictSyncResult, EvaluationVerdictSyncRequest>(
      `/api/v1/employees/${employeeId}/evaluation/sync-verdict`,
      payload,
    )
  },

  resetEvaluationData(employeeId: string) {
    return httpClient.delete<object>(`/api/v1/employees/${employeeId}/evaluation/data`)
  },

  /** 获取评估报告文件下载 URL（需携带 Bearer Token 访问，由 handleReportAction 使用） */
  getReportFileDownloadUrl(employeeId: string, reportId: string, fileType: 'json' | 'html'): string {
    return `/api/v1/employees/${encodeURIComponent(employeeId)}/evaluation/reports/${encodeURIComponent(reportId)}/files/${fileType}`
  },

  getTraceContent(employeeId: string, sessionId: string) {
    return httpClient.get<EvaluationTraceContentResult>(
      `/api/v1/employees/${employeeId}/evaluation/trace-content?sessionId=${encodeURIComponent(sessionId)}`,
    )
  },

  async quickCreateFromTemplate(file: File): Promise<EmployeeDetail> {
    const path = '/api/v1/employees/quick-create'
    const locationBase =
      typeof window !== 'undefined' ? window.location.origin : 'http://localhost'
    const baseUrl =
      (typeof window !== 'undefined' && window.__AUTH_CONFIG__?.ApiBase !== undefined
        ? window.__AUTH_CONFIG__.ApiBase
        : undefined) ??
      (import.meta.env.VITE_API_BASE_URL as string | undefined) ??
      'http://localhost:5280'
    const url = new URL(`${baseUrl}${path}`, locationBase).toString()

    const accessToken = await tokenService.ensureFresh()
    const form = new FormData()
    form.append('templatePackage', file)

    const response = await fetch(url, {
      method: 'POST',
      headers: accessToken
        ? { Authorization: `Bearer ${accessToken}` }
        : undefined,
      body: form,
    })

    const text = await response.text()
    if (!response.ok) {
      try {
        const payload = JSON.parse(text) as Partial<{
          code: number
          success: boolean
          message: string
          data: unknown
        }>
        throw new ApiClientError(
          payload.message?.trim() || `请求失败（HTTP ${response.status}）`,
          response.status,
          payload.code,
          payload,
        )
      } catch (err) {
        if (err instanceof ApiClientError) throw err
        throw new ApiClientError(
          `请求失败（HTTP ${response.status}）`,
          response.status,
          undefined,
          text,
        )
      }
    }

    const envelope = JSON.parse(text) as {
      code: number
      success: boolean
      message: string
      data: EmployeeDetail
    }
    if (!envelope?.data) {
      throw new ApiClientError(
        '上传返回数据为空',
        response.status,
        envelope?.code,
        envelope,
      )
    }

    return envelope.data
  },
}

