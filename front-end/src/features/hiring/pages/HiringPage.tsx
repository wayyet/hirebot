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
  hasPendingDownstreamRuns,
} from './utils/hiringCacheNormalizers'
import {
  buildProjectionPassPayload,
  hasConsumableProducerProjection,
  buildSkillGenerationPayload,
  buildDownstreamPrompt,
  isSkillGenerationApprovalMessage,
  isPackagingTestCasesApprovalMessage,
  isPackagingTestCasesSkipMessage,
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
  shouldHoldExternalStageUntilSkillImplementation,
  resolveDownstreamRunFromArtifact,
  resolveHiringStageFromWs,
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
import { extractLatestMaterialRequestedCategories, normalizeMaterialRequestedCategories } from './materialRequestedCategories'

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
    artifactArchive,
    artifactFileNames,
    restoredPackageFileName,
    materialRequestedCategories,
    pendingPackageArtifact,
    pendingStageConfirmation,
    requiresFreshPackaging,
    linkedStoreSkillIds,
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
    setArtifactArchive,
    setArtifactFileNames,
    setRestoredPackageFileName,
    setMaterialRequestedCategories,
    setPendingPackageArtifact,
    setPendingStageConfirmation,
    setRequiresFreshPackaging,
    setLinkedStoreSkillIds,
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
  const visibleTyping = typing && !streamingTurnInternal

  // ── 计算属性（使用自定义 Hook） ────────────────────────────────────────────
  const computed = useHiringComputed({
    messages,
    wsStageOverrides,
    downstreamRuns,
    latestSkillSummary: null, // 将在后续 ref 中填充
    focusedStage,
    t,
    workflowHireId,
    instanceCreated,
    artifactArchive,
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
  const internalPromptFlushInFlightRef = useRef(false)
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

  function applyRestoredMessages(
    restoredMessages: ChatMessage[],
    requestedCategories = extractLatestMaterialRequestedCategories(restoredMessages),
  ) {
    setMessages(restoredMessages)
    messagesRef.current = restoredMessages
    setMaterialRequestedCategories(requestedCategories)

    latestMaterialSummaryRef.current = extractLatestMessageArtifactData(restoredMessages, 'material_handoff_summary')
    latestSkillSummaryRef.current = extractLatestMessageArtifactData(restoredMessages, 'skill_workorder_summary')
    latestProjectionResultRef.current = extractLatestMessageArtifactData(restoredMessages, 'ontology_projection_done')
    latestExternalSummaryRef.current = extractLatestMessageArtifactData(restoredMessages, 'external_workorder_summary')
    materialSummarySignatureRef.current = latestMaterialSummaryRef.current ? JSON.stringify(latestMaterialSummaryRef.current) : ''
    skillSummarySignatureRef.current = latestSkillSummaryRef.current ? JSON.stringify(latestSkillSummaryRef.current) : ''
    externalSummarySignatureRef.current = latestExternalSummaryRef.current ? JSON.stringify(latestExternalSummaryRef.current) : ''
    externalConfigCommittedSignatureRef.current = restoredMessages
      .filter(message => message.artifact?.artifactType === 'external_config_committed' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    ontologyExtractionDoneSignatureRef.current = restoredMessages
      .filter(message => message.artifact?.artifactType === 'ontology_extraction_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    ontologyProjectionDoneSignatureRef.current = restoredMessages
      .filter(message => message.artifact?.artifactType === 'ontology_projection_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    packagingTestCasesDoneSignatureRef.current = restoredMessages
      .filter(message => message.artifact?.artifactType === 'packaging_testcases_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''

    appendExternalConfigCommittedArtifact(latestExternalConfigRef.current)
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

      // 恢复最新产物包结构（不恢复 blob，仅恢复显示用的文件名和结构数据）
        if (state.packageStructure?.fileName) {
          setArtifactFileNames(state.packageStructure.fileNames ?? [])
        // 无 blob 时仅设 fileName 以便 FinalCard 显示包名；artifactArchive blob 留 null
        // canDownloadFinalPackage 依赖 artifactArchive.blob，刷新后不可下载但可显示包名
          setRestoredPackageFileName(state.packageStructure.fileName)
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
    mode: 'always' | 'if-longer' = 'always',
  ): Promise<boolean> {
    const sandboxMessages = await fetchSandboxSessionMessages(endpoint, sessionId)
    if (sandboxMessages.length === 0) {
      return false
    }

    const restored = buildHistoricalHiringConversationState(sandboxMessages, normalizeAssistantReply)
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

  const skillGenerationState = downstreamRuns['skill-generation'] ?? null
  const packagingTestCasesState = downstreamRuns['packaging-test-cases'] ?? null

  const handleExternalConfigChange = useCallback((
    config: HiringExternalSystemConfig | null,
    source: ExternalConfigChangeSource = 'hydrate',
  ) => {
    const previousConfig = latestExternalConfigRef.current
    latestExternalConfigRef.current = config
    if (shouldRequireFreshPackagingAfterExternalConfigChange(previousConfig, config, source, instanceCreated)) {
      setPendingPackageArtifact(null)
      setArtifactArchive(null)
      setArtifactFileNames([])
      setRestoredPackageFileName('')
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
  useRuntimeStateSync(workflowHireId, wsStageOverrides, downstreamRuns, allFiles, artifactArchive, artifactFileNames, createdId)
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
  // downstreamRuns 加入依赖：下游任务完成后重新检查，确保延迟暂存的产物包也能自动导入
  useEffect(() => {
    if (!pendingPackageArtifact || !workflowHireId || instanceCreated) return
    if (pendingStageConfirmation) return
    if (hasPendingDownstreamRuns(downstreamRunsRef.current)) return

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
  }, [pendingPackageArtifact, workflowHireId, instanceCreated, pendingStageConfirmation, downstreamRuns])

  const introName = template?.name ?? t('hiring.intro.digitalEmployee')
  const introAbilities = template?.coreAbilities.slice(0, 3).join('、') || t('hiring.intro.defaultAbilities')

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
      `${marker}`,
      `模板包已解压到工作区目录（文件：${uploadedFileName}，模板名：${templateName}）。`,
      '',
      useCaseSection,
      '',
      '请读取上述工作区目录中的 manifest.json，按照 SKILL.md 的"会话初始化"步骤完成初始化（文件已就绪，无需解压），然后执行以下动作：',
      'A. 静默调用 `emit_artifact` 推送 stage1 progress（artifactType=material_collection_progress, stage=stage1_material, isTerminal=false），data.requested_categories 必须包含 1-3 个开场白中提到的建议上传资料分类。这是内部系统调用，不要在回复中提及。',
      'B. 只用一句自然的话邀请我上传或描述业务资料，按 story-driven 风格开口，点到这 1-3 个分类即可。',
      '',
      '重要约束：',
      '- 不要输出任何系统状态确认语句（如"已确认工作区可用""执行阶段 1""强制动作"等内部步骤名称）。',
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
        const rawName = String(rawMsg.tool_name ?? rawMsg.name ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const textStr = String(rawMsg.text ?? '')
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
            const isError = Boolean(rawMsg.is_error ?? rawMsg.isError)
            const next = list.slice()
            next[targetIdx] = {
              ...next[targetIdx],
              status: isError ? 'error' : 'done',
              result: textStr || next[targetIdx].result,
            }
            pendingToolStepsRef.current = next
            setStreamingToolSteps([...next])
          }
        }
      } else if (type === 'artifact') {
        // 下游 skill 通过 emit_artifact 工具推送产物（对应 contracts/artifacts.json 声明的类型）
        const raw = msg.artifact as Record<string, unknown> | null | undefined
        if (raw) {
          const kind = (String(raw.kind ?? 'data')) as 'file' | 'data'
          const artifactType = String(raw.artifactType ?? raw.artifact_type ?? 'generic')
          const label = raw.label != null ? String(raw.label) : undefined
          const skillName = raw.skillName != null ? String(raw.skillName) : undefined
          const stage = raw.stage != null ? String(raw.stage) : undefined
          const isTerminal = Boolean(raw.isTerminal ?? raw.is_terminal)
          const displayHint = raw.displayHint != null ? String(raw.displayHint) : raw.display_hint != null ? String(raw.display_hint) : undefined
          const artifactData: ArtifactDisplayData = { kind, artifactType, label, skillName, stage, isTerminal, displayHint }
          if (kind === 'file') {
            artifactData.fileUrl = String(raw.fileUrl ?? raw.file_url ?? '')
            artifactData.fileName = String(raw.fileName ?? raw.file_name ?? label ?? 'file')
            artifactData.mimeType = String(raw.mimeType ?? raw.mime_type ?? '')
            const sizeBytes = typeof raw.fileSizeBytes === 'number' ? raw.fileSizeBytes : typeof raw.file_size_bytes === 'number' ? raw.file_size_bytes : null
            artifactData.sizeLabel = sizeBytes !== null ? formatFileSize(sizeBytes) : ''
          } else {
            if (raw.data != null) {
              artifactData.data = typeof raw.data === 'string' ? JSON.parse(raw.data as string) : raw.data
            } else {
              // 兜底：Gateway 部分版本将 data 字段平铺在 artifact 顶层而非嵌套在 data 字段下
              // 历史重建路径（tool call arguments）始终有嵌套 data 字段，WS 推送有时没有
              const META_KEYS = new Set(['kind', 'artifactType', 'artifact_type', 'label', 'skillName', 'skill_name', 'stage', 'isTerminal', 'is_terminal', 'displayHint', 'display_hint'])
              const fallback: Record<string, unknown> = {}
              for (const [k, v] of Object.entries(raw)) {
                if (!META_KEYS.has(k)) fallback[k] = v
              }
              artifactData.data = Object.keys(fallback).length > 0 ? fallback : undefined
            }
          }
          if (artifactType === 'material_collection_progress') {
            const categories = normalizeMaterialRequestedCategories(artifactData.data)
            if (categories.length > 0) {
              setMaterialRequestedCategories(categories)
            }
          }
          if (artifactType === 'material_handoff_summary' && kind === 'data' && isTerminal) {
            latestMaterialSummaryRef.current = artifactData.data ?? null
            const signature = JSON.stringify(artifactData.data ?? {})
            if (materialSummarySignatureRef.current !== signature) {
              materialSummarySignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildDownstreamPrompt('ontology-extraction', artifactData.data ?? {}),
              )
            }
          }
          if (artifactType === 'ontology_extraction_done' && kind === 'data' && isTerminal) {
            const signature = JSON.stringify(artifactData.data ?? {})
            if (ontologyExtractionDoneSignatureRef.current !== signature) {
              ontologyExtractionDoneSignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildCoachResumePrompt('post-ontology-extraction', {
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
              if (latestSkillSummaryRef.current !== null) {
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
            latestProjectionResultRef.current = null
            skillSummarySignatureRef.current = JSON.stringify(artifactData.data ?? {})
            projectionPassLaunchSignatureRef.current = ''
            skillGenerationLaunchSignatureRef.current = ''
            ontologyProjectionDoneSignatureRef.current = ''
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
          if (!duplicateExternalConfigCommitted) {
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
              if ((artifactType === 'external_workorder_summary' || artifactType === 'external_config_committed') && isTerminal) {
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
            // 无论是否有下游任务未完成，均暂存产物包信息；
          // useEffect 会在 downstreamRuns 全部完成后自动触发 triggerCreate。
          setRequiresFreshPackaging(false)
          setPendingPackageArtifact({ fileUrl: artifactData.fileUrl, fileName: artifactData.fileName ?? 'artifacts.zip' })
          if (hasPendingDownstreamRuns(downstreamRunsRef.current)) {
            setWorkflowNotice('已收到产物包，下游生成完成后将自动导入。')
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
          const shouldSuppressStageGate =
            stageGate.canProceed &&
            resolveHiringStageFromWs(stageGate.skillName, stageGate.completedStage) === HiringCollectionStage.Skill &&
            resolveHiringStageFromWs(stageGate.skillName, stageGate.nextStage) === HiringCollectionStage.External &&
            shouldHoldExternalStageUntilSkillImplementation(
              latestSkillSummaryRef.current,
              downstreamRunsRef.current['skill-generation'] ?? null,
            )
          if (shouldSuppressStageGate) {
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

      if (typing || messageSubmitRef.current || pendingInternalPromptsRef.current.length === 0) {
        return
      }

      const prompt = pendingInternalPromptsRef.current.shift()
      if (!prompt) return

      await submitWorkflowMessage(prompt, undefined, true, false, true)
    } finally {
      internalPromptFlushInFlightRef.current = false
    }
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

  async function requestProjectionBindingConfirmation(projectionResult: unknown): Promise<boolean> {
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
      setWorkflowNotice(
        hasConsumableProducerProjection(projectionResult)
          ? '技能所需业务资料已准备好，正在回到雇佣教练等待你确认是否采用。'
          : '当前还没有可用于技能生成的业务资料，正在回到雇佣教练引导下一步。',
      )
    }

    return submitted
  }

  async function launchSkillGenerationFromProjectionConfirmation(): Promise<boolean> {
    const currentRun = downstreamRunsRef.current['skill-generation']
    if (currentRun?.status === 'running') {
      setWorkflowNotice('资料采用已确认，技能实现正在生成中。')
      return true
    }

    if (currentRun?.status === 'completed') {
      setWorkflowNotice('采用已整理资料的技能实现已生成完成。')
      return true
    }

    const summary = latestSkillSummaryRef.current
    const projectionResult = latestProjectionResultRef.current
    const payload = buildSkillGenerationPayload(summary, projectionResult)
    if (!payload) {
      setWorkflowError('当前没有可用于技能生成的业务资料，暂时不能启动本轮技能生成。')
      return false
    }

    const signature = JSON.stringify({
      skillSummary: summary,
      projectionResult,
      projection_binding_confirmed: true,
    })
    if (signature && skillGenerationLaunchSignatureRef.current === signature) {
      setWorkflowNotice('资料采用已确认，正在等待技能实现进度更新。')
      return true
    }

    skillGenerationLaunchSignatureRef.current = signature
    setOptimisticDownstreamRun(
      'skill-generation',
      'running',
      '已确认采用已整理业务资料，正在启动技能实现。',
      'skill_generation_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('skill-generation', payload),
      undefined,
      true,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已确认采用已整理业务资料，正在生成技能实现。')
      return true
    }

    skillGenerationLaunchSignatureRef.current = ''
    clearDownstreamRun('skill-generation')
    return false
  }

  async function launchProjectionPassFromApproval(): Promise<boolean> {
    const summary = latestSkillSummaryRef.current
    if (!summary) return false

    const projectionRun = downstreamRunsRef.current['ontology-projection']
    const signature = skillSummarySignatureRef.current || JSON.stringify(summary)
    if (projectionRun?.status === 'running') {
      setWorkflowNotice('正在为技能准备业务资料，请等待进度更新。')
      return true
    }

    if (projectionRun?.status === 'completed' && skillGenerationState?.artifactType === 'skill_generation_ready') {
      return requestProjectionBindingConfirmation(
        latestProjectionResultRef.current ?? projectionRun.data ?? {},
      )
    }

    if (signature && projectionPassLaunchSignatureRef.current === signature) {
      setWorkflowNotice('正在为技能准备业务资料，请等待进度更新。')
      return true
    }

    const projectionPayload = buildProjectionPassPayload(summary)
    if (!projectionPayload) {
      setWorkflowError('技能定义摘要缺少业务资料准备所需字段，暂时无法启动。')
      return false
    }

    if (signature) {
      projectionPassLaunchSignatureRef.current = signature
    }

    setOptimisticDownstreamRun(
      'ontology-projection',
      'running',
      '正在为技能准备业务资料，等待下游进度更新。',
      'ontology_projection_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('ontology-projection', projectionPayload),
      undefined,
      true,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已开始为技能准备业务资料；完成后会先回到雇佣教练等待你的二次确认。')
      return true
    }

    projectionPassLaunchSignatureRef.current = ''
    clearDownstreamRun('ontology-projection')
    return false
  }

  async function launchPackagingTestCasesFromApproval(): Promise<boolean> {
    const currentRun = downstreamRunsRef.current['packaging-test-cases']
    if (currentRun?.status === 'running') {
      setWorkflowNotice('评估测试用例生成已启动，请等待进度更新。')
      return true
    }

    if (currentRun?.status === 'completed') {
      setWorkflowNotice('评估测试用例已生成，可以继续生成实例包。')
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
    )

    if (submitted) {
      setWorkflowNotice('已开始生成评估测试用例，完成后可继续生成实例包。')
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

  async function handleSend() {
    if (isInteractionLocked || handleSendRef.current) return
    const text = input.trim()
    if (!text && pendingFiles.length === 0) return

    handleSendRef.current = true
    try {
      const incoming = pendingFiles.length ? [...pendingFiles] : []
      const shouldLaunchProjectionPass =
        incoming.length === 0 &&
        skillGenerationState?.status === 'waiting_confirm' &&
        skillGenerationState?.artifactType !== 'skill_projection_binding_ready' &&
        isSkillGenerationApprovalMessage(text) &&
        latestSkillSummaryRef.current !== null
      const shouldLaunchSkillGeneration =
        incoming.length === 0 &&
        skillGenerationState?.status === 'waiting_confirm' &&
        skillGenerationState?.artifactType === 'skill_projection_binding_ready' &&
        isSkillGenerationApprovalMessage(text) &&
        latestSkillSummaryRef.current !== null
      const shouldLaunchPackagingTestCases =
        incoming.length === 0 &&
        !shouldLaunchProjectionPass &&
        !shouldLaunchSkillGeneration &&
        packagingTestCasesState?.status === 'waiting_confirm' &&
        isPackagingTestCasesApprovalMessage(text)
      const shouldSkipPackagingTestCases =
        incoming.length === 0 &&
        !shouldLaunchProjectionPass &&
        !shouldLaunchSkillGeneration &&
        !shouldLaunchPackagingTestCases &&
        packagingTestCasesState?.status === 'waiting_confirm' &&
        isPackagingTestCasesSkipMessage(text)

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
      const submitted = shouldLaunchProjectionPass
        ? await launchProjectionPassFromApproval()
        : shouldLaunchSkillGeneration
          ? await launchSkillGenerationFromProjectionConfirmation()
        : shouldLaunchPackagingTestCases
          ? await launchPackagingTestCasesFromApproval()
          : await submitWorkflowMessage(
            fallbackText,
            incoming.length > 0 ? incoming : undefined,
            true,
            false, // handleSend 已经主动 setMessages，避免重复推用户气泡
          )

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
    if (!gatewayEndpointRef.current) {
      setWorkflowError(t('hiring.error.noGatewayEndpoint'))
      return
    }

    try {
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

      // 前端从沙箱网关下载产物包后上传给后端 import-package，后端不再依赖 KingCrab finalize。
      // fileUrl 可能是绝对 URL（如 http://opensandbox-gateway.../media/xxx）或相对路径；
      // 绝对 URL 直接使用，相对路径则需拼接 gateway base。
      const fullUrl = resolveGatewayFileUrl(artifact.fileUrl)
      const accessToken = await tokenService.ensureFresh()
      const dlResp = await fetch(fullUrl, {
        headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      })
      if (!dlResp.ok) {
        throw new Error(`从沙箱网关下载产物包失败（HTTP ${dlResp.status}）`)
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
        throw new Error(`从沙箱网关下载的产物包过小（${packageBlob.size} 字节），可能不是有效 ZIP 文件`)
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
      // 后续下载统一走后端 final_package_zip，避免继续使用沙箱原始 ZIP 缓存。
      setArtifactArchive(null)
      setInstanceCreated(true)
      setWorkflowError('')
      setWorkflowNotice('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
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
      setArtifactArchive(artifact)
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
    return
    if (artifactArchive) {
      downloadBlob(artifactArchive!.blob, artifactArchive!.fileName)
      setWorkflowNotice('')
      return
    }
    // 页面刷新后产物包 Blob 会丢失,需要重新从沙箱下载并导入
    setWorkflowNotice('产物包未缓存,请从对话产物中重新导入')
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
    await submitWorkflowMessage('三个阶段均已确认完成，请开始生成产物包', undefined, true, true)
  }

  function handlePrototypeContinue() {    setJourneyGuideVisible(true)
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
        setArtifactArchive(null)
        setArtifactFileNames([])
        setRestoredPackageFileName('')
        setLinkedStoreSkillIds([])
        downstreamRunsRef.current = {}
        latestMaterialSummaryRef.current = null
        latestSkillSummaryRef.current = null
        latestProjectionResultRef.current = null
        latestExternalSummaryRef.current = null
        materialSummarySignatureRef.current = ''
        skillSummarySignatureRef.current = ''
        externalSummarySignatureRef.current = ''
        projectionPassLaunchSignatureRef.current = ''
        skillGenerationLaunchSignatureRef.current = ''
        ontologyExtractionDoneSignatureRef.current = ''
        ontologyProjectionDoneSignatureRef.current = ''
        packagingTestCasesDoneSignatureRef.current = ''
        pendingInternalPromptsRef.current = []
        lastWsTurnInternalRef.current = false
        internalPromptFlushInFlightRef.current = false
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
          introAbilities={introAbilities}
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
            createdId={createdId}
            summaryItems={[{ label: '已上传文件', value: String(uploadedFileCount) }]}
            artifactFileNames={artifactFileNames}
            hasArtifactArchive={canDownloadFinalPackage}
            onContinue={handlePrototypeContinue}
            onFinalize={() => { void handleRequestPackaging() }}
            onEnterTraining={(employeeId) => navigate(`/department-employees/instances/${employeeId}/training`)}
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
            skillDefinitionStageStatus={wsStageOverrides.get(HiringCollectionStage.Skill) ?? null}
            skillGenerationState={skillGenerationState}
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
            packageStructure={
              artifactArchive
                ? { fileName: artifactArchive.fileName, fileNames: artifactFileNames }
                : restoredPackageFileName
                  ? { fileName: restoredPackageFileName, fileNames: artifactFileNames }
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

