import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'

import { api, HiringAuditDecision, HiringCollectionStage } from '@/infra/api'
import type {
  HiringCollectionStageType,
  HiringExternalSystemConfig,
} from '@/infra/api'
import { GatewayWs, type GatewayMessage } from '@/infra/sandbox/gateway-ws'
import { resolveGatewayEndpoint } from '@/infra/sandbox/sandbox-config'
import {
  fetchLatestGatewaySession,
  fetchSandboxSessionMessages,
  uploadMediaToGateway,
  uploadWorkspaceFileToGateway,
} from '@/infra/sandbox/sandbox-api'
import { inferGatewayProtocol } from '@/infra/sandbox/sandbox-utils'
import { tokenService } from '@/infra/auth/token-service'
import { TYPEWRITER_SOFT_FINISH_DEFER_MS, useTypewriterStream } from '@/shared/hooks/useTypewriterStream'

import { useHiringState } from './hooks/useHiringState'
import { useHiringComputed } from './hooks/useHiringComputed'
import {
  useScrollToBottom,
  useBodyClassAndCleanup,
  useTemplateDetail,
  useSyncMessagesRef,
  useRuntimeStateSync,
  useAutoFocusStage,
} from './hooks/useHiringEffects'
import {
  mkId,
  sleep,
  normalizeErrorMessage,
  normalizeAssistantReply,
  normalizeAssistantStreamingPreview,
} from './utils/hiringPageHelpers'
import {
  EXTERNAL_CONFIG_REPACKAGE_NOTICE,
  downloadBlob,
  fileToChatFile,
  toConversationMaterials,
  normalizeCollectionStage,
  formatFileSize,
} from './utils/hiringFileUtils'
import {
  type CachedStageOverride,
  hasPendingRequiredDownstreamRuns,
} from './utils/hiringCacheNormalizers'
import {
  buildProjectionPassPayload,
  buildSkillGenerationPayload,
  buildSkillDefinitionConfirmationPrompt,
  buildDownstreamPrompt,
  buildPackagingRequestPrompt,
  isPackagingTestCasesApprovalMessage,
  isPackagingTestCasesSkipMessage,
  resolveActiveSkillStageRun,
  resolvePackagingRequestRoute,
  resolveSkillStageApprovalRoute,
} from './utils/hiringDownstreamTriggers'
import { RUNTIME_STATE_STAGE_SEQUENCE } from './utils/hiringRuntimeState'
import { HiringConversationPanel } from './components/HiringConversationPanel'
import { HiringJourneyHeader } from './components/HiringJourneyHeader'
import { HiringProgressLedger } from './components/HiringProgressLedger'
import { HiringTodoPanel } from './components/HiringTodoPanel'
import { HiringStagePills } from './components/HiringStagePills'
import { SkillUploadModal } from './components/SkillUploadModal'
import type {
  ArtifactDisplayData,
  ChatFile,
  ChatMessage,
  DownstreamRunKey,
  DownstreamRunsSnapshot,
  DownstreamRunState,
  DownstreamRunStatus,
  SkillUploadPayload,
  StageGateData,
  ToolStep,
} from './hiringPageTypes'
import {
  buildCoachResumePrompt,
  buildHistoricalHiringConversationState,
  deriveStageOverridesFromDownstreamRuns,
  extractArtifactFromToolCall,
  normalizeArtifactDisplayData,
  resolveDownstreamRunFromArtifact,
  resolveHiringStageFromWs,
  shouldSuppressStageGate,
} from './hiringArtifactState'
import {
  buildExternalConfigCommittedArtifact as createExternalConfigCommittedArtifact,
  buildExternalConfigCommittedSandboxPrompt,
  buildExternalConfigCommittedSignature,
  buildPackagingTestCasesReadyArtifact,
  buildPackagingTestCasesReadySignature,
  isDuplicateExternalConfigCommittedArtifact,
  tryBuildExternalConfigCommittedSignature,
} from './externalConfigCommitted'
import {
  type ExternalConfigChangeSource,
  shouldRequireFreshPackagingAfterExternalConfigChange,
} from './externalPackagingState'
import {
  buildPendingStageAdvanceConfirmation,
  shouldRequireStageAdvanceConfirmation,
  type StageAdvanceIntent,
} from './stageAdvanceConfirmation'
import { type HiringUiStage } from './hiringWorkflowViewModel'
import {
  extractLatestMaterialRequestedCategories,
  normalizeMaterialRequestedCategories,
} from './materialRequestedCategories'
import {
  getBlockedIncomingArtifactReason,
  normalizeIncomingArtifactTerminal,
  shouldDisplayArtifactInConversation,
} from './hiringArtifactGuards'

function buildArtifactEventSignature(artifact: ArtifactDisplayData): string {
  return JSON.stringify({
    kind: artifact.kind,
    artifactType: artifact.artifactType,
    label: artifact.label,
    skillName: artifact.skillName,
    stage: artifact.stage,
    isTerminal: artifact.isTerminal,
    fileUrl: artifact.fileUrl,
    fileName: artifact.fileName,
    data: artifact.data,
  })
}

/**
 * 从 emit_artifact 的 tool_result.text 中解析 artifact 元数据。
 * 当 tool_result 消息缺少 arguments 字段时（Gateway 版本差异或消息乱序），
 * text 中会携带 `Data artifact emitted: [key=value ...]` 格式的结构化描述，
 * 作为 artifact 提取的回退路径，避免 skill_generation_done / external_workorder_summary
 * 等终态 artifact 静默丢失导致阶段无法推进。
 *
 * 示例输入：
 *   "Data artifact emitted: [kind=data type=skill_generation_done stage=stage2_skill terminal=True]"
 */
function parseArtifactFromToolResultText(text: string): Record<string, unknown> | null {
  const bracketMatch = /Data artifact emitted:\s*\[([^\]]*)\]/.exec(text)
  if (!bracketMatch) return null

  const inner = bracketMatch[1].trim()
  if (!inner) return null

  const result: Record<string, unknown> = {}
  const pairRegex = /(\w+)=(\S+)/g
  let pairMatch: RegExpExecArray | null
  while ((pairMatch = pairRegex.exec(inner)) !== null) {
    const key = pairMatch[1]
    const value = pairMatch[2]

    switch (key) {
      case 'type':
        result.artifactType = value
        break
      case 'kind':
        result.kind = value
        break
      case 'stage':
        result.stage = value
        break
      case 'terminal':
        result.isTerminal = value.toLowerCase() === 'true'
        break
      default:
        // 保留其他元数据字段（如 skillName、displayHint 等未来扩展）
        result[key] = value
        break
    }
  }

  if (!result.artifactType) return null
  return result
}

export default function HiringPage() {
  const { templateId } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation()

  // ── 状态管理（使用自定义 Hook） ────────────────────────────────────────────
  const [state, actions] = useHiringState()
  const {
    template,
    templateLoading,
    templateError,
    messages,
    typing,
    input,
    pendingFiles,
    allFiles,
    showSkillUploadModal,
    journeyGuideVisible,
    focusedStage,
    instanceCreated,
    createdId,
    workflowHireId,
    workflowBooting,
    workflowError,
    workflowNotice,
    workflowInitAttempted,
    artifactFileNames,
    materialRequestedCategories,
    pendingPackageArtifact,
    pendingStageConfirmation,
    requiresFreshPackaging,
    linkedStoreSkillIds,
    latestSkillSummary,
    submittingMessage,
    streamingTurnInternal,
    resetting,
    wsStageOverrides,
    downstreamRuns,
  } = state
  const {
    setTemplate,
    setTemplateLoading,
    setTemplateError,
    setMessages,
    setTyping,
    setInput,
    setPendingFiles,
    setAllFiles,
    setShowSkillUploadModal,
    setJourneyGuideVisible,
    setFocusedStage,
    setInstanceCreated,
    setCreatedId,
    setWorkflowHireId,
    setWorkflowBooting,
    setWorkflowError,
    setWorkflowNotice,
    setWorkflowInitAttempted,
    setArtifactFileNames,
    setMaterialRequestedCategories,
    setPendingPackageArtifact,
    setPendingStageConfirmation,
    setRequiresFreshPackaging,
    setLinkedStoreSkillIds,
    setLatestSkillSummary,
    setSubmittingMessage,
    setStreamingTurnInternal,
    setResetting,
    setWsStageOverrides,
    setDownstreamRuns,
  } = actions

  // ── Typewriter 流式效果 ────────────────────────────────────────────────────
  const typewriterStream = useTypewriterStream()
  const streamingContent = typewriterStream.displayText
  const visibleStreamingContent = streamingTurnInternal || streamingContent === null
    ? null
    : normalizeAssistantStreamingPreview(streamingContent)
  const visibleTyping = typing

  // ── 计算属性（使用自定义 Hook） ────────────────────────────────────────────
  const computed = useHiringComputed({
    messages,
    wsStageOverrides,
    downstreamRuns,
    latestSkillSummary,
    focusedStage,
    t,
    templateName: template?.name,
    workflowHireId,
    instanceCreated,
    typing,
    workflowBooting,
    submittingMessage,
    resetting,
    allFiles,
    pendingPackageArtifact,
  })
  const {
    uiStageOverrides,
    definedSkills,
    finalPackageFileName,
    hasTemplatePackageArtifact,
    uploadedConversationFiles,
    uploadedFileCount,
    isInteractionLocked,
    canCreate,
    canDownloadFinalPackage,
    viewModel,
    mergedStepPills,
    mergedActionState,
  } = computed

  // ── Refs（保持独立，不纳入状态管理） ───────────────────────────────────────
  const messagesRef = useRef<ChatMessage[]>([])
  const pendingToolStepsRef = useRef<ToolStep[]>([])
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([])
  const resettingRef = useRef(false)
  const downstreamRunsRef = useRef<DownstreamRunsSnapshot>({})
  const latestMaterialSummaryRef = useRef<unknown>(null)
  const latestSkillSummaryRef = useRef<unknown>(null)
  const latestProjectionResultRef = useRef<unknown>(null)
  const latestExternalSummaryRef = useRef<unknown>(null)
  const latestReviewReportRef = useRef<unknown>(null)
  const materialSummarySignatureRef = useRef('')
  const skillSummarySignatureRef = useRef('')
  const externalSummarySignatureRef = useRef('')
  const ontologyExtractionDoneSignatureRef = useRef('')
  const ontologyProjectionDoneSignatureRef = useRef('')
  const projectionPassLaunchSignatureRef = useRef('')
  const skillGenerationLaunchSignatureRef = useRef('')
  const packagingTestCasesReadySignatureRef = useRef('')
  const packagingTestCasesDoneSignatureRef = useRef('')
  const packagingTestCasesLaunchSignatureRef = useRef('')
  const processedArtifactSignaturesRef = useRef<Set<string>>(new Set())
  const pendingInternalPromptsRef = useRef<string[]>([])

  const fileRef = useRef<HTMLInputElement>(null)
  const composerRef = useRef<HTMLTextAreaElement>(null)
  const chatEndRef = useRef<HTMLDivElement>(null)
  const workflowInitRef = useRef<Promise<string | null> | null>(null)
  // workflowHireId 的 ref 镜像：connectSandboxWs 内的 ws.onMessage 闭包在创建时捕获的
  // workflowHireId state 仍是空字符串（React state 异步更新），后续通过 ref 获取最新值，
  // 防止 ensureWorkflowReady / typing_stop 里误判未初始化而重新触发初始化流程。
  const workflowHireIdRef = useRef<string>('')
  const messageSubmitRef = useRef(false)
  const handleSendRef = useRef(false)
  // 沙箱直连引用：WebSocket 实例、网关端点、会话 ID
  const wsRef = useRef<GatewayWs | null>(null)
  const gatewayEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  // 记录最近一次通过 WS 发送的用户消息，用于同步端点回传
  const lastWsUserMessageRef = useRef<string>('')
  // 记录最近一次 WS 发送时的附件材料
  const lastWsMaterialsRef = useRef<ReturnType<typeof toConversationMaterials> | undefined>(undefined)
  const lastWsTurnInternalRef = useRef(false)
  /** 等待本轮 conversation/sync 完成后再 import-package，避免 final 包早于 testcase staging */
  const turnSyncBarrierRef = useRef<Promise<void>>(Promise.resolve())
  const packagingInProgressRef = useRef(false)
  const packageImportInFlightRef = useRef(false)
  const internalPromptFlushInFlightRef = useRef(false)
  const internalPromptFlushRetryRef = useRef<number | null>(null)
  const typingRef = useRef(false)
  const postTurnHistoryRefreshRef = useRef<Promise<boolean> | null>(null)
  // 存储原始 File 对象，供 WS 路径上传到 Gateway 使用
  const rawFileMapRef = useRef<Map<string, File>>(new Map())
  // 避免同一会话重复触发“自动上传模板并引导”
  const autoTemplateBootstrapSessionRef = useRef<string | null>(null)
  const latestExternalConfigRef = useRef<HiringExternalSystemConfig | null>(null)
  const externalConfigCommittedSignatureRef = useRef('')

  /**
   * 将外部系统配置提交事件以 artifact 形式追加到本地消息列表，
   * 并在需要时向沙箱网关同步一条内部信号，告知 AI 引擎外部配置已完成。
   *
   * - sendToSandbox=true：用户在 UI 上真正完成/跳过配置后调用，需要把信号发送到沙箱以推动流程；
   * - sendToSandbox=false（默认）：历史会话恢复等场景，沙箱已经知晓该状态，避免重复发送。
   */
  function appendExternalConfigCommittedArtifact(
    config: HiringExternalSystemConfig | null,
    options: { sendToSandbox?: boolean } = {},
  ) {
    const submissionMode = config?.submissionMode ?? 'pending'
    if (!config || (submissionMode !== 'configured' && submissionMode !== 'skipped')) {
      return
    }

    const signature = buildExternalConfigCommittedSignature(config)
    const duplicateCommitted = externalConfigCommittedSignatureRef.current === signature
    if (!duplicateCommitted) {
      externalConfigCommittedSignatureRef.current = signature
      const artifact = createExternalConfigCommittedArtifact(config)
      setMessages(msgs => [...msgs, {
        id: mkId(),
        role: 'artifact',
        content: artifact.label ?? artifact.artifactType,
        artifact,
      }])

      if (options.sendToSandbox) {
        sendExternalConfigCommittedToSandbox(artifact)
      }
    }

    setWsStageOverrides(prev => {
      const next = new Map(prev)
      next.set(HiringCollectionStage.Material, 'completed')
      next.set(HiringCollectionStage.Skill, 'completed')
      next.set(HiringCollectionStage.External, 'completed')
      return next
    })
    appendPackagingTestCasesReadyArtifact(config)
  }

  function appendPackagingTestCasesReadyArtifact(config: HiringExternalSystemConfig) {
    const signature = buildPackagingTestCasesReadySignature(config)
    if (packagingTestCasesReadySignatureRef.current === signature) {
      return
    }

    if (messagesRef.current.some(message => message.artifact?.artifactType === 'packaging_testcases_ready')) {
      packagingTestCasesReadySignatureRef.current = signature
      return
    }

    const currentRun = downstreamRunsRef.current['packaging-test-cases']
    if (currentRun?.status === 'running' || currentRun?.status === 'completed') {
      return
    }

    packagingTestCasesReadySignatureRef.current = signature
    const artifact = buildPackagingTestCasesReadyArtifact(config)
    setMessages(msgs => [...msgs, {
      id: mkId(),
      role: 'artifact',
      content: artifact.label ?? artifact.artifactType,
      artifact,
    }])
    setDownstreamRuns(prev => {
      const next = {
        ...prev,
        'packaging-test-cases': {
          key: 'packaging-test-cases',
          status: 'waiting_confirm',
          artifactType: artifact.artifactType,
          label: artifact.label,
          displayHint: artifact.displayHint,
          updatedAt: new Date().toISOString(),
          data: artifact.data,
        } satisfies DownstreamRunState,
      }
      downstreamRunsRef.current = next
      return next
    })
  }

  /**
   * 将 external_config_committed artifact 通过 WebSocket 同步给沙箱网关。
   * 网关只支持 user_message / assistant_message 帧，因此采用 `[Internal ...]` 前缀的
   * 内部 user_message 携带 artifact 负载：历史回放路径会按该前缀过滤掉，避免污染聊天记录。
   */
  function sendExternalConfigCommittedToSandbox(artifact: ArtifactDisplayData) {
    const ws = wsRef.current
    const sessionId = sessionIdRef.current
    if (!ws || !sessionId || !ws.isOpen()) {
      return
    }

    const prompt = buildExternalConfigCommittedSandboxPrompt(artifact)
    lastWsUserMessageRef.current = prompt
    lastWsMaterialsRef.current = undefined
    lastWsTurnInternalRef.current = true
    ws.send({
      type: 'user_message',
      text: prompt,
      sessionId,
    })
  }

  function extractLatestMessageArtifactData(restoredMessages: ChatMessage[], artifactType: string): unknown | null {
    for (let index = restoredMessages.length - 1; index >= 0; index -= 1) {
      const artifact = restoredMessages[index].artifact
      if (artifact?.artifactType === artifactType) {
        return artifact.data ?? null
      }
    }

    return null
  }

  function syncArtifactStateFromMessages(
    sourceMessages: ChatMessage[],
    requestedCategories = extractLatestMaterialRequestedCategories(sourceMessages),
  ) {
    setMaterialRequestedCategories(requestedCategories)
    processedArtifactSignaturesRef.current = new Set(
      sourceMessages
        .map(message => message.artifact)
        .filter((artifact): artifact is ArtifactDisplayData => Boolean(artifact))
        .map(buildArtifactEventSignature),
    )

    latestMaterialSummaryRef.current = extractLatestMessageArtifactData(sourceMessages, 'material_handoff_summary')
    latestSkillSummaryRef.current = extractLatestMessageArtifactData(sourceMessages, 'skill_workorder_summary')
    setLatestSkillSummary(latestSkillSummaryRef.current)
    latestProjectionResultRef.current = extractLatestMessageArtifactData(sourceMessages, 'ontology_projection_done')
    latestExternalSummaryRef.current = extractLatestMessageArtifactData(sourceMessages, 'external_workorder_summary')
    latestReviewReportRef.current = extractLatestMessageArtifactData(sourceMessages, 'review_report')
    materialSummarySignatureRef.current = latestMaterialSummaryRef.current ? JSON.stringify(latestMaterialSummaryRef.current) : ''
    skillSummarySignatureRef.current = latestSkillSummaryRef.current ? JSON.stringify(latestSkillSummaryRef.current) : ''
    externalSummarySignatureRef.current = latestExternalSummaryRef.current ? JSON.stringify(latestExternalSummaryRef.current) : ''
    externalConfigCommittedSignatureRef.current = sourceMessages
      .filter(message => message.artifact?.artifactType === 'external_config_committed' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    ontologyExtractionDoneSignatureRef.current = sourceMessages
      .filter(message => message.artifact?.artifactType === 'ontology_slice_extraction_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    ontologyProjectionDoneSignatureRef.current = sourceMessages
      .filter(message => message.artifact?.artifactType === 'ontology_projection_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    packagingTestCasesDoneSignatureRef.current = sourceMessages
      .filter(message => message.artifact?.artifactType === 'packaging_testcases_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''

    appendExternalConfigCommittedArtifact(latestExternalConfigRef.current)
  }

  function applyRestoredMessages(
    restoredMessages: ChatMessage[],
    requestedCategories = extractLatestMaterialRequestedCategories(restoredMessages),
  ) {
    setMessages(restoredMessages)
    messagesRef.current = restoredMessages
    syncArtifactStateFromMessages(restoredMessages, requestedCategories)
  }

  // 从后端恢复运行时状态（stageOverrides、downstreamRuns、uploadedFiles、packageStructure）
  async function restoreRuntimeState(hireId: string): Promise<boolean> {
    try {
      let restored = false
      const restoredStageOverrides: CachedStageOverride[] = []
      let restoredDownstreamRuns: DownstreamRunsSnapshot | null = null

      // 恢复阶段覆盖配置
      for (const stage of RUNTIME_STATE_STAGE_SEQUENCE) {
        const state = await api.hiringWorkflow.getRuntimeStateByStage(hireId, stage)

        if (state.stageOverrides && Object.keys(state.stageOverrides).length > 0) {
          restoredStageOverrides.push(...Object.entries(state.stageOverrides) as CachedStageOverride[])
          restored = true
        }

      // 恢复下游运行记录
        if (state.downstreamRuns && Object.keys(state.downstreamRuns).length > 0) {
          restoredDownstreamRuns = { ...(restoredDownstreamRuns ?? downstreamRunsRef.current), ...state.downstreamRuns }
          restored = true
        }

      // 恢复对话上传文件列表（rawFile 丢失无影响，仅用于 MaterialCard 显示计数）
        if (state.uploadedFiles && state.uploadedFiles.length > 0) {
          const restoredFiles: ChatFile[] = state.uploadedFiles.map(f => ({
            id: f.id,
            name: f.name,
            size: f.size,
            status: f.status as ChatFile['status'],
            type: f.type as ChatFile['type'],
            mimeType: f.mimeType,
            metadata: f.metadata,
          }))
          setAllFiles(restoredFiles)
          restored = true
        }

      // 恢复最新数字员工包结构；文件名由当前模板名统一计算，不信任历史缓存名。
        if (state.packageStructure?.fileName) {
          setArtifactFileNames(state.packageStructure.fileNames ?? [])
        // 恢复员工实例 ID：如果包内储了 employeeId，则恢复评估入口
          if (state.packageStructure.employeeId) {
            setCreatedId(state.packageStructure.employeeId)
            setInstanceCreated(true)
          }
          restored = true
        }

      }

      if (restoredStageOverrides.length > 0) {
        setWsStageOverrides(prev => {
          const next = new Map(prev)
          for (const entry of restoredStageOverrides) {
            next.set(entry[0], entry[1])
          }
          return next
        })
      }

      if (restoredDownstreamRuns) {
        downstreamRunsRef.current = restoredDownstreamRuns
        setDownstreamRuns(restoredDownstreamRuns)
      }

      return restored
    } catch {
      return false
    }
  }

  async function restoreConversationFromSandboxHistory(
    endpoint: string,
    sessionId: string,
    mode: 'always' | 'if-longer' | 'merge-artifacts' = 'always',
  ): Promise<boolean> {
    const sandboxMessages = await fetchSandboxSessionMessages(endpoint, sessionId)
    if (sandboxMessages.length === 0) {
      return false
    }

    const restored = buildHistoricalHiringConversationState(sandboxMessages, normalizeAssistantReply)
    const hasUnseenArtifact = restored.messages
      .map(message => message.artifact)
      .filter((artifact): artifact is ArtifactDisplayData => Boolean(artifact))
      .some(artifact => !processedArtifactSignaturesRef.current.has(buildArtifactEventSignature(artifact)))
    if (mode === 'merge-artifacts') {
      if (!hasUnseenArtifact) {
        return false
      }

      const restoredArtifacts = restored.messages
        .filter(message => {
          if (!message.artifact) return false
          return !processedArtifactSignaturesRef.current.has(buildArtifactEventSignature(message.artifact))
        })
        .map(message => ({ ...message, id: mkId() }))
      if (restoredArtifacts.length === 0) {
        return false
      }

      const mergedMessages = [...messagesRef.current, ...restoredArtifacts]
      setMessages(mergedMessages)
      messagesRef.current = mergedMessages
      syncArtifactStateFromMessages(mergedMessages, extractLatestMaterialRequestedCategories(mergedMessages))
      setWsStageOverrides(prev => {
        const next = new Map(prev)
        for (const [stage, status] of restored.wsStageOverrides) {
          next.set(stage, status)
        }
        return next
      })
      const nextDownstreamRuns = { ...downstreamRunsRef.current, ...restored.downstreamRuns }
      downstreamRunsRef.current = nextDownstreamRuns
      setDownstreamRuns(nextDownstreamRuns)
      return true
    }

    const shouldReplace = mode === 'always' || restored.messages.length >= messagesRef.current.length
    if (!shouldReplace) {
      return false
    }

    applyRestoredMessages(restored.messages, restored.materialRequestedCategories)
    setWsStageOverrides(restored.wsStageOverrides)
    downstreamRunsRef.current = restored.downstreamRuns
    setDownstreamRuns(restored.downstreamRuns)
    return true
  }

  async function refreshConversationFromSandboxHistoryAfterTurn(
    endpoint: string,
    sessionId: string,
  ): Promise<boolean> {
    const retryDelaysMs = [250, 750, 1500]
    for (const delayMs of retryDelaysMs) {
      await sleep(delayMs)
      if (gatewayEndpointRef.current !== endpoint || sessionIdRef.current !== sessionId) {
        return false
      }

      const restored = await restoreConversationFromSandboxHistory(endpoint, sessionId, 'merge-artifacts')
      if (restored) {
        return true
      }
    }

    return false
  }

  const skillGenerationState = downstreamRuns['skill-generation'] ?? null
  const ontologyProjectionState = downstreamRuns['ontology-projection'] ?? null
  const skillStageConfirmationState = resolveActiveSkillStageRun(
    skillGenerationState,
    ontologyProjectionState,
  )
  const packagingTestCasesState = downstreamRuns['packaging-test-cases'] ?? null

  const handleExternalConfigChange = useCallback((
    config: HiringExternalSystemConfig | null,
    source: ExternalConfigChangeSource = 'hydrate',
  ) => {
    const previousConfig = latestExternalConfigRef.current
    latestExternalConfigRef.current = config
    if (shouldRequireFreshPackagingAfterExternalConfigChange(previousConfig, config, source, instanceCreated)) {
      setPendingPackageArtifact(null)
      setArtifactFileNames([])
      setRequiresFreshPackaging(true)
      setWorkflowError('')
      setWorkflowNotice(EXTERNAL_CONFIG_REPACKAGE_NOTICE)
    }

    const submissionMode = config?.submissionMode ?? 'pending'
    if (submissionMode !== 'configured' && submissionMode !== 'skipped') {
      setPendingStageConfirmation(prev => prev?.stage === HiringCollectionStage.External ? null : prev)
      if (source !== 'hydrate') {
        setWsStageOverrides(prev => {
          const next = new Map(prev)
          next.set(HiringCollectionStage.External, 'running')
          return next
        })
      }
      return
    }

    appendExternalConfigCommittedArtifact(config, { sendToSandbox: source !== 'hydrate' })
    setWsStageOverrides(prev => {
      if (source === 'hydrate') {
        return prev
      }

      const next = new Map(prev)
      next.set(HiringCollectionStage.External, 'running')
      return next
    })
  }, [instanceCreated])

  function handleAfterStageMessage(
    stage: HiringCollectionStageType,
    summary: string,
    intent: StageAdvanceIntent = 'collecting',
  ) {
    if (shouldRequireStageAdvanceConfirmation(stage, intent)) {
      const pending = buildPendingStageAdvanceConfirmation(stage, summary)
      if (!pending) {
        return
      }

      setPendingStageConfirmation(pending)
      setWorkflowError('')
      setWorkflowNotice('')
      setMessages(prev => [...prev, {
        id: mkId(),
        role: 'bot',
        content: pending.prompt,
      }])
      return
    }

    // skip 视为阶段已完成，直接标记阶段状态
    if (intent === 'skip' && stage === HiringCollectionStage.External) {
      setWsStageOverrides(prev => {
        const next = new Map(prev)
        next.set(HiringCollectionStage.External, 'completed')
        return next
      })
    }

    void submitWorkflowMessage(summary)
  }

  const workflowReady = Boolean(workflowHireId)
  const workflowCurrentStage = normalizeCollectionStage(
    (() => {
      const stages: HiringCollectionStageType[] = [
        HiringCollectionStage.Material,
        HiringCollectionStage.Skill,
        HiringCollectionStage.External,
        HiringCollectionStage.ReadyForPackaging,
      ]
      for (const stage of stages) {
        if (uiStageOverrides.get(stage) !== 'completed') return stage
      }
      return HiringCollectionStage.ReadyForPackaging
    })(),
  )

  // ── 副作用 Hooks ────────────────────────────────────────────────────────────
  useScrollToBottom(chatEndRef, messages, visibleTyping, visibleStreamingContent, streamingToolSteps)
  useBodyClassAndCleanup(wsRef)
  useTemplateDetail(templateId, setTemplate, setTemplateLoading, setTemplateError, t, normalizeErrorMessage)
  useSyncMessagesRef(messages, messagesRef)

  useEffect(() => {
    typingRef.current = typing
    if (!typing && !submittingMessage && pendingInternalPromptsRef.current.length > 0) {
      scheduleInternalPromptFlush()
    }
  }, [typing, submittingMessage])
  useRuntimeStateSync(
    workflowHireId,
    uiStageOverrides,
    downstreamRuns,
    allFiles,
    instanceCreated ? finalPackageFileName : '',
    artifactFileNames,
    createdId,
    instanceCreated,
  )
  useAutoFocusStage(journeyGuideVisible, focusedStage, workflowCurrentStage, setFocusedStage)

  // 工作流自动初始化
  useEffect(() => {
    if (templateLoading || templateError || !templateId) {
      return
    }
    if (Boolean(workflowHireId) || workflowBooting || workflowInitAttempted || messages.length > 0) {
      return
    }
    void ensureWorkflowReady()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateLoading, templateError, templateId, workflowHireId, workflowBooting, workflowInitAttempted, messages.length])

  // 沙箱推送 template_package artifact 后自动触发 import-package，将产物直接存入系统
  // 注：不在此清除 pendingPackageArtifact，以便【生成实例】按钮可依据它的状态作为手动兑现入口；instanceCreated 防重入
  // downstreamRuns 加入依赖：下游任务完成后重新检查，确保延迟暂存的数字员工包也能自动导入
  useEffect(() => {
    if (!pendingPackageArtifact || !workflowHireId || instanceCreated) return
    if (hasPendingRequiredDownstreamRuns(downstreamRunsRef.current)) return

    let cancelled = false
    void (async () => {
      await turnSyncBarrierRef.current
      if (cancelled) return
      void triggerCreate(pendingPackageArtifact)
    })()

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingPackageArtifact, workflowHireId, instanceCreated, downstreamRuns])

  const introName = template?.name ?? t('hiring.intro.digitalEmployee')
  // ─────────────────────────────────────────────────────────────────────────────

  async function ensureWorkflowReady(): Promise<string | null> {
    if (!templateId) {
      setWorkflowError(t('hiring.error.templateParamMissingDetail'))
      return null
    }
    if (workflowHireIdRef.current) {
      return workflowHireIdRef.current
    }
    if (workflowInitRef.current) {
      return workflowInitRef.current
    }

    workflowInitRef.current = (async () => {
      setWorkflowInitAttempted(true)
      setWorkflowBooting(true)
      setWorkflowError('')
      try {
        const hired = await api.employeeTemplate.hire(templateId, {})
        setWorkflowHireId(hired.hireId)
        workflowHireIdRef.current = hired.hireId

        // hire() 对复用的 Running+Initialized 沙箱会直接返回 READY + gatewayEndpoint，
        // 避免进入轮询循环；Paused/新建沙箱仍走轮询等待
        let latestStatus = hired.status
        let latestGatewayEndpoint: string | null = hired.gatewayEndpoint ?? null
        for (let retry = 0; retry < 30; retry += 1) {
          if (latestStatus === 'READY' || latestStatus === 'FAILED') break
          await sleep(1000)
          const statusResult = await api.employeeTemplate.getHiringStatus(hired.hireId)
          latestStatus = statusResult.status
          latestGatewayEndpoint = statusResult.gatewayEndpoint ?? null
        }
        if (latestStatus !== 'READY') {
          throw new Error('沙箱尚未就绪，请稍后重试')
        }

        // 如果 gatewayEndpoint 仍为空（旧版后端或极端情况），补一次查询
        if (!latestGatewayEndpoint) {
          const statusResult = await api.employeeTemplate.getHiringStatus(hired.hireId)
          latestGatewayEndpoint = statusResult.gatewayEndpoint ?? null
        }

        // 保存网关端点，后续直连沙箱使用（VITE_SANDBOX_URL 有值时固定使用本地端点）
        latestGatewayEndpoint = resolveGatewayEndpoint(latestGatewayEndpoint)
        if (latestGatewayEndpoint) {
          gatewayEndpointRef.current = latestGatewayEndpoint
        }

        // hire() 对复用的沙箱会直接返回 sessionId，跳过 startConversation() 调用；
        // 新建沙箱或后端未返回 sessionId 时仍走 startConversation() 获取
        if (hired.sessionId) {
          sessionIdRef.current = hired.sessionId
        } else {
          const conversation = await api.hiringWorkflow.startConversation(hired.hireId)
          sessionIdRef.current = conversation.sessionId
        }

        // 建立到沙箱的 WebSocket 直连，用于流式展示 AI 回复
        if (latestGatewayEndpoint) {
          await connectSandboxWs(latestGatewayEndpoint)
        }

        // 前端直连链路：自动下载模板包并上传到当前会话，触发模板分析与引导
        await autoBootstrapTemplateConversation(templateId, hired.hireId).catch((error: unknown) => {
          const bootstrapError = normalizeErrorMessage(error)
          console.warn('[HiringPage] auto template bootstrap skipped:', bootstrapError)
          setWorkflowNotice(t('hiring.notice.autoBootstrapFailed', { error: bootstrapError }))
        })

        return hired.hireId
      } catch (error: unknown) {
        setWorkflowError(normalizeErrorMessage(error))
        return null
      } finally {
        setWorkflowBooting(false)
        workflowInitRef.current = null
      }
    })()

    return workflowInitRef.current
  }

  async function waitForWsReady(maxRetry = 12, intervalMs = 250) {
    for (let i = 0; i < maxRetry; i += 1) {
      if (wsRef.current?.isOpen()) {
        return true
      }
      await sleep(intervalMs)
    }
    return Boolean(wsRef.current?.isOpen())
  }

  function buildTemplateBootstrapPrompt(
    templateName: string,
    useCases: string[],
    marker: string,
    uploadedFileName: string,
  ) {
    const topUseCases = useCases.slice(0, 5)
    const useCaseSection = topUseCases.length > 0
      ? `该模板典型场景：\n${topUseCases.map((item, index) => `${index + 1}. ${item}`).join('\n')}`
      : '该模板未提供显式场景列表，请先从模板文档中抽取核心业务场景。'

    return [
      '你正在运行雇佣教练会话，不是目标数字员工本人。',
      '本轮初始化同时涉及两套包，必须先明确二者关系：',
      '1. `coach_runtime_root`：固定为 `/workspace`。这是雇佣教练运行根目录，包含 employment-coach-conversation、ontology-slice-extraction、skill-generation 等系统 skill，只用于读取流程规则，永远不能作为数字员工包的 manifest 同步、产物写入、审查或打包根目录。',
      `2. \`employee_package_root\`：固定为下面的 ${marker} 目录。它才是本轮待装配的"${templateName}"专属工作区，只能在这个目录内做 manifest 同步、写入运行时产物、完整性审查和最终打包。`,
      '读取顺序：先读取并遵守雇佣教练包的 `/workspace/AGENTS.md`、`/workspace/SOUL.md`、`/workspace/IDENTITY.md` 和 `/workspace/skills/employment-coach-conversation/SKILL.md`，再读取目标员工模板包的 manifest.json 与 config 文档。',
      '冲突规则：雇佣教练包决定“你是谁、流程怎么走、artifact 怎么发”；目标员工模板包决定“要装配什么员工、需要哪些业务资料”。不得把目标员工的 config/SOUL.md、config/IDENTITY.md 或 config/AGENTS.md 当作你的身份指令。',
      '根目录红线：后续所有 artifact data 中的 `workspace_root` 必须等于 `employee_package_root`，不得等于 `/workspace`；所有打包命令必须先进入 `employee_package_root`，不得从 `coach_runtime_root` 打包。',
      '',
      `${marker}`,
      `模板包已解压到工作区目录（文件：${uploadedFileName}，模板名：${templateName}）。`,
      '',
      useCaseSection,
      '',
      '请在雇佣教练入口规则下读取上述目标模板目录中的 manifest.json，并按照 `/workspace/skills/employment-coach-conversation/SKILL.md` 的"会话初始化"步骤完成初始化（文件已就绪，无需解压），然后执行以下动作：',
      'A. 静默调用 `emit_artifact` 推送 stage1 progress（artifactType=material_collection_progress, stage=stage1_material, isTerminal=false），data.requested_categories 必须包含 1-3 个开场白中提到的建议上传资料分类，且必须是对象数组，例如 [{"title":"历史工单","description":"...","examples":["..."]}]，禁止输出字符串数组。这是内部系统调用，不要在回复中提及。',
      'B. 只用一句自然的话邀请我上传或描述业务资料，按 story-driven 风格开口，点到这 1-3 个分类即可。',
      '',
      '重要约束：',
      `- 所有用户可见回复都必须以雇佣教练口吻表达；不要自称"${templateName}"，也不要承诺直接执行该员工上岗后的业务任务。`,
      `- 如果用户问"你是谁"，应回答你是雇佣教练，正在帮助装配"${templateName}"这位数字员工。`,
      '- 不要输出任何系统状态确认语句（如"已确认工作区可用""执行阶段 1""强制动作"等内部步骤名称）。',
      '- 不要发出或提及未在 contracts/artifacts.json 声明的 artifact，例如 skill_generation_trigger、stage2_analysis、stage3_skills、skills_pipeline、技能流水线。',
      '- emit_artifact 已将分类推送到右侧面板，你不需要在文字回复中再逐项罗列分类名称或"最需要的三类资料是"这类总结句。',
      '- 你的回复就是一句简短自然的开场邀请，其他什么都不要加。',
    ].join('\n')
  }

  async function autoBootstrapTemplateConversation(currentTemplateId: string, currentHireId?: string) {
    const endpoint = gatewayEndpointRef.current
    let sessionId = sessionIdRef.current
    const ws = wsRef.current
    if (!endpoint || !sessionId || !ws) {
      return
    }
    if (autoTemplateBootstrapSessionRef.current === sessionId) {
      return
    }

    const wsReady = await waitForWsReady()
    if (!wsReady) {
      return
    }

    // 查询沙箱里最新的 WebSocket 会话，以便复用已有上下文
    const latestSessionId = await fetchLatestGatewaySession(endpoint)
    if (latestSessionId && latestSessionId !== sessionId) {
      sessionIdRef.current = latestSessionId
      sessionId = latestSessionId
    }

    // 检查当前会话是否已有历史消息——有消息说明模板之前已上传过，直接恢复历史，跳过引导上传
    const existingMessages = await fetchSandboxSessionMessages(endpoint, sessionId)
    const hireIdForCache = currentHireId || workflowHireId
    if (existingMessages.length === 0 && hireIdForCache) {
      try {
        // 尝试恢复运行时状态
        const restoredFromCache = await restoreRuntimeState(hireIdForCache)
        if (restoredFromCache) {
          autoTemplateBootstrapSessionRef.current = sessionId
          return
        }
      } catch {
        // 缓存读取失败时继续走首次引导，不阻断用户进入流程。
      }
    }

    if (existingMessages.length > 0) {
      // 1. 先从沙箱会话历史恢复消息，同时得到从 artifact tool call 派生的基础阶段状态
      await restoreConversationFromSandboxHistory(endpoint, sessionId, 'always')

      // 2. 从后端恢复运行时状态（wsStageOverrides + downstreamRuns）。
      //    原因：wsStageOverrides 中 WS stage_update 事件不在沙箱消息历史里，无法从历史重建。
      if (hireIdForCache) {
        try {
          await restoreRuntimeState(hireIdForCache)
        } catch {
          // 缓存读取失败时静默忽略，保留历史派生值
        }

        // 兜底：若 history 和 cache 均未能恢复 wsStageOverrides（stageOverrides 为空），
        // 则从 downstreamRuns 因果链反向推断主阶段状态，避免阶段胶囊在已有进度的情况下全部灰色。
        setWsStageOverrides(prev => {
          if (prev.size > 0) return prev
          return deriveStageOverridesFromDownstreamRuns(downstreamRunsRef.current)
        })
      }

      autoTemplateBootstrapSessionRef.current = sessionId
      return
    }

    // 会话为空（首次进入），执行模板下载 → 上传 → 发送引导消息
    const storeDetail = await api.employeeTemplate.getStoreDetail(currentTemplateId)
    const versionId = storeDetail.latestVersion?.id
    if (!versionId) {
      throw new Error(`模板 ${currentTemplateId} 暂无已发布版本，无法自动导入模板包`)
    }

    const packageData = await api.employeeTemplate.downloadTemplatePackage(currentTemplateId, versionId)
    const fileName = packageData.fileName || `${currentTemplateId}_${versionId}.zip`
    const packageFile = new File([packageData.blob], fileName, {
      type: packageData.blob.type || 'application/zip',
    })

    const token = await tokenService.ensureFresh()
    if (!token) {
      return
    }

    // 生成本次会话专属工作区目录名，格式与 SKILL.md 约定一致：<template_slug>-<yyyymmddHHmmss>
    const rawSlug = (storeDetail.name || currentTemplateId || 'template')
      .toLowerCase()
      .replace(/\s+/g, '-')
      .replace(/[^a-z0-9-]/g, '')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '') || 'template'
    const now = new Date()
    const pad = (n: number) => String(n).padStart(2, '0')
    const timestamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
    const dir = `${rawSlug}-${timestamp}`

    const uploadResult = await uploadWorkspaceFileToGateway(
      endpoint,
      token,
      packageFile,
      dir,
    )
    const prompt = buildTemplateBootstrapPrompt(
      storeDetail.name || template?.name || '数字员工模板',
      Array.isArray(storeDetail.useCases) ? storeDetail.useCases : [],
      uploadResult.fileMarker,
      packageFile.name,
    )

    lastWsUserMessageRef.current = prompt
    lastWsMaterialsRef.current = [
      {
        type: 'file',
        name: packageFile.name,
        size: packageFile.size,
        mimeType: 'application/zip',
        metadata: {
          source: 'template_auto_bootstrap',
          templateId: currentTemplateId,
          templateVersionId: versionId,
          workspaceDir: uploadResult.workspaceDir,
        },
      },
    ]

    const sent = ws.send({ type: 'user_message', text: prompt, sessionId })
    if (!sent) {
      return
    }

    autoTemplateBootstrapSessionRef.current = sessionId
    setTyping(true)
    setWorkflowNotice(t('hiring.notice.autoBootstrapInProgress'))
  }

  /**
   * 建立到沙箱 Gateway 的 WebSocket 直连。
   * 消息发送直接经由 WebSocket，沙箱流式推送 AI 回复。
   */
  async function connectSandboxWs(endpoint: string) {
    wsRef.current?.disconnect()
    wsRef.current = null

    const token = await tokenService.ensureFresh()
    if (!token) return

    const ws = new GatewayWs(endpoint, token)
    let settled = false
    let timeoutId: ReturnType<typeof window.setTimeout> | null = null

    const waitForOpen = new Promise<void>((resolve, reject) => {
      timeoutId = window.setTimeout(() => {
        if (settled) return
        settled = true
        reject(new Error('沙箱 WebSocket 连接超时，请稍后重试'))
      }, 8000)

      ws.onStateChange = (state) => {
        if (state === 'open' && !settled) {
          settled = true
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
            timeoutId = null
          }
          resolve()
          return
        }

        if ((state === 'closed' || state === 'error') && !settled) {
          settled = true
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
            timeoutId = null
          }
          reject(new Error('沙箱 WebSocket 握手失败，请确认当前账号有权访问该沙箱后重试'))
        }
      }
    })

    ws.onMessage = (msg) => {
      const type = msg.type as string
      if (type === 'typing_start') {
        // AI 开始思考，切换到流式展示
        typewriterStream.start()
        setTyping(true)
        setStreamingTurnInternal(lastWsTurnInternalRef.current)
        // 重置本轮工具步骤累积
        pendingToolStepsRef.current = []
        setStreamingToolSteps([])
        // 沙箱 AI 已开始回复，"模板包解析中"提示已完成其使命，清除以避免流程结束后残留
        setWorkflowNotice(prev => prev.includes('自动导入模板包') ? '' : prev)
      } else if (type === 'text_delta' || type === 'assistant_chunk') {
        // 原始分片进入共享打字机缓冲，UI 按稳定节奏释放字符。
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        typewriterStream.append(chunk)
      } else if (type === 'typing_stop' || type === 'assistant_done') {
        // typing_stop 只做软结束等待，assistant_done 可带完整正文并立即固化。
        const userMessage = lastWsUserMessageRef.current
        const materials = lastWsMaterialsRef.current
        const isInternalTurn = lastWsTurnInternalRef.current
        const fallbackReply = String(msg.content ?? msg.text ?? '')
        const finishOptions = type === 'typing_stop'
          ? { deferMs: TYPEWRITER_SOFT_FINISH_DEFER_MS }
          : undefined
        typewriterStream.finish(fallbackReply, (rawReply) => {
          // 直接从 hook 的 raw 文本提交正式消息，避免 React StrictMode 双重调用导致重复气泡。
          if (!isInternalTurn && rawReply && rawReply.trim().length > 0) {
            const cleaned = normalizeAssistantReply(rawReply)
            if (cleaned.length > 0) {
              // 将本轮累积的工具调用步骤附到 bot 消息，与 Markdown 正文合并呈现。
              const steps = pendingToolStepsRef.current.length > 0 ? [...pendingToolStepsRef.current] : undefined
              setMessages(msgs => [...msgs, { id: mkId(), role: 'bot', content: cleaned, toolSteps: steps }])
            }
          }
          // 无论是否产生 bot 消息，本轮状态都需重置。
          pendingToolStepsRef.current = []
          setStreamingToolSteps([])
          setTyping(false)
          setStreamingTurnInternal(false)
          lastWsTurnInternalRef.current = false

          // 将对话轮次同步到后端，使工作流引擎处理 AI 结构化标签、推进阶段等
          const hireId = workflowHireIdRef.current
          const endpoint = gatewayEndpointRef.current
          const sessionId = sessionIdRef.current
          if (hireId && rawReply) {
            const syncPromise = api.hiringWorkflow
              .syncConversationTurn(hireId, {
                userMessage: userMessage || '',
                assistantReply: rawReply,
                materials: materials ?? undefined,
              })
              .then(() => undefined)
              .catch(() => undefined)
            turnSyncBarrierRef.current = syncPromise
          }
          if (endpoint && sessionId && !postTurnHistoryRefreshRef.current) {
            const refreshPromise = refreshConversationFromSandboxHistoryAfterTurn(endpoint, sessionId)
              .catch(() => false)
              .finally(() => {
                if (postTurnHistoryRefreshRef.current === refreshPromise) {
                  postTurnHistoryRefreshRef.current = null
                }
              })
            postTurnHistoryRefreshRef.current = refreshPromise
          }
          void flushQueuedInternalPrompt()
        }, finishOptions)
      } else if (type === 'tool_start') {
        // MCP 工具开始调用：仅用于记录流式气泡上方的进度面板
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.text ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        // 累积本轮的工具调用，驱动流式气泡上方的进度面板
        const args = rawMsg.arguments != null
          ? (typeof rawMsg.arguments === 'string' ? rawMsg.arguments : JSON.stringify(rawMsg.arguments))
          : undefined
        const step: ToolStep = { id: mkId(), name: toolName || 'tool', status: 'running', args }
        pendingToolStepsRef.current = [...pendingToolStepsRef.current, step]
        setStreamingToolSteps([...pendingToolStepsRef.current])
      } else if (type === 'tool_result') {
        // MCP 工具调用完成：优先从顶层字段取工具名（部分 Gateway 版本携带），
        // 取不到时尝试解析 text JSON——若结果中含 data.handoff_id 则判定为 hiring todo 结果
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.tool_name ?? rawMsg.toolName ?? rawMsg.name ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const textStr = String(rawMsg.text ?? '')
        const fallbackArgs = rawMsg.arguments != null
          ? (typeof rawMsg.arguments === 'string' ? rawMsg.arguments : JSON.stringify(rawMsg.arguments))
          : undefined
        let completedStep: ToolStep | null = null
        let toolResultIsError = Boolean(rawMsg.is_error ?? rawMsg.isError)
        // 将返回填回本轮步骤：同名优先匹配最后一个 running；缺失工具名时回退到最后一个 running
        {
          const list = pendingToolStepsRef.current
          let targetIdx = -1
          if (toolName) {
            for (let i = list.length - 1; i >= 0; i--) {
              if (list[i].status === 'running' && list[i].name === toolName) { targetIdx = i; break }
            }
          }
          if (targetIdx < 0) {
            for (let i = list.length - 1; i >= 0; i--) {
              if (list[i].status === 'running') { targetIdx = i; break }
            }
          }
          if (targetIdx >= 0) {
            const next = list.slice()
            next[targetIdx] = {
              ...next[targetIdx],
              status: toolResultIsError ? 'error' : 'done',
              result: textStr || next[targetIdx].result,
            }
            completedStep = next[targetIdx]
            pendingToolStepsRef.current = next
            setStreamingToolSteps([...next])
          }
        }
        if (!toolResultIsError) {
          let artifact = extractArtifactFromToolCall({
            toolName: toolName || completedStep?.name || '',
            arguments: completedStep?.args ?? fallbackArgs,
            result: textStr,
          })
          // 回退：当 tool_result 缺少 arguments 字段时（如 Gateway 版本使用 toolName 驼峰字段
          // 且 tool_start 未被正确捕获），从 text 描述中解析 artifact 元数据，
          // 避免 skill_generation_done / external_workorder_summary 等终态 artifact 静默丢失导致阶段无法推进
          if (!artifact && toolName === 'emit_artifact' && textStr) {
            const parsedFromText = parseArtifactFromToolResultText(textStr)
            if (parsedFromText) {
              try {
                artifact = normalizeArtifactDisplayData(parsedFromText)
              } catch (err) {
                console.warn('[HiringPage] failed to normalize artifact parsed from tool_result text:', err)
              }
            }
          }
          if (artifact) {
            ws.onMessage?.({ type: 'artifact', artifact })
          } else if (toolName === 'emit_artifact') {
            console.warn(
              '[HiringPage] emit_artifact tool_result ignored: unable to extract artifact metadata (missing arguments and unparseable text)',
              { textStr },
            )
          }
        }
      } else if (type === 'artifact') {
        // 下游 skill 通过 emit_artifact 工具推送产物（对应 contracts/artifacts.json 声明的类型）
        const raw = msg.artifact as Record<string, unknown> | null | undefined
        if (raw) {
          let artifactData: ArtifactDisplayData
          try {
            artifactData = normalizeArtifactDisplayData(raw)
          } catch (error) {
            console.warn('[HiringPage] ignored malformed artifact payload:', error)
            return
          }

          const kind = artifactData.kind
          const artifactType = artifactData.artifactType
          const label = artifactData.label
          const skillName = artifactData.skillName
          const stage = artifactData.stage
          let isTerminal = Boolean(artifactData.isTerminal)
          isTerminal = normalizeIncomingArtifactTerminal(artifactType, isTerminal)
          artifactData.isTerminal = isTerminal
          if (kind === 'file') {
            artifactData.mimeType = String(raw.mimeType ?? raw.mime_type ?? '')
            const sizeBytes = typeof raw.fileSizeBytes === 'number' ? raw.fileSizeBytes : typeof raw.file_size_bytes === 'number' ? raw.file_size_bytes : null
            artifactData.sizeLabel = sizeBytes !== null ? formatFileSize(sizeBytes) : ''
          }
          const blockedArtifactReason = getBlockedIncomingArtifactReason(artifactType, {
            hasMaterialSummary: latestMaterialSummaryRef.current !== null,
            hasOntologyExtractionDone: downstreamRunsRef.current['ontology-slice-extraction']?.status === 'completed'
              || (artifactType === 'ontology_slice_extraction_done' && isTerminal),
            hasSkillSummary: latestSkillSummaryRef.current !== null,
            hasProjectionResult: (artifactType === 'ontology_projection_done' && isTerminal)
              || latestProjectionResultRef.current !== null,
            canUseProjectionForSkillGeneration: buildSkillGenerationPayload(
              latestSkillSummaryRef.current,
              artifactType === 'ontology_projection_done' && isTerminal
                ? artifactData.data ?? null
                : latestProjectionResultRef.current,
            ) !== null,
            hasExternalConfigCommitted: Boolean(externalConfigCommittedSignatureRef.current),
          }, {
            isTerminal,
            kind,
            data: artifactData.data,
          })
          if (blockedArtifactReason) {
            console.warn('[HiringPage] ignored gated artifact:', artifactType, blockedArtifactReason)
            return
          }
          if (artifactType === 'packaging_testcases_ready') {
            const currentRun = downstreamRunsRef.current['packaging-test-cases']
            const alreadyPrompted =
              currentRun?.status === 'waiting_confirm' ||
              currentRun?.status === 'running' ||
              currentRun?.status === 'completed' ||
              messagesRef.current.some(message => message.artifact?.artifactType === 'packaging_testcases_ready')
            if (alreadyPrompted) {
              return
            }
          }
          const artifactSignature = buildArtifactEventSignature(artifactData)
          if (processedArtifactSignaturesRef.current.has(artifactSignature)) {
            return
          }
          processedArtifactSignaturesRef.current.add(artifactSignature)
          if (artifactType === 'material_collection_progress') {
            const categories = normalizeMaterialRequestedCategories(artifactData.data)
            if (categories.length > 0) {
              setMaterialRequestedCategories(categories)
            }
          }
          pruneStaleInternalPromptsForState(artifactType)
          if (artifactType === 'material_handoff_summary' && kind === 'data' && isTerminal) {
            latestMaterialSummaryRef.current = artifactData.data ?? null
            const signature = JSON.stringify(artifactData.data ?? {})
            if (materialSummarySignatureRef.current !== signature) {
              materialSummarySignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildDownstreamPrompt('ontology-slice-extraction', artifactData.data ?? {}),
              )
            }
          }
          if (artifactType === 'ontology_slice_extraction_done' && kind === 'data' && isTerminal) {
            const signature = JSON.stringify(artifactData.data ?? {})
            if (ontologyExtractionDoneSignatureRef.current !== signature) {
              ontologyExtractionDoneSignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildCoachResumePrompt('post-ontology-slice-extraction', {
                  materialSummary: latestMaterialSummaryRef.current,
                  ontologyResult: artifactData.data ?? {},
                }),
              )
            }
          }
          if (artifactType === 'ontology_projection_done' && kind === 'data' && isTerminal) {
            latestProjectionResultRef.current = artifactData.data ?? null
            const signature = JSON.stringify(artifactData.data ?? {})
            if (ontologyProjectionDoneSignatureRef.current !== signature) {
              ontologyProjectionDoneSignatureRef.current = signature
              const skillGenerationQueued = queueSkillGenerationReadyFromProjectionResult(artifactData.data ?? {})
              if (!skillGenerationQueued && latestSkillSummaryRef.current !== null) {
                pendingInternalPromptsRef.current.push(
                  buildCoachResumePrompt('post-ontology-projection', {
                    skillSummary: latestSkillSummaryRef.current,
                    projectionResult: artifactData.data ?? {},
                  }),
                )
              }
            }
          }
          if (artifactType === 'skill_workorder_summary' && kind === 'data' && isTerminal) {
            latestSkillSummaryRef.current = artifactData.data ?? null
            setLatestSkillSummary(artifactData.data ?? null)
            latestProjectionResultRef.current = null
            skillSummarySignatureRef.current = JSON.stringify(artifactData.data ?? {})
            projectionPassLaunchSignatureRef.current = ''
            skillGenerationLaunchSignatureRef.current = ''
            ontologyProjectionDoneSignatureRef.current = ''
          }
          if (artifactType === 'review_report' && kind === 'data' && isTerminal) {
            latestReviewReportRef.current = artifactData.data ?? null
          }
          if (artifactType === 'external_workorder_summary' && kind === 'data' && isTerminal) {
            latestExternalSummaryRef.current = artifactData.data ?? null
            externalSummarySignatureRef.current = JSON.stringify(artifactData.data ?? {})
          }
          if (artifactType === 'packaging_testcases_done' && kind === 'data' && isTerminal) {
            const signature = JSON.stringify(artifactData.data ?? {})
            if (packagingTestCasesDoneSignatureRef.current !== signature) {
              packagingTestCasesDoneSignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildCoachResumePrompt('post-packaging-test-cases', {
                  packagingTestCasesResult: artifactData.data ?? {},
                }),
              )
            }
          }
          const downstreamRun = resolveDownstreamRunFromArtifact(artifactType)
          const duplicateExternalConfigCommitted = artifactType === 'external_config_committed'
            && isDuplicateExternalConfigCommittedArtifact(
              externalConfigCommittedSignatureRef.current,
              artifactData.data,
            )
          const externalConfigCommittedSignature = artifactType === 'external_config_committed'
            ? tryBuildExternalConfigCommittedSignature(artifactData.data)
            : null
          if (externalConfigCommittedSignature) {
            externalConfigCommittedSignatureRef.current = externalConfigCommittedSignature
          }
          const shouldDisplayArtifact = shouldDisplayArtifactInConversation(artifactType, isTerminal)
          if (artifactType === 'packaging_progress') {
            const progressData = artifactData.data && typeof artifactData.data === 'object' && !Array.isArray(artifactData.data)
              ? artifactData.data as Record<string, unknown>
              : null
            packagingInProgressRef.current = progressData?.status === 'packing' || progressData?.status === 'waiting_downstream'
          }
          if (!duplicateExternalConfigCommitted && shouldDisplayArtifact) {
            setMessages(msgs => [...msgs, {
              id: mkId(),
              role: 'artifact',
              content: label ?? artifactType,
              artifact: artifactData,
            }])
          }
          if (downstreamRun) {
            setDownstreamRuns(prev => {
              const next = {
                ...prev,
                [downstreamRun.key]: {
                  key: downstreamRun.key,
                  status: downstreamRun.status,
                  artifactType,
                  label,
                  displayHint: artifactData.displayHint,
                  updatedAt: new Date().toISOString(),
                  data: artifactData.data,
                } satisfies DownstreamRunState,
              }
              downstreamRunsRef.current = next
              return next
            })
          }
          // 同步更新阶段胶囊状态（实时，不等 REST 轮询）
          const hiringStage = downstreamRun ? null : resolveHiringStageFromWs(skillName, stage)
          if (hiringStage) {
            setWsStageOverrides(prev => {
              const next = new Map(prev)
              // External 阶段的需求收口和系统提交是两件事：
              // `external_workorder_summary` 仅表示需求已收口，仍需等待右侧卡片提交完成。
              // Skill 阶段的技能定义完成（skill_workorder_summary）不等于技能阶段完成；
              // 必须等待下游 skill-generation 完成（skill_generation_done）才算阶段结束。
              if (artifactType === 'external_config_committed' && isTerminal) {
                next.set(HiringCollectionStage.Material, 'completed')
                next.set(HiringCollectionStage.Skill, 'completed')
                next.set(HiringCollectionStage.External, 'completed')
              } else if (
                (
                  artifactType === 'external_workorder_summary'
                  || artifactType === 'skill_workorder_summary'
                  || artifactType === 'material_handoff_summary'
                )
                && isTerminal
              ) {
                if (next.get(hiringStage) !== 'completed') {
                  next.set(hiringStage, 'running')
                }
              } else if (isTerminal) {
                next.set(hiringStage, 'completed')
              } else if (next.get(hiringStage) !== 'completed') {
                next.set(hiringStage, 'running')
              }
              return next
            })
          }
          // template_package artifact 表示沙箱已完成打包，暂存 fileUrl 后自动触发 import-package
          if (artifactType === 'template_package' && kind === 'file' && artifactData.fileUrl) {
            packagingInProgressRef.current = false
            // 既然已经收到最终包，说明阶段推进已实际完成，清掉陈旧的确认提示，避免阻塞自动导入。
            setPendingStageConfirmation(null)
            // 无论是否有下游任务未完成，均暂存数字员工包信息；
          // useEffect 会在 downstreamRuns 全部完成后自动触发 triggerCreate。
          setRequiresFreshPackaging(false)
          setPendingPackageArtifact({ fileUrl: artifactData.fileUrl, fileName: artifactData.fileName ?? '数字员工.zip' })
          if (hasPendingRequiredDownstreamRuns(downstreamRunsRef.current)) {
            setWorkflowNotice('已收到数字员工包，下游生成完成后将自动导入。')
            }
          }
        }
      } else if (type === 'skill_stage_gate') {
        // skill 内部阶段推进通知（对应 contracts/artifacts.json 的 gate 声明）
        const gate = (msg.stageGate ?? msg.stage_gate) as Record<string, unknown> | null | undefined
        if (gate) {
          const stageGate: StageGateData = {
            skillName: String(gate.skillName ?? gate.skill_name ?? ''),
            completedStage: String(gate.completedStage ?? gate.completed_stage ?? ''),
            nextStage: String(gate.nextStage ?? gate.next_stage ?? ''),
            canProceed: Boolean(gate.canProceed ?? gate.can_proceed),
            blockedReason: gate.blockedReason != null ? String(gate.blockedReason) : gate.blocked_reason != null ? String(gate.blocked_reason) : undefined,
          }
          const shouldSuppressGate = shouldSuppressStageGate(stageGate, downstreamRunsRef.current)
          if (shouldSuppressGate) {
            return
          }
          setMessages(msgs => [...msgs, {
            id: mkId(),
            role: 'stage_gate',
            content: stageGate.canProceed
              ? `${stageGate.skillName}: ${stageGate.completedStage} → ${stageGate.nextStage}`
              : `${stageGate.nextStage} 阶段阻塞${stageGate.blockedReason ? `：${stageGate.blockedReason}` : ''}`,
            stageGate,
          }])
          // stage_gate 推进：completedStage 对应的雇佣阶段标记完成；nextStage 标记运行中
          const completedHiringStage = resolveHiringStageFromWs(stageGate.skillName, stageGate.completedStage)
          const nextHiringStage = resolveHiringStageFromWs(stageGate.skillName, stageGate.nextStage)
          if (completedHiringStage || nextHiringStage) {
            setWsStageOverrides(prev => {
              const next = new Map(prev)
              if (completedHiringStage && stageGate.canProceed) next.set(completedHiringStage, 'completed')
              if (completedHiringStage && !stageGate.canProceed) next.set(completedHiringStage, 'failed')
              if (nextHiringStage && stageGate.canProceed && next.get(nextHiringStage) !== 'completed') {
                next.set(nextHiringStage, 'running')
              }
              return next
            })
          }
        }
      }
    }

    // 重连后拉取断线期间的会话历史
    ws.onReconnected = () => {
      const sid = sessionIdRef.current
      if (endpoint && sid) {
        restoreConversationFromSandboxHistory(endpoint, sid, 'if-longer').catch(() => { /* 忽略拉取失败 */ })
      }
    }

    ws.connect()
    wsRef.current = ws

    // 开发调试钩子：window.__injectWsMsg({ type, ... }) 可在浏览器 Console 模拟 WS 消息
    if (import.meta.env.DEV) {
      ;((window as unknown) as Record<string, unknown>).__injectWsMsg = (msg: GatewayMessage) => {
        ws.onMessage?.(msg)
      }
    }

    try {
      await waitForOpen
    } catch (error) {
      ws.disconnect()
      if (wsRef.current === ws) {
        wsRef.current = null
      }
      throw error
    }
  }

  function retryWorkflowInitialization() {
    setWorkflowError('')
    setWorkflowNotice('')
    setWorkflowInitAttempted(false)
    void ensureWorkflowReady()
  }

  async function flushQueuedInternalPrompt() {
    if (internalPromptFlushInFlightRef.current) {
      return
    }

    internalPromptFlushInFlightRef.current = true
    try {
      await turnSyncBarrierRef.current

      if (typingRef.current || messageSubmitRef.current) {
        scheduleInternalPromptFlush()
        return
      }

      if (pendingInternalPromptsRef.current.length === 0) {
        return
      }

      const prompt = pendingInternalPromptsRef.current.shift()
      if (!prompt) return

      await submitWorkflowMessage(prompt, undefined, true, false, false)
      if (pendingInternalPromptsRef.current.length > 0) {
        scheduleInternalPromptFlush()
      }
    } finally {
      internalPromptFlushInFlightRef.current = false
    }
  }

  function scheduleInternalPromptFlush(delayMs = 80) {
    if (internalPromptFlushRetryRef.current !== null) {
      return
    }

    internalPromptFlushRetryRef.current = window.setTimeout(() => {
      internalPromptFlushRetryRef.current = null
      void flushQueuedInternalPrompt()
    }, delayMs)
  }

  function setOptimisticDownstreamRun(
    key: DownstreamRunKey,
    status: DownstreamRunStatus,
    label: string,
    artifactType: string,
  ) {
    setDownstreamRuns(prev => {
      const next = {
        ...prev,
        [key]: {
          key,
          status,
          artifactType,
          label,
          displayHint: 'progress',
          updatedAt: new Date().toISOString(),
          data: prev[key]?.data,
        } satisfies DownstreamRunState,
      }
      downstreamRunsRef.current = next
      return next
    })
  }

  function clearDownstreamRun(key: DownstreamRunKey) {
    setDownstreamRuns(prev => {
      if (!(key in prev)) return prev
      const next = { ...prev }
      delete next[key]
      downstreamRunsRef.current = next
      return next
    })
  }

  function pruneStaleInternalPromptsForState(artifactType: string) {
    if (pendingInternalPromptsRef.current.length === 0) {
      return
    }

    const stalePatternsByArtifact: Record<string, string[]> = {
      skill_workorder_summary: [
        'Resume the main hiring flow at the boundary between stage1_material and stage2_skill',
        '[Internal skill definition confirmation.',
      ],
      ontology_projection_done: [
        'trigger_reason: user_confirmed_ontology_projection',
      ],
      skill_generation_done: [
        'trigger_reason: projection_done_generate_skills',
        'The downstream ontology projection pass has completed.',
      ],
      review_report: [
        'Resume the main hiring flow inside stage2_skill.',
        'trigger_reason: projection_done_generate_skills',
        'trigger_reason: user_confirmed_ontology_projection',
        'The optional evaluation test case generation has completed.',
      ],
      template_package: [
        '[Internal stage resume.',
        '[Internal downstream trigger:',
        '[Internal packaging trigger.',
        '[Internal skill definition confirmation.',
      ],
    }

    const stalePatterns = stalePatternsByArtifact[artifactType]
    if (!stalePatterns || stalePatterns.length === 0) {
      return
    }

    pendingInternalPromptsRef.current = pendingInternalPromptsRef.current.filter(prompt =>
      !stalePatterns.some(pattern => prompt.includes(pattern)),
    )
  }

  function queueSkillGenerationReadyFromProjectionResult(projectionResult: unknown): boolean {
    const currentRun = downstreamRunsRef.current['skill-generation']
    if (currentRun?.status === 'running' || currentRun?.status === 'completed') {
      return true
    }

    const summary = latestSkillSummaryRef.current
    const payload = buildSkillGenerationPayload(summary, projectionResult)
    if (!payload) {
      return false
    }

    const signature = JSON.stringify({ skillSummary: summary, projectionResult })
    if (signature && skillGenerationLaunchSignatureRef.current === signature) {
      return true
    }

    skillGenerationLaunchSignatureRef.current = signature
    setOptimisticDownstreamRun(
      'skill-generation',
      'waiting_confirm',
      '技能数据已匹配完成，等待确认生成技能实现。',
      'skill_generation_ready',
    )
    pendingInternalPromptsRef.current.push(
      buildCoachResumePrompt('post-ontology-projection', {
        skillSummary: summary,
        projectionResult,
      }),
    )
    setWorkflowNotice('技能数据已匹配完成，请确认是否生成技能实现。')
    void flushQueuedInternalPrompt()
    return true
  }

  async function resumeCoachAfterUnusableProjection(projectionResult: unknown): Promise<boolean> {
    const summary = latestSkillSummaryRef.current
    if (!summary) return false

    const submitted = await submitWorkflowMessage(
      buildCoachResumePrompt('post-ontology-projection', {
        skillSummary: summary,
        projectionResult,
      }),
      undefined,
      true,
      false,
    )

    if (submitted) {
      setWorkflowNotice('当前还没有可用于技能生成的业务资料，正在回到雇佣教练引导下一步。')
    }

    return submitted
  }

  /** 技能阶段快捷按钮：确认生成技能 */
  async function handleConfirmSkillGeneration() {
    const state = downstreamRunsRef.current['skill-generation']
    const projectionState = downstreamRunsRef.current['ontology-projection']
    if (state?.status === 'running') {
      setWorkflowNotice('技能实现正在生成中。')
      return
    }
    if (state?.status === 'completed') {
      setWorkflowNotice('技能实现已生成完成。')
      return
    }

    if (state?.artifactType === 'skill_definition_ready') {
      setMessages(prev => [...prev, { id: mkId(), role: 'user', content: '确认技能清单' }])
      await confirmSkillDefinitionFromApproval('确认技能清单')
      return
    }

    if (projectionState?.artifactType === 'ontology_projection_ready') {
      setMessages(prev => [...prev, { id: mkId(), role: 'user', content: '开始匹配技能数据' }])
      await launchProjectionPassFromApproval()
      return
    }

    if (state?.artifactType === 'skill_generation_ready') {
      setMessages(prev => [...prev, { id: mkId(), role: 'user', content: '确认生成技能实现' }])
      await launchSkillGenerationFromApproval()
      return
    }

    // 兜底：直接发送确认消息
    await submitWorkflowMessage('确认生成技能，请基于当前已定义的技能和业务资料开始生成。', undefined, true, true)
  }

  /** 技能阶段快捷按钮：推进到外部系统 */
  async function handleConfirmSkillStageDone() {
    const summary = '技能生成已完成，请推进到外部系统阶段。'
    const submitted = await submitWorkflowMessage(summary)
    if (submitted) {
      setWsStageOverrides(prev => {
        const next = new Map(prev)
        next.set(HiringCollectionStage.Skill, 'completed')
        return next
      })
    }
  }

  async function confirmSkillDefinitionFromApproval(userRequest: string): Promise<boolean> {
    const state = downstreamRunsRef.current['skill-generation']
    if (state?.artifactType !== 'skill_definition_ready') {
      return false
    }

    const submitted = await submitWorkflowMessage(
      buildSkillDefinitionConfirmationPrompt(userRequest, state.data ?? {}),
      undefined,
      true,
      false,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已确认技能清单，正在收口技能定义并进入匹配技能数据确认。')
      return true
    }

    return false
  }

  async function launchProjectionPassFromApproval(): Promise<boolean> {
    const summary = latestSkillSummaryRef.current
    if (!summary) return false

    const projectionRun = downstreamRunsRef.current['ontology-projection']
    const signature = skillSummarySignatureRef.current || JSON.stringify(summary)
    if (projectionRun?.status === 'running') {
      setWorkflowNotice('正在匹配技能数据，请等待进度更新。')
      return true
    }

    if (projectionRun?.status === 'completed') {
      const projectionResult = latestProjectionResultRef.current ?? projectionRun.data ?? {}
      if (queueSkillGenerationReadyFromProjectionResult(projectionResult)) {
        return true
      }

      return resumeCoachAfterUnusableProjection(projectionResult)
    }

    if (signature && projectionPassLaunchSignatureRef.current === signature) {
      setWorkflowNotice('正在匹配技能数据，请等待进度更新。')
      return true
    }

    const projectionPayload = buildProjectionPassPayload(summary)
    if (!projectionPayload) {
      setWorkflowError('技能定义摘要缺少匹配技能数据所需字段，暂时无法启动。')
      return false
    }

    if (signature) {
      projectionPassLaunchSignatureRef.current = signature
    }

    setOptimisticDownstreamRun(
      'ontology-projection',
      'running',
      '正在匹配技能数据，等待下游进度更新。',
      'ontology_projection_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('ontology-projection', projectionPayload),
      undefined,
      true,
      false,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已开始匹配技能数据；匹配完成后会等待你确认是否生成技能实现。')
      return true
    }

    projectionPassLaunchSignatureRef.current = ''
    clearDownstreamRun('ontology-projection')
    return false
  }

  async function launchSkillGenerationFromApproval(): Promise<boolean> {
    const currentRun = downstreamRunsRef.current['skill-generation']
    if (currentRun?.status === 'running') {
      setWorkflowNotice('技能实现正在生成中。')
      return true
    }

    if (currentRun?.status === 'completed') {
      setWorkflowNotice('技能实现已生成完成。')
      return true
    }

    const summary = latestSkillSummaryRef.current
    const projectionResult = latestProjectionResultRef.current ?? downstreamRunsRef.current['ontology-projection']?.data ?? null
    const payload = buildSkillGenerationPayload(summary, projectionResult)
    if (!payload) {
      return resumeCoachAfterUnusableProjection(projectionResult ?? {})
    }

    const signature = JSON.stringify({
      skillSummary: summary,
      projectionResult,
      projection_binding_confirmed: true,
    })
    if (signature && skillGenerationLaunchSignatureRef.current === signature) {
      setWorkflowNotice('技能实现生成已启动，请等待进度更新。')
      return true
    }

    skillGenerationLaunchSignatureRef.current = signature
    setOptimisticDownstreamRun(
      'skill-generation',
      'running',
      '技能实现生成已启动，正在等待下游进度。',
      'skill_generation_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('skill-generation', payload),
      undefined,
      true,
      false,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已开始生成技能实现。')
      return true
    }

    skillGenerationLaunchSignatureRef.current = ''
    setOptimisticDownstreamRun(
      'skill-generation',
      'waiting_confirm',
      '技能数据已匹配完成，等待确认生成技能实现。',
      'skill_generation_ready',
    )
    return false
  }

  async function launchPackagingTestCasesFromApproval(): Promise<boolean> {
    const currentRun = downstreamRunsRef.current['packaging-test-cases']
    if (currentRun?.status === 'running') {
      setWorkflowNotice('评估测试用例生成已启动，请等待进度更新。')
      return true
    }

    if (currentRun?.status === 'completed') {
      setWorkflowNotice('评估测试用例已生成，可以继续生成数字员工。')
      return true
    }

    const payload = {
      trigger_after: 'external_config_committed',
      latest_material_summary: latestMaterialSummaryRef.current,
      latest_skill_summary: latestSkillSummaryRef.current,
      latest_external_summary: latestExternalSummaryRef.current,
      external_config: latestExternalConfigRef.current,
    }
    const signature = JSON.stringify(payload)
    if (signature && packagingTestCasesLaunchSignatureRef.current === signature) {
      setWorkflowNotice('评估测试用例生成已启动，请等待进度更新。')
      return true
    }

    packagingTestCasesLaunchSignatureRef.current = signature
    setOptimisticDownstreamRun(
      'packaging-test-cases',
      'running',
      '评估测试用例生成已启动，正在等待下游进度。',
      'packaging_testcases_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('packaging-test-cases', payload),
      undefined,
      true,
      false,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已开始生成评估测试用例，完成后可继续生成数字员工。')
      return true
    }

    packagingTestCasesLaunchSignatureRef.current = ''
    clearDownstreamRun('packaging-test-cases')
    return false
  }

  async function submitWorkflowMessage(
    text: string,
    incoming?: ChatFile[],
    autoApprove = true,
    /**
     * 是否把本条用户消息推入本地 `messages` 列表。
     * - true（默认）：调用方未自行 setMessages，本函数负责上屏（TODO 卡回调、onAfterStageMessage 等模拟消息走这条路）
     * - false：调用方已经 setMessages（handleSend / 技能上传弹窗），避免重复气泡
     */
    displayInChat = true,
    isInternalTurn = false,
  ): Promise<boolean> {
    if (messageSubmitRef.current) {
      setWorkflowError(t('hiring.error.generationInProgress'))
      return false
    }

    const hireId = await ensureWorkflowReady()
    if (!hireId) return false

    // 模拟消息上屏：在真正发送前先把用户气泡推入列表，避免 TODO/Stage 回调发的消息悄无声息
    if (displayInChat && (text || (incoming && incoming.length > 0))) {
      setMessages(prev => [...prev, {
        id: mkId(),
        role: 'user',
        content: text || '',
        files: incoming && incoming.length > 0 ? incoming : undefined,
      }])
    }

    messageSubmitRef.current = true
    setSubmittingMessage(true)

    const ws = wsRef.current
    const sessionId = sessionIdRef.current

    // WS 已连通：直接通过 WebSocket 发送消息，沙箱实时流式回复；
    // 若有附件，先上传到 Gateway 获取 [FILE_URL:...] 标记，再随文本一起发送
    if (ws && sessionId) {
      try {
        let messageText = text || '补充信息'

        if (incoming && incoming.length > 0) {
          const endpoint = gatewayEndpointRef.current
          const token = await tokenService.ensureFresh()
          if (!endpoint || !token) {
            throw new Error('Gateway endpoint or token not available for file upload')
          }
          const markers: string[] = []
          for (const file of incoming) {
            const rawFile = rawFileMapRef.current.get(file.id) ?? file.rawFile
            if (!rawFile) {
              throw new Error(`无法获取文件原始数据：${file.name}`)
            }
            const result = await uploadMediaToGateway(endpoint, token, rawFile)
            markers.push(`${result.marker}\nAttached file: ${result.fileName} (${formatFileSize(result.sizeBytes)})`)
          }
          if (markers.length > 0) {
            messageText = `${markers.join('\n')}\n\n${messageText}`
          }
        }

        // 记录本次发送的用户消息和材料，供 WS 终止事件中调用同步端点使用。
        lastWsUserMessageRef.current = messageText
        lastWsMaterialsRef.current = toConversationMaterials(incoming)
        lastWsTurnInternalRef.current = isInternalTurn
        if (!ws.isOpen()) {
          throw new Error('沙箱 WebSocket 尚未连接，请稍后重试')
        }
        const sent = ws.send({ type: 'user_message', text: messageText, sessionId })
        if (!sent) {
          throw new Error('沙箱 WebSocket 消息发送失败，请稍后重试')
        }
        setTyping(true)
        setWorkflowError('')
        setWorkflowNotice('')
        // 清理已完成上传的原始文件引用
        if (incoming) {
          for (const file of incoming) {
            rawFileMapRef.current.delete(file.id)
          }
        }
        return true
      } catch (error: unknown) {
        setWorkflowError(normalizeErrorMessage(error))
        setTyping(false)
        setStreamingTurnInternal(false)
        typewriterStream.reset()
        lastWsTurnInternalRef.current = false
        // 错误回退时清理本轮工具步骤累积
        pendingToolStepsRef.current = []
        setStreamingToolSteps([])
        return false
      } finally {
        messageSubmitRef.current = false
        setSubmittingMessage(false)
      }
    }

    // 降级：WS 未连接，走 REST
    setTyping(true)
    try {
      const response = await api.hiringWorkflow.sendConversationMessage(hireId, {
        content: text || '补充信息',
        materials: toConversationMaterials(incoming),
      })
      if (autoApprove && response.requiresAudit) {
        await api.hiringWorkflow.submitAuditDecision(hireId, {
          stage: response.latestPreview.stage,
          decision: HiringAuditDecision.Approve,
          comment: '前端自动审计通过',
        })
      }

      const assistantContent = normalizeAssistantReply(response.assistantMessage.content)
      if (!isInternalTurn && assistantContent) {
        typewriterStream.start()
        await new Promise<void>((resolve) => {
          typewriterStream.finish(assistantContent, (displayedReply) => {
            setMessages(prev => [...prev, {
              id: response.assistantMessage.messageId || mkId(),
              role: 'bot',
              content: displayedReply,
            }])
            resolve()
          })
        })
      }

      const endpoint = gatewayEndpointRef.current
      const sessionId = sessionIdRef.current
      if (endpoint && sessionId) {
        await restoreConversationFromSandboxHistory(endpoint, sessionId, 'always').catch(() => false)
      }

      setWorkflowError('')
      setWorkflowNotice('')
      return true
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
      return false
    } finally {
      setTyping(false)
      setStreamingTurnInternal(false)
      typewriterStream.reset()
      lastWsTurnInternalRef.current = false
      messageSubmitRef.current = false
      setSubmittingMessage(false)
    }
  }

  function handleContinueStageCollection() {
    if (!pendingStageConfirmation) {
      return
    }

    setWorkflowError('')
    setWorkflowNotice(pendingStageConfirmation.continueNotice)
    setPendingStageConfirmation(null)
  }

  async function handleConfirmStageAdvance() {
    if (!pendingStageConfirmation) {
      return
    }

    const pending = pendingStageConfirmation
    const submitted = await submitWorkflowMessage(pending.summary)
    if (!submitted) {
      return
    }

    if (pending.stage === HiringCollectionStage.External) {
      setWsStageOverrides(prev => {
        const next = new Map(prev)
        next.set(HiringCollectionStage.External, 'completed')
        return next
      })
    }

    setPendingStageConfirmation(null)
  }

  async function importExistingPackageFromRequest(): Promise<boolean> {
    if (!pendingPackageArtifact) {
      return false
    }

    await triggerCreate(pendingPackageArtifact)
    return true
  }

  async function handleSend() {
    if (isInteractionLocked || handleSendRef.current) return
    const text = input.trim()
    if (!text && pendingFiles.length === 0) return

    handleSendRef.current = true
    try {
      const incoming = pendingFiles.length ? [...pendingFiles] : []
      const skillStageApprovalRoute = resolveSkillStageApprovalRoute({
        text,
        incomingFileCount: incoming.length,
        skillGenerationState,
        ontologyProjectionState,
        hasSkillSummary: latestSkillSummaryRef.current !== null,
        hasProjectionResult: latestProjectionResultRef.current !== null,
      })
      const shouldConfirmSkillDefinition = skillStageApprovalRoute === 'confirm_skill_definition'
      const shouldLaunchProjectionPass = skillStageApprovalRoute === 'launch_projection_pass'
      const shouldLaunchSkillGeneration = skillStageApprovalRoute === 'launch_skill_generation'
      const shouldLaunchPackagingTestCases =
        incoming.length === 0 &&
        !shouldConfirmSkillDefinition &&
        !shouldLaunchProjectionPass &&
        !shouldLaunchSkillGeneration &&
        packagingTestCasesState?.status === 'waiting_confirm' &&
        isPackagingTestCasesApprovalMessage(text)
      const shouldSkipPackagingTestCases =
        incoming.length === 0 &&
        !shouldConfirmSkillDefinition &&
        !shouldLaunchProjectionPass &&
        !shouldLaunchSkillGeneration &&
        !shouldLaunchPackagingTestCases &&
        packagingTestCasesState?.status === 'waiting_confirm' &&
        isPackagingTestCasesSkipMessage(text)
      const hasPackagingContext =
        externalConfigCommittedSignatureRef.current.length > 0 ||
        packagingTestCasesState !== null ||
        messagesRef.current.some(message => {
          const artifactType = message.artifact?.artifactType
          return artifactType === 'packaging_testcases_ready' ||
            artifactType === 'packaging_testcases_done' ||
            artifactType === 'review_readiness' ||
            artifactType === 'review_report' ||
            artifactType === 'packaging_progress' ||
            artifactType === 'template_package'
        })
      const hasCompletedCoreSummaries =
        latestMaterialSummaryRef.current !== null &&
        latestSkillSummaryRef.current !== null &&
        latestExternalSummaryRef.current !== null
      const packagingRequestRoute = resolvePackagingRequestRoute({
        text,
        incomingFileCount: incoming.length,
        isBlockedByRequiredConfirmation: shouldConfirmSkillDefinition || shouldLaunchProjectionPass || shouldLaunchSkillGeneration,
        isBlockedByPackagingTestCaseGeneration: shouldLaunchPackagingTestCases,
        hasPendingPackageArtifact: pendingPackageArtifact !== null,
        packagingInProgress: packagingInProgressRef.current,
        hasReviewReport: latestReviewReportRef.current !== null,
        hasPackagingContext,
        hasCompletedCoreSummaries,
      })
      const shouldImportExistingPackage = packagingRequestRoute === 'import_existing_package'
      const shouldWaitForActivePackaging = packagingRequestRoute === 'wait_for_active_packaging'
      const shouldLaunchPackagingRequest = packagingRequestRoute === 'launch_packaging_request'

      setMessages(prev => [...prev, {
        id: mkId(),
        role: 'user',
        content: text,
        files: incoming.length > 0 ? incoming : undefined,
      }])
      setInput('')
      if (incoming.length > 0) {
        setPendingFiles([])
        setAllFiles(prev => [...prev, ...incoming])
      }
      if (shouldSkipPackagingTestCases) {
        setOptimisticDownstreamRun(
          'packaging-test-cases',
          'completed',
          '已跳过评估测试用例生成。',
          'packaging_testcases_skipped',
        )
      }

      const fallbackText = text || `上传文件：${incoming.map(file => file.name).join('、')}`
      let submitted = false
      if (shouldConfirmSkillDefinition) {
        submitted = await confirmSkillDefinitionFromApproval(fallbackText)
      } else if (shouldLaunchProjectionPass) {
        submitted = await launchProjectionPassFromApproval()
      } else if (shouldLaunchSkillGeneration) {
        submitted = await launchSkillGenerationFromApproval()
      } else if (shouldImportExistingPackage) {
        submitted = await importExistingPackageFromRequest()
      } else if (shouldWaitForActivePackaging) {
        setWorkflowNotice('数字员工包正在生成，请稍候。')
        submitted = true
      } else if (shouldLaunchPackagingRequest) {
        submitted = await submitWorkflowMessage(
          buildPackagingRequestPrompt(fallbackText, latestReviewReportRef.current),
          undefined,
          true,
          false,
          false,
        )
      } else if (shouldLaunchPackagingTestCases) {
        submitted = await launchPackagingTestCasesFromApproval()
      } else {
        submitted = await submitWorkflowMessage(
          fallbackText,
          incoming.length > 0 ? incoming : undefined,
          true,
          false, // handleSend 已经主动 setMessages，避免重复推用户气泡
        )
      }

      if (!submitted && incoming.length > 0) {
        setPendingFiles(prev => [...incoming, ...prev])
      }
      if (!submitted && shouldSkipPackagingTestCases) {
        setOptimisticDownstreamRun(
          'packaging-test-cases',
          'waiting_confirm',
          '等待确认是否生成评估测试用例。',
          'packaging_testcases_ready',
        )
      }
    } finally {
      handleSendRef.current = false
    }
  }

  const addPendingFiles = useCallback((fl: FileList | File[]) => {
    const files = Array.from(fl)
    const placeholders = files.map(file => {
      const id = mkId()
      rawFileMapRef.current.set(id, file)
      return {
        id,
        name: file.name,
        size: file.size,
        status: '解析中' as const,
        type: 'file' as const,
        mimeType: file.type || undefined,
      }
    })
    setPendingFiles(prev => [...prev, ...placeholders])

    void Promise.all(files.map(file => fileToChatFile(file, 'file'))).then(parsedFiles => {
      setPendingFiles(prev => prev.map(item => {
        const parsed = parsedFiles.find(file => file.name === item.name && file.size === item.size)
        return parsed ? { ...parsed, id: item.id } : item
      }))
    })
  }, [])

  async function handleSkillUploadSubmit(payload: SkillUploadPayload) {
    if (isInteractionLocked) {
      setWorkflowError(t('hiring.error.conversationInProgress'))
      return
    }
    const hireId = await ensureWorkflowReady()
    if (!hireId) return

    try {
      const details = [
        `Skill 名称：${payload.name}`,
        payload.releaseNote.trim() ? `版本说明：${payload.releaseNote.trim()}` : '',
        `技能描述：${payload.description.trim()}`,
      ].filter(Boolean)

      const uploaded = await api.hiringWorkflow.uploadMaterialFile(hireId, payload.file, {
        type: 'skill',
        skillName: payload.name,
        releaseNote: payload.releaseNote.trim(),
        description: payload.description.trim(),
        archiveFormat: 'zip',
      })
      const skillFile: ChatFile = {
        id: mkId(),
        name: uploaded.name,
        size: uploaded.size ?? payload.file.size,
        status: i18n.t('hiring.file.parsed') as '已解析',
        type: 'skill',
        mimeType: uploaded.mimeType ?? payload.file.type ?? undefined,
        content: undefined,
        metadata: uploaded.metadata ? { ...uploaded.metadata } : undefined,
        rawFile: payload.file,
      }

      setAllFiles(prev => [...prev, skillFile])
      setMessages(prev => [...prev, {
        id: mkId(),
        role: 'user',
        content: `已上传 Skill 包并提交信息\n${details.join('\n')}`,
        files: [skillFile],
      }])

      if (await submitWorkflowMessage(
        `已上传 Skill 包并提交信息\n${details.join('\n')}`,
        [skillFile],
        false, // autoApprove
        false, // 调用方上面已经 setMessages，避免重复
      )) {
        setShowSkillUploadModal(false)
      }
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    }
  }

  async function triggerCreate(
    packageArtifact?: { fileUrl: string; fileName: string },
    options?: { forceManual?: boolean },
  ) {
    const forceManual = options?.forceManual === true
    if (!forceManual && (!canCreate || instanceCreated)) return
    await turnSyncBarrierRef.current
    const hireId = await ensureWorkflowReady()
    if (!hireId) return

    if (!forceManual && requiresFreshPackaging) {
      setWorkflowError('')
      setWorkflowNotice(EXTERNAL_CONFIG_REPACKAGE_NOTICE)
      return
    }

    // 方案 A：只走 import-package，不再回退调 KingCrab finalize。
    // 不明确传入 packageArtifact 时，尝试从状态中拿上次推送的产物事件。
    const effectiveArtifact = packageArtifact ?? pendingPackageArtifact
    if (!effectiveArtifact) {
      setWorkflowError(t('hiring.error.pleaseRequestPackaging'))
      return
    }
    if (packageImportInFlightRef.current) {
      setWorkflowNotice(t('hiring.artifact.waitForFinalPackage'))
      return
    }
    if (!gatewayEndpointRef.current) {
      setWorkflowError(t('hiring.error.noGatewayEndpoint'))
      return
    }

    try {
      packageImportInFlightRef.current = true
      const artifact = effectiveArtifact
      const resolveGatewayFileUrl = (rawFileUrl: string): string => {
        const trimmedFileUrl = rawFileUrl.trim()
        if (/^https?:\/\//i.test(trimmedFileUrl)) {
          const parsed = new URL(trimmedFileUrl)
          const expectedProtocol = inferGatewayProtocol(parsed.host, 'https', 'http')
          if (parsed.protocol === 'http:' && expectedProtocol === 'https') {
            parsed.protocol = 'https:'
          }
          return parsed.toString()
        }

        const rawGateway = (gatewayEndpointRef.current ?? '').trim()
        if (!rawGateway) {
          throw new Error('网关地址为空，无法拼接附件下载 URL')
        }

        let normalizedBase: string
        if (/^https?:\/\//i.test(rawGateway)) {
          const parsedGateway = new URL(rawGateway)
          const expectedProtocol = inferGatewayProtocol(parsedGateway.host, 'https', 'http')
          if (parsedGateway.protocol === 'http:' && expectedProtocol === 'https') {
            parsedGateway.protocol = 'https:'
          }
          normalizedBase = parsedGateway.toString().replace(/\/$/, '')
        } else {
          const protocol = inferGatewayProtocol(rawGateway, 'https', 'http')
          normalizedBase = `${protocol}://${rawGateway.replace(/^\/+/, '').replace(/\/$/, '')}`
        }

        const fileUrlPath = trimmedFileUrl.startsWith('/')
          ? trimmedFileUrl
          : `/${trimmedFileUrl}`
        return `${normalizedBase}${fileUrlPath}`
      }

      // 前端从沙箱网关下载数字员工包后上传给后端 import-package，后端不再依赖 KingCrab finalize。
      // fileUrl 可能是绝对 URL（如 http://opensandbox-gateway.../media/xxx）或相对路径；
      // 绝对 URL 直接使用，相对路径则需拼接 gateway base。
      const fullUrl = resolveGatewayFileUrl(artifact.fileUrl)
      const accessToken = await tokenService.ensureFresh()
      const dlResp = await fetch(fullUrl, {
        headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      })
      if (!dlResp.ok) {
        throw new Error(`从沙箱网关下载数字员工包失败（HTTP ${dlResp.status}）`)
      }
      // 校验 Content-Type，防止网关返回 JSON / HTML 被误当 ZIP 上传
      const contentType = dlResp.headers.get('content-type') ?? ''
      if (contentType.includes('text/') || contentType.includes('application/json')) {
        const preview = await dlResp.text()
        throw new Error(`沙箱网关返回了非二进制响应（Content-Type: ${contentType}）：${preview.slice(0, 200)}`)
      }
      const packageBlob = await dlResp.blob()
      if (packageBlob.size < 22) {
        // ZIP 最小合法大小（End of Central Directory = 22 字节）
        throw new Error(`从沙箱网关下载的数字员工包过小（${packageBlob.size} 字节），可能不是有效 ZIP 文件`)
      }
      const finalizeResult = await api.hiringWorkflow.importPackage(
        hireId,
        packageBlob,
        artifact.fileName,
        linkedStoreSkillIds,
      )

      setArtifactFileNames(finalizeResult.generatedFiles)
      if (finalizeResult.employeeId) {
        setCreatedId(finalizeResult.employeeId)
      }
      // 后续下载统一走后端 final_package_zip，页面展示名由模板名计算。
      setInstanceCreated(true)
      setPendingStageConfirmation(null)
      setWorkflowError('')
      setWorkflowNotice('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    } finally {
      packageImportInFlightRef.current = false
    }
  }

  async function downloadBackendArtifact(artifactName: string) {
    if (!workflowHireId || !artifactName) return
    try {
      const artifact = await api.hiringWorkflow.downloadArtifactFile(workflowHireId, artifactName)
      downloadBlob(artifact.blob, artifact.fileName)
      setWorkflowError('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    }
  }

  async function downloadPersistedFinalPackage() {
    if (!workflowHireId) {
      setWorkflowNotice(t('hiring.artifact.waitForFinalPackage'))
      return
    }

    try {
      const artifact = await api.hiringWorkflow.downloadArtifacts(workflowHireId)
      downloadBlob(artifact.blob, artifact.fileName)
      setWorkflowError('')
      setWorkflowNotice('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    }
  }

  /** template_package 卡片：仅下载后端 final 交付包 */
  async function downloadTemplatePackageFinal() {
    if (!canDownloadFinalPackage || !workflowHireId) {
      setWorkflowNotice(t('hiring.artifact.waitForFinalPackage'))
      return
    }
    await downloadPersistedFinalPackage()
  }

  /**
   * 从沙箱 Gateway 下载文件（需要附带 Bearer token）。
   * fileUrl 可以是绝对 URL 或相对于 gateway endpoint 的路径。
   */
  async function downloadGatewayFile(fileUrl: string, fileName: string) {
    try {
      const trimmedFileUrl = fileUrl.trim()
      let fullUrl: string
      if (/^https?:\/\//i.test(trimmedFileUrl)) {
        const parsed = new URL(trimmedFileUrl)
        const expectedProtocol = inferGatewayProtocol(parsed.host, 'https', 'http')
        if (parsed.protocol === 'http:' && expectedProtocol === 'https') {
          parsed.protocol = 'https:'
        }
        fullUrl = parsed.toString()
      } else {
        const rawGateway = (gatewayEndpointRef.current ?? '').trim()
        if (!rawGateway) {
          throw new Error('网关地址为空，无法下载附件')
        }

        let normalizedBase: string
        if (/^https?:\/\//i.test(rawGateway)) {
          const parsedGateway = new URL(rawGateway)
          const expectedProtocol = inferGatewayProtocol(parsedGateway.host, 'https', 'http')
          if (parsedGateway.protocol === 'http:' && expectedProtocol === 'https') {
            parsedGateway.protocol = 'https:'
          }
          normalizedBase = parsedGateway.toString().replace(/\/$/, '')
        } else {
          const protocol = inferGatewayProtocol(rawGateway, 'https', 'http')
          normalizedBase = `${protocol}://${rawGateway.replace(/^\/+/, '').replace(/\/$/, '')}`
        }

        const urlPath = trimmedFileUrl.startsWith('/') ? trimmedFileUrl : `/${trimmedFileUrl}`
        fullUrl = `${normalizedBase}${urlPath}`
      }
      const accessToken = await tokenService.ensureFresh()
      const resp = await fetch(fullUrl, {
        headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      })
      if (!resp.ok) {
        throw new Error(`下载文件失败（HTTP ${resp.status}）`)
      }
      const blob = await resp.blob()
      downloadBlob(blob, fileName)
      setWorkflowError('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    }
  }

  /**
   * 点击「发起打包」按钮时：
   * - 若沙箱已推送过 template_package（pendingPackageArtifact 不为 null），直接复用产物触发导入
   * - 否则模拟用户发送打包请求消息，由 AI 调用 package_workspace → 发出 template_package artifact
   *   → useEffect 自动触发 triggerCreate()，形成闭环
   */
  async function handleRequestPackaging() {
    if (isInteractionLocked) return
    if (pendingStageConfirmation) {
      setWorkflowNotice(pendingStageConfirmation.prompt)
      return
    }
    if (pendingPackageArtifact) {
      void triggerCreate()
      return
    }
    const visibleRequest = '三个阶段均已确认完成，请开始生成数字员工'
    setMessages(prev => [...prev, {
      id: mkId(),
      role: 'user',
      content: visibleRequest,
    }])
    await submitWorkflowMessage(
      buildPackagingRequestPrompt(visibleRequest, latestReviewReportRef.current),
      undefined,
      true,
      false,
      true,
    )
  }

  function handlePrototypeContinue() {
    setJourneyGuideVisible(true)
    setFocusedStage(workflowCurrentStage)
    setWorkflowNotice('')
    composerRef.current?.focus()
  }

  function handleResetConversation() {
    if (resettingRef.current) return
    resettingRef.current = true
    setResetting(true)
    setWorkflowError('')
    setWorkflowNotice(t('hiring.notice.resetting'))

    void (async () => {
      try {
        const hireId = await ensureWorkflowReady()
        if (!hireId) { setResetting(false); resettingRef.current = false; return }

        // 断开旧 WebSocket
        wsRef.current?.disconnect()
        wsRef.current = null

        // 调用后端重置 API 创建新会话
        const resetResult = await api.hiringWorkflow.resetConversation(hireId)
        const newSessionId = resetResult.sessionId

        // 更新 session ref
        sessionIdRef.current = newSessionId
        autoTemplateBootstrapSessionRef.current = null

        // 清空前端状态
        setMessages([])
        setInput('')
        setPendingFiles([])
        setAllFiles([])
        typewriterStream.reset()
        setTyping(false)
        setStreamingTurnInternal(false)
        setJourneyGuideVisible(false)
        setFocusedStage(null)
        setWorkflowError('')
        setWorkflowNotice('')
        setWsStageOverrides(new Map())
        setDownstreamRuns({})
        setMaterialRequestedCategories([])
        setPendingPackageArtifact(null)
        setPendingStageConfirmation(null)
        setRequiresFreshPackaging(false)
        setArtifactFileNames([])
        setLinkedStoreSkillIds([])
        setLatestSkillSummary(null)
        downstreamRunsRef.current = {}
        packagingInProgressRef.current = false
        packageImportInFlightRef.current = false
        latestMaterialSummaryRef.current = null
        latestSkillSummaryRef.current = null
        latestProjectionResultRef.current = null
        latestExternalSummaryRef.current = null
        latestReviewReportRef.current = null
        materialSummarySignatureRef.current = ''
        skillSummarySignatureRef.current = ''
        externalSummarySignatureRef.current = ''
        projectionPassLaunchSignatureRef.current = ''
        skillGenerationLaunchSignatureRef.current = ''
        ontologyExtractionDoneSignatureRef.current = ''
        ontologyProjectionDoneSignatureRef.current = ''
        packagingTestCasesDoneSignatureRef.current = ''
        processedArtifactSignaturesRef.current.clear()
        pendingInternalPromptsRef.current = []
        lastWsTurnInternalRef.current = false
        internalPromptFlushInFlightRef.current = false
        postTurnHistoryRefreshRef.current = null
        turnSyncBarrierRef.current = Promise.resolve()

        // 清除后端运行时状态，确保重置后刷新页面不会恢复旧记录
        await Promise.all(
          RUNTIME_STATE_STAGE_SEQUENCE.map(stage =>
            api.hiringWorkflow.saveRuntimeStateByStage(hireId, stage, {}).catch(() => {}),
          ),
        )

        // 重新连接 WebSocket
        const endpoint = gatewayEndpointRef.current
        if (endpoint) {
          await connectSandboxWs(endpoint)
        }

        // 重置后自动重新注入模板包，直接重启引导流程
        if (templateId) {
          await autoBootstrapTemplateConversation(templateId, hireId)
        }

        setWorkflowNotice(t('hiring.notice.resetComplete'))
      } catch (error: unknown) {
        setWorkflowError(normalizeErrorMessage(error))
      } finally {
        setResetting(false)
        resettingRef.current = false
      }
    })()
  }

  function handleSelectStage(stage: HiringUiStage, blockedReason: string) {
    setJourneyGuideVisible(true)
    if (blockedReason) {
      setFocusedStage(workflowCurrentStage)
      setWorkflowNotice(blockedReason)
      return
    }

    setFocusedStage(stage)
    setWorkflowNotice('')
  }

  if (!templateId) {
    return <CenterState message={t('hiring.error.templateParamMissing')} />
  }
  if (templateLoading) {
    return <CenterState message={t('hiring.status.loadingTemplate')} />
  }
  if (!template) {
    return <CenterState message={templateError || t('hiring.error.templateNotFound')} />
  }

  const workflowStatus = (() => {
    if (workflowError) {
      return {
        title: t('hiring.status.workflowError'),
        detail: workflowError,
        tone: 'pink' as const,
        onRetry: retryWorkflowInitialization,
        retryDisabled: workflowBooting,
      }
    }

    if (workflowNotice.includes('自动导入模板包')) {
      return {
        title: t('hiring.status.parsingTemplate'),
        detail: t('hiring.status.parsingTemplateDetail'),
        tone: 'blue' as const,
      }
    }

    if (workflowBooting) {
      return {
        title: t('hiring.status.initializingWorkflow'),
        detail: t('hiring.status.connectingWorkflow'),
        tone: 'blue' as const,
      }
    }

    if (workflowNotice) {
      return {
        title: t('hiring.status.needsAttention'),
        detail: workflowNotice,
        tone: 'blue' as const,
      }
    }

    if (workflowReady) {
      return {
        title: t('hiring.status.sandboxConnected'),
        detail: t('hiring.status.readyToContinue'),
        tone: 'green' as const,
      }
    }

    return null
  })()

  return (
    <div className="hb-hiring-page hb-workflow-page">
      <HiringJourneyHeader
        templateName={introName}
        templateId={template.templateId}
        onReset={handleResetConversation}
        onContinue={handlePrototypeContinue}
        resetting={resetting}
      />

      <div className="hb-hiring-steps-card">
        <HiringStagePills
          steps={mergedStepPills}
          onSelectStage={handleSelectStage}
        />
      </div>

      <div className="hb-hiring-workspace">
        <HiringConversationPanel
          introName={introName}
          messages={messages}
          typing={visibleTyping}
          streamingContent={visibleStreamingContent}
          streamingToolSteps={streamingToolSteps}
          pendingFiles={pendingFiles}
          input={input}
          promptPlaceholder={viewModel.promptPlaceholder}
          disabled={isInteractionLocked}
          fileInputRef={fileRef}
          composerRef={composerRef}
          chatEndRef={chatEndRef}
          onInputChange={setInput}
          onSend={() => { void handleSend() }}
          onFileChange={addPendingFiles}
          onOpenSkillUpload={() => setShowSkillUploadModal(true)}
          onRemovePendingFile={(fileId) => setPendingFiles(prev => prev.filter(file => file.id !== fileId))}
          formatFileSize={formatFileSize}
          onArtifactFileDownload={(url, fileName, artifactType) => {
            if (artifactType === 'template_package') {
              void downloadTemplatePackageFinal()
              return
            }
            void downloadGatewayFile(url, fileName)
          }}
          onArtifactManualUpload={(url, fileName) => { void triggerCreate({ fileUrl: url, fileName }, { forceManual: true }) }}
          templatePackageDownloadFileName={finalPackageFileName || undefined}
          templatePackageDownloadDisabled={hasTemplatePackageArtifact && !canDownloadFinalPackage}
          templatePackageDownloadDisabledTitle={t('hiring.artifact.waitForFinalPackage')}
          workflowStatus={workflowStatus}
        />

        <div className="hb-hiring-right-col">
          <HiringProgressLedger
            stageCards={viewModel.stageCards}
            overallProgress={viewModel.overallProgress}
            actionState={mergedActionState}
            instanceCreated={instanceCreated}
            summaryItems={[{ label: '已上传文件', value: String(uploadedFileCount) }]}
            artifactFileNames={artifactFileNames}
            hasArtifactArchive={canDownloadFinalPackage}
            onContinue={handlePrototypeContinue}
            onFinalize={() => { void handleRequestPackaging() }}
            onDownloadArtifact={(artifactName) => { void downloadBackendArtifact(artifactName) }}
            onDownloadArchive={() => { void downloadPersistedFinalPackage() }}
          />

          {/* MCP TODO 交互面板：完全由 WS artifact 事件驱动阶段亮灯 */}
          <HiringTodoPanel
            hireId={workflowHireId}
            sessionId={sessionIdRef.current ?? ''}
            wsStageOverrides={uiStageOverrides}
            templateId={template?.templateId ?? templateId}
            templatePackageSkills={template?.packageSkills ?? []}
            requestedMaterialCategories={materialRequestedCategories}
            uploadedConversationFiles={uploadedConversationFiles}
            skillDefinitionStageStatus={uiStageOverrides.get(HiringCollectionStage.Skill) ?? null}
            skillGenerationState={skillStageConfirmationState}
            definedSkills={definedSkills}
            onExternalConfigChange={handleExternalConfigChange}
            pendingStageConfirmation={pendingStageConfirmation}
            onContinueStageCollection={handleContinueStageCollection}
            onConfirmStageAdvance={() => { void handleConfirmStageAdvance() }}
            stageConfirmationBusy={isInteractionLocked}
            onAfterStageMessage={handleAfterStageMessage}
            onGenerate={() => { void handleRequestPackaging() }}
            generated={instanceCreated}
            canDownloadFinalPackage={canDownloadFinalPackage}
            onDownloadFinalPackage={() => { void downloadTemplatePackageFinal() }}
            onEnterEvaluation={createdId ? () => navigate(`/department-employees/instances/${createdId}/evaluation`) : undefined}
            onLinkedSkillIdsChange={setLinkedStoreSkillIds}
            onConfirmSkillGeneration={() => { void handleConfirmSkillGeneration() }}
            onConfirmSkillStageDone={() => { void handleConfirmSkillStageDone() }}
            packageStructure={
              instanceCreated
                ? { fileName: finalPackageFileName, fileNames: artifactFileNames }
                : null
            }
          />
        </div>
      </div>

      <SkillUploadModal
        key={showSkillUploadModal ? 'skill-upload-open' : 'skill-upload-closed'}
        open={showSkillUploadModal}
        disabled={isInteractionLocked}
        onClose={() => setShowSkillUploadModal(false)}
        onSubmit={handleSkillUploadSubmit}
      />
    </div>
  )
}

function CenterState({ message }: { message: string }) {
  return (
    <div className="hb-page hb-workflow-page">
      <div className="hb-card hb-detail-state">
        {message}
      </div>
    </div>
  )
}

