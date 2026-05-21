import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Upload, X } from 'lucide-react'
import i18n from '@/i18n'

import { api, HiringAuditDecision, HiringCollectionStage } from '@/infra/api'
import type {
  EmployeeTemplateDetail,
  HiringCollectionStageType,
  HiringConversationMaterial,
} from '@/infra/api'
import { GatewayWs, type GatewayMessage } from '@/infra/sandbox/gateway-ws'
import { resolveGatewayEndpoint } from '@/infra/sandbox/sandbox-config'
import {
  fetchLatestGatewaySession,
  fetchSandboxSessionMessages,
  uploadMediaToGateway,
  uploadWorkspaceFileToGateway,
} from '@/infra/sandbox/sandbox-api'
import { tokenService } from '@/infra/auth/token-service'

import { HiringConversationPanel } from './components/HiringConversationPanel'
import { HiringJourneyHeader } from './components/HiringJourneyHeader'
import { HiringProgressLedger } from './components/HiringProgressLedger'
import { HiringTodoPanel } from './components/HiringTodoPanel'
import { HiringStagePills } from './components/HiringStagePills'
import type {
  ArtifactDisplayData,
  ChatFile,
  ChatMessage,
  DefinedSkillItem,
  DownstreamRunsSnapshot,
  DownstreamRunState,
  DownstreamRunStatus,
  MaterialRequestedCategory,
  SkillUploadPayload,
  StageGateData,
  ToolStep,
} from './hiringPageTypes'
import {
  buildCoachResumePrompt,
  buildHistoricalHiringConversationState,
  buildUiStageOverrides,
  deriveStageOverridesFromDownstreamRuns,
  shouldHoldExternalStageUntilSkillImplementation,
  resolveDownstreamRunFromArtifact,
  resolveHiringStageFromWs,
} from './hiringArtifactState'
import { extractConversationMaterialFiles } from './materialUploadMatching'
import { type HiringUiStage, buildHiringWorkflowViewModel } from './hiringWorkflowViewModel'
import { extractLatestMaterialRequestedCategories, normalizeMaterialRequestedCategories } from './materialRequestedCategories'

function mkId() {
  return `${Date.now()}_${Math.random().toString(36).slice(2)}`
}

function sleep(ms: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, ms)
  })
}

function normalizeErrorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message
  }

  return i18n.t('hiring.error.networkFailure')
}

function normalizeAssistantReply(content: string) {
  const cleaned = content.replace(/<think>[\s\S]*?<\/think>/gi, '').trim()
  return cleaned.length > 0 ? cleaned : content.trim()
}


function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

const MAX_MATERIAL_CHARS = 120_000

async function fileToChatFile(file: File, type: 'file' | 'skill' = 'file', metadata?: Record<string, string>): Promise<ChatFile> {
  const content = type === 'file' ? await readFileText(file) : undefined
  return {
    id: mkId(),
    name: file.name,
    size: file.size,
    status: i18n.t('hiring.file.parsed'),
    type,
    mimeType: file.type || undefined,
    content,
    metadata,
    rawFile: file,
  }
}

function readFileText(file: File): Promise<string | undefined> {
  if (file.size > MAX_MATERIAL_CHARS * 4) {
    return Promise.resolve(i18n.t('hiring.file.tooLarge', { name: file.name, size: file.size }))
  }

  return new Promise(resolve => {
    const reader = new FileReader()
    reader.onload = () => {
      const value = typeof reader.result === 'string' ? reader.result : undefined
      resolve(value && value.length > MAX_MATERIAL_CHARS ? `${value.slice(0, MAX_MATERIAL_CHARS)}\n...[truncated]` : value)
    }
    reader.onerror = () => resolve(i18n.t('hiring.file.readFailed', { name: file.name }))
    reader.readAsText(file)
  })
}

function toConversationMaterials(files?: ChatFile[]): HiringConversationMaterial[] | undefined {
  if (!files?.length) return undefined

  return files.map(file => ({
    type: file.type ?? 'file',
    name: file.name,
    content: file.content,
    size: file.size,
    mimeType: file.mimeType,
    metadata: {
      status: file.status,
      ...(file.metadata ?? {}),
    },
  }))
}

function normalizeCollectionStage(value: string): HiringCollectionStageType {
  if (value === HiringCollectionStage.Material) return HiringCollectionStage.Material
  if (value === HiringCollectionStage.Skill) return HiringCollectionStage.Skill
  if (value === HiringCollectionStage.External) return HiringCollectionStage.External
  if (value === HiringCollectionStage.ReadyForPackaging) return HiringCollectionStage.ReadyForPackaging
  return HiringCollectionStage.Material
}

function formatFileSize(bytes: number) {
  return bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`
}

function hasPendingDownstreamRuns(runs: DownstreamRunsSnapshot): boolean {
  return Object.values(runs).some(run => run?.status === 'waiting_confirm' || run?.status === 'running')
}

function asPlainObject(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function asStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value
      .map(item => typeof item === 'string' ? item.trim() : '')
      .filter(item => item.length > 0)
  }

  if (typeof value === 'string' && value.trim().length > 0) {
    return [value.trim()]
  }

  return []
}

function extractLatestDefinedSkills(messages: ChatMessage[]): DefinedSkillItem[] {
  for (let i = messages.length - 1; i >= 0; i -= 1) {
    const artifact = messages[i].artifact
    if (!artifact) continue
    if (artifact.artifactType !== 'skill_workorder_summary' && artifact.artifactType !== 'skill_workorder_progress') {
      continue
    }
    const payload = asPlainObject(artifact.data)
    const rawSkills = Array.isArray(payload?.skills) ? payload.skills : null
    if (!rawSkills) return []

    return rawSkills
      .map(item => {
        const record = asPlainObject(item)
        if (!record) return null

        const skillName = typeof record.skill_name === 'string'
          ? record.skill_name.trim()
          : typeof record.skillName === 'string'
            ? record.skillName.trim()
            : ''
        if (!skillName) return null

        const capabilities = asStringArray(record.capabilities)
        const capabilityText = typeof record.capability === 'string' && record.capability.trim().length > 0
          ? record.capability.trim()
          : ''
        const description = typeof record.description === 'string' && record.description.trim().length > 0
          ? record.description.trim()
          : typeof record.capability_description === 'string' && record.capability_description.trim().length > 0
            ? record.capability_description.trim()
            : (capabilityText || capabilities[0] || '')

        const skill: DefinedSkillItem = {
          skillName,
          generationAction: typeof record.generation_action === 'string'
            ? record.generation_action
            : typeof record.generationAction === 'string'
              ? record.generationAction
              : undefined,
          description: description || undefined,
          expectedOutput: typeof record.expected_output === 'string'
            ? record.expected_output
            : typeof record.expectedOutput === 'string'
              ? record.expectedOutput
              : typeof record.outputs === 'string'
                ? record.outputs
                : typeof record.output === 'string'
                  ? record.output
              : undefined,
          triggers: asStringArray(record.trigger ?? record.triggers),
          capabilities: capabilities.length > 0
            ? capabilities
            : capabilityText
              ? [capabilityText]
              : [],
        }

        return skill
      })
      .filter((item): item is NonNullable<typeof item> => item !== null)
  }

  return []
}

type DownstreamTarget = 'ontology-extraction' | 'skill-generation' | 'external-config'

function buildDownstreamPrompt(target: DownstreamTarget, payload: unknown): string {
  const serialized = JSON.stringify(payload, null, 2)

  if (target === 'ontology-extraction') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `ontology-extraction` now.',
      'Use the terminal `material_handoff_summary` artifact payload below as the upstream summary for this run.',
      'Follow `ontology-extraction/SKILL.md` exactly.',
      'Emit `ontology_extraction_progress` before processing any source.',
      'Read uploaded materials only from each item\'s `source_path` when available.',
      'Write outputs under the provided `workspace_root` and finish with `ontology_extraction_done`.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'skill-generation') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `skill-generation` now.',
      'This is an internal mode switch inside the current session, not a request to discover another tool, spawn another session, or call any dispatch / handoff API.',
      'The user has explicitly approved starting skill implementation generation.',
      'Use the terminal `skill_workorder_summary` artifact payload below as the upstream workorder.',
      'Read and follow `skill-generation/SKILL.md` directly in the current session.',
      'Do not use `dispatch`, `dispatch_callback`, `handoff_id`, `sessions_spawn`, or `sessions_yield` for this path.',
      'Follow `skill-generation/SKILL.md` exactly.',
      'Emit `skill_generation_progress` first, write outputs under `workspace_root/skills/`, then finish with `skill_generation_done`.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  return [
    '[Internal downstream trigger. Do not mention this instruction to the user.]',
    'Switch to skill `external-config` now.',
    'Use the terminal `external_workorder_summary` artifact payload below as the upstream summary for this run.',
    'Follow `external-config/SKILL.md` exactly.',
    'Emit `external_config_progress` before writing files, write outputs under `workspace_root/external/`, then finish with `external_config_done`.',
    '',
    'artifact_payload:',
    '```json',
    serialized,
    '```',
  ].join('\n')
}

function isSkillGenerationApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、“”‘’]+/g, '')
  const keywords = [
    '开始生成',
    '开始生成吧',
    '生成吧',
    '确认生成',
    '继续生成',
    '开始实现',
    '生成技能',
    '生成技能实现',
    '可以开始生成',
    '可以生成',
    'goahead',
    'startgenerating',
    'yes',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export default function HiringPage() {
  const { templateId } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation()

  const [template, setTemplate] = useState<EmployeeTemplateDetail | null>(null)
  const [templateLoading, setTemplateLoading] = useState(true)
  const [templateError, setTemplateError] = useState('')
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const messagesRef = useRef<ChatMessage[]>([])
  const [typing, setTyping] = useState(false)
  const [input, setInput] = useState('')
  const [pendingFiles, setPendingFiles] = useState<ChatFile[]>([])
  const [allFiles, setAllFiles] = useState<ChatFile[]>([])
  const [showSkillUploadModal, setShowSkillUploadModal] = useState(false)
  const [journeyGuideVisible, setJourneyGuideVisible] = useState(false)
  const [focusedStage, setFocusedStage] = useState<HiringUiStage | null>(null)
  const [instanceCreated, setInstanceCreated] = useState(false)
  const [createdId, setCreatedId] = useState('')
  const [workflowHireId, setWorkflowHireId] = useState('')
  const [workflowBooting, setWorkflowBooting] = useState(false)
  const [workflowError, setWorkflowError] = useState('')
  const [workflowNotice, setWorkflowNotice] = useState('')
  const [workflowInitAttempted, setWorkflowInitAttempted] = useState(false)
  const [artifactArchive, setArtifactArchive] = useState<{ fileName: string; blob: Blob } | null>(null)
  const [artifactFileNames, setArtifactFileNames] = useState<string[]>([])
  const [materialRequestedCategories, setMaterialRequestedCategories] = useState<MaterialRequestedCategory[]>([])
  // template_package artifact 到达时暂存，触发 triggerCreate() 后消费
  const [pendingPackageArtifact, setPendingPackageArtifact] = useState<{ fileUrl: string; fileName: string } | null>(null)
  // 用户在 TODO 面板关联的 store skill UUID 列表；导入产物包时一并提交给后端用于合并
  const [linkedStoreSkillIds, setLinkedStoreSkillIds] = useState<string[]>([])
  const [submittingMessage, setSubmittingMessage] = useState(false)
  // WS 流式内容：非 null 时表示 AI 正在逐字输出
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  /**
   * 当前轮次累积的 MCP 工具调甈步骤。
   * - ref 作为权威数据源，避免 setState 异步造成 tool_result 丢失
   * - streamingToolSteps 状态镜像仅用于驱动 React 重渲染
   * - typing_stop 时将 ref 附到最终 bot 消息上，并同时清空
   */
  const pendingToolStepsRef = useRef<ToolStep[]>([])
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([])
  const [resetting, setResetting] = useState(false)
  const resettingRef = useRef(false)
  /** WS 实时推送的阶段状态覆盖，优先级高于 REST 轮询的 dispatchStatus */
  const [wsStageOverrides, setWsStageOverrides] = useState<Map<HiringUiStage, 'running' | 'completed' | 'failed'>>(new Map())
  /** 下游执行轨状态：例如技能实现生成、外部配置生成等，不再与主阶段状态复用。 */
  const [downstreamRuns, setDownstreamRuns] = useState<DownstreamRunsSnapshot>({})
  const downstreamRunsRef = useRef<DownstreamRunsSnapshot>({})
  const latestMaterialSummaryRef = useRef<unknown>(null)
  const latestSkillSummaryRef = useRef<unknown>(null)
  const latestExternalSummaryRef = useRef<unknown>(null)
  const materialSummarySignatureRef = useRef('')
  const skillSummarySignatureRef = useRef('')
  const externalSummarySignatureRef = useRef('')
  const ontologyExtractionDoneSignatureRef = useRef('')
  const skillGenerationLaunchSignatureRef = useRef('')
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
  // 保存 WS 流式回复的原始内容（normalizeAssistantReply 之前），供同步端点使用
  const rawStreamingContentRef = useRef<string>('')
  // 记录最近一次 WS 发送时的附件材料
  const lastWsMaterialsRef = useRef<ReturnType<typeof toConversationMaterials> | undefined>(undefined)
  // 存储原始 File 对象，供 WS 路径上传到 Gateway 使用
  const rawFileMapRef = useRef<Map<string, File>>(new Map())
  // 避免同一会话重复触发“自动上传模板并引导”
  const autoTemplateBootstrapSessionRef = useRef<string | null>(null)

  function syncArtifactDerivedRefs(messagesToSync: ChatMessage[]) {
    latestMaterialSummaryRef.current = null
    latestSkillSummaryRef.current = null
    latestExternalSummaryRef.current = null
    materialSummarySignatureRef.current = ''
    skillSummarySignatureRef.current = ''
    externalSummarySignatureRef.current = ''
    ontologyExtractionDoneSignatureRef.current = ''

    for (const message of messagesToSync) {
      const artifact = message.artifact
      if (!artifact || artifact.kind !== 'data') continue

      const signature = JSON.stringify(artifact.data ?? {})
      if (artifact.artifactType === 'material_handoff_summary' && artifact.isTerminal) {
        latestMaterialSummaryRef.current = artifact.data ?? null
        materialSummarySignatureRef.current = signature
      }
      if (artifact.artifactType === 'skill_workorder_summary' && artifact.isTerminal) {
        latestSkillSummaryRef.current = artifact.data ?? null
        skillSummarySignatureRef.current = signature
      }
      if (artifact.artifactType === 'external_workorder_summary' && artifact.isTerminal) {
        latestExternalSummaryRef.current = artifact.data ?? null
        externalSummarySignatureRef.current = signature
      }
      if (artifact.artifactType === 'ontology_extraction_done' && artifact.isTerminal) {
        ontologyExtractionDoneSignatureRef.current = signature
      }
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

    setMessages(restored.messages)
    setMaterialRequestedCategories(restored.materialRequestedCategories)
    setWsStageOverrides(restored.wsStageOverrides)
    downstreamRunsRef.current = restored.downstreamRuns
    setDownstreamRuns(restored.downstreamRuns)
    latestMaterialSummaryRef.current = restored.latestMaterialSummary
    latestSkillSummaryRef.current = restored.latestSkillSummary
    latestExternalSummaryRef.current = restored.latestExternalSummary
    materialSummarySignatureRef.current = restored.latestMaterialSummary ? JSON.stringify(restored.latestMaterialSummary) : ''
    skillSummarySignatureRef.current = restored.latestSkillSummary ? JSON.stringify(restored.latestSkillSummary) : ''
    externalSummarySignatureRef.current = restored.latestExternalSummary ? JSON.stringify(restored.latestExternalSummary) : ''
    ontologyExtractionDoneSignatureRef.current = restored.messages
      .filter(message => message.artifact?.artifactType === 'ontology_extraction_done' && message.artifact.isTerminal)
      .map(message => JSON.stringify(message.artifact?.data ?? {}))
      .at(-1) ?? ''
    return true
  }
  const skillGenerationState = downstreamRuns['skill-generation'] ?? null
  const externalConfigState = downstreamRuns['external-config'] ?? null
  const holdExternalStage = shouldHoldExternalStageUntilSkillImplementation(
    latestSkillSummaryRef.current,
    skillGenerationState,
  )
  const uiStageOverrides = useMemo(
    () => buildUiStageOverrides(wsStageOverrides, skillGenerationState, externalConfigState, holdExternalStage),
    [wsStageOverrides, skillGenerationState, externalConfigState, holdExternalStage],
  )

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
  const definedSkills = useMemo(
    () => extractLatestDefinedSkills(messages),
    [messages],
  )
  const viewModel = buildHiringWorkflowViewModel(null, focusedStage, t)
  // 将 WS 实时推送的阶段状态合并到阶段胶囊
  const mergedStepPills = viewModel.stepPills.map(pill => {
    const wsStatus = uiStageOverrides.get(pill.stage)
    if (!wsStatus) return pill
    return { ...pill, dispatchStatus: wsStatus }
  })
  // 三个收集阶段全部通过 WS 标记为 completed 时，允许触发打包（不依赖后端 workflowState 轮询）
  // 仅当沙箱已推送 template_package artifact（pendingPackageArtifact 不为 null）才能点击生成实例，
  // 否则后端无可导入的产物包。
  const wsStagesAllCompleted = (
    uiStageOverrides.get(HiringCollectionStage.Material) === 'completed' &&
    uiStageOverrides.get(HiringCollectionStage.Skill) === 'completed' &&
    uiStageOverrides.get(HiringCollectionStage.External) === 'completed'
  )
  const wsCanFinalize = wsStagesAllCompleted
  const mergedActionState = wsCanFinalize
    ? { ...viewModel.actionState, canFinalize: true }
    : viewModel.actionState
  const canCreate = Boolean(workflowHireId) && !instanceCreated
  const isInteractionLocked = typing || workflowBooting || submittingMessage || resetting
  const uploadedConversationFiles = useMemo(
    () => extractConversationMaterialFiles(messages),
    [messages],
  )
  const uploadedFileCount = Math.max(allFiles.length, uploadedConversationFiles.length)

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, typing])

  useEffect(() => {
    document.body.classList.add('hb-body-hiring-prototype')
    return () => {
      document.body.classList.remove('hb-body-hiring-prototype')
      // 离开页面时断开沙箱 WebSocket
      wsRef.current?.disconnect()
      wsRef.current = null
    }
  }, [])

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
  useEffect(() => {
    if (!pendingPackageArtifact || !workflowHireId || instanceCreated) return
    if (hasPendingDownstreamRuns(downstreamRunsRef.current)) return
    void triggerCreate(pendingPackageArtifact)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingPackageArtifact, workflowHireId, instanceCreated])

  // 对话状态变化时防抖保存到后端（messages 或 wsStageOverrides 变化时触发）
  useEffect(() => {
    messagesRef.current = messages
  }, [messages])

  useEffect(() => {
    if (!workflowHireId) return
    const timer = setTimeout(() => {
      const cache = {
        stageOverrides: Array.from(wsStageOverrides.entries()),
        downstreamRuns,
      }
      api.hiringWorkflow.saveConversationCache(workflowHireId, cache).catch(() => {})
    }, 2000)
    return () => clearTimeout(timer)
  }, [wsStageOverrides, downstreamRuns, workflowHireId])

  useEffect(() => {
    if (journeyGuideVisible && !focusedStage) {
      setFocusedStage(workflowCurrentStage)
    }
  }, [focusedStage, journeyGuideVisible, workflowCurrentStage])

  useEffect(() => {
    if (!templateId) {
      setTemplate(null)
      setTemplateLoading(false)
      setTemplateError(t('hiring.error.templateParamMissing'))
      return
    }

    let mounted = true
    setTemplateLoading(true)
    setTemplateError('')
    api.employeeTemplate.getDetail(templateId)
      .then((detail) => {
        if (mounted) {
          setTemplate(detail)
        }
      })
      .catch((error: unknown) => {
        if (mounted) {
          setTemplate(null)
          setTemplateError(normalizeErrorMessage(error))
        }
      })
      .finally(() => {
        if (mounted) {
          setTemplateLoading(false)
        }
      })

    return () => {
      mounted = false
    }
  }, [templateId])

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
    if (existingMessages.length > 0) {
      // 1. 先从沙箱会话历史恢复消息，同时得到从 artifact tool call 派生的基础阶段状态
      await restoreConversationFromSandboxHistory(endpoint, sessionId, 'always')

      // 2. 再从后端缓存加载阶段状态，覆盖历史派生值
      //    原因：wsStageOverrides 中 WS stage_update 事件不在沙箱消息历史里，无法从历史重建；
      //          downstreamRuns 缓存保存了最新完整的 status/data，优先级高于历史派生值。
      const hireIdForCache = currentHireId || workflowHireId
      if (hireIdForCache) {
        try {
          const cached = await api.hiringWorkflow.getConversationCache(hireIdForCache) as {
            stageOverrides?: [string, string][]
            downstreamRuns?: DownstreamRunsSnapshot
          } | null
          if (cached?.stageOverrides && cached.stageOverrides.length > 0) {
            setWsStageOverrides(new Map(cached.stageOverrides as [HiringUiStage, 'running' | 'completed' | 'failed'][]))
          }
          if (cached?.downstreamRuns && Object.keys(cached.downstreamRuns).length > 0) {
            // 合并：缓存中的 run 优先，保留历史中有而缓存中没有的 run
            const merged: DownstreamRunsSnapshot = { ...downstreamRunsRef.current, ...cached.downstreamRuns }
            downstreamRunsRef.current = merged
            setDownstreamRuns(merged)
          }
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
      // 调试：打印每条 WS 事件（text 截断为 120 字符避免刷屏）
      if (import.meta.env.DEV) {
        const preview = String((msg as unknown as Record<string, unknown>).text ?? '').slice(0, 120)
        console.log('[WS onMessage] type=%s text=%s', type, preview)
      }
      if (type === 'typing_start') {
        // AI 开始思考，切换到流式展示
        rawStreamingContentRef.current = ''
        setStreamingContent('')
        setTyping(true)
        // 重置本轮工具步骤累积
        pendingToolStepsRef.current = []
        setStreamingToolSteps([])
      } else if (type === 'text_delta' || type === 'assistant_chunk') {
        // 逐字追加流式内容
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        setStreamingContent(prev => {
          const next = prev === null ? chunk : prev + chunk
          rawStreamingContentRef.current = next
          return next
        })
      } else if (type === 'typing_stop' || type === 'assistant_done') {
        // AI 回复完毕，保存原始内容（供同步端点使用），然后将清理后的内容提交为正式气泡
        const rawReply = rawStreamingContentRef.current
        const userMessage = lastWsUserMessageRef.current
        const materials = lastWsMaterialsRef.current
        rawStreamingContentRef.current = ''

        // 直接从 ref 取流式内容提交为正式消息（不放在 setStreamingContent 回调里，
        // 避免 React StrictMode 双重调用导致同一条 bot 消息被 add 两遍）
        if (rawReply && rawReply.trim().length > 0) {
          const cleaned = normalizeAssistantReply(rawReply)
          if (cleaned.length > 0) {
            // 将本轮累积的工具调甈步骤附到 bot 消息，与 Markdown 正文合并呈现
            const steps = pendingToolStepsRef.current.length > 0 ? [...pendingToolStepsRef.current] : undefined
            setMessages(msgs => [...msgs, { id: mkId(), role: 'bot', content: cleaned, toolSteps: steps }])
          }
        }
        // 无论是否产生 bot 消息，本轮状态都需重置
        pendingToolStepsRef.current = []
        setStreamingToolSteps([])
        setStreamingContent(null)
        setTyping(false)

        // 将对话轮次同步到后端，使工作流引擎处理 AI 结构化标签、推进阶段等
        const hireId = workflowHireIdRef.current
        if (hireId && rawReply) {
          api.hiringWorkflow.syncConversationTurn(hireId, {
            userMessage: userMessage || '',
            assistantReply: rawReply,
            materials: materials ?? undefined,
          }).catch(() => { /* 忽略 */ })
        }
        void flushQueuedInternalPrompt()
      } else if (type === 'tool_start') {
        // MCP 工具开始调用：仅用于记录流式气泡上方的进度面板
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.text ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        console.log('[WS tool_start] rawName=%s toolName=%s', rawName, toolName)
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
        console.log('[WS tool_result] rawName=%s toolName=%s textPreview=%s', rawName, toolName, textStr.slice(0, 120))
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
          if (artifactType === 'skill_workorder_summary' && kind === 'data' && isTerminal) {
            latestSkillSummaryRef.current = artifactData.data ?? null
            skillSummarySignatureRef.current = JSON.stringify(artifactData.data ?? {})
            skillGenerationLaunchSignatureRef.current = ''
          }
          if (artifactType === 'external_workorder_summary' && kind === 'data' && isTerminal) {
            latestExternalSummaryRef.current = artifactData.data ?? null
            const signature = JSON.stringify(artifactData.data ?? {})
            if (externalSummarySignatureRef.current !== signature) {
              externalSummarySignatureRef.current = signature
              pendingInternalPromptsRef.current.push(
                buildDownstreamPrompt('external-config', artifactData.data ?? {}),
              )
            }
          }
          const downstreamRun = resolveDownstreamRunFromArtifact(artifactType)
          setMessages(msgs => [...msgs, {
            id: mkId(),
            role: 'artifact',
            content: label ?? artifactType,
            artifact: artifactData,
          }])
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
              // terminal artifact 标记阶段完成；否则只在尚未完成时标记运行中
              if (isTerminal) {
                next.set(hiringStage, 'completed')
              } else if (next.get(hiringStage) !== 'completed') {
                next.set(hiringStage, 'running')
              }
              return next
            })
          }
          // template_package artifact 表示沙箱已完成打包，暂存 fileUrl 后自动触发 import-package
          if (artifactType === 'template_package' && kind === 'file' && artifactData.fileUrl) {
            if (hasPendingDownstreamRuns(downstreamRunsRef.current)) {
              setWorkflowNotice('已收到产物包，但下游生成尚未完成，当前不会自动导入。请在下游完成后重新触发打包。')
            } else {
              setPendingPackageArtifact({ fileUrl: artifactData.fileUrl, fileName: artifactData.fileName ?? 'artifacts.zip' })
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
    if (typing || messageSubmitRef.current || pendingInternalPromptsRef.current.length === 0) {
      return
    }

    const prompt = pendingInternalPromptsRef.current.shift()
    if (!prompt) return

    await submitWorkflowMessage(prompt, undefined, true, false)
  }

  function setOptimisticSkillGenerationRun(status: DownstreamRunStatus, label: string, artifactType: string) {
    setDownstreamRuns(prev => {
      const next = {
        ...prev,
        ['skill-generation']: {
          key: 'skill-generation',
          status,
          artifactType,
          label,
          displayHint: 'progress',
          updatedAt: new Date().toISOString(),
          data: prev['skill-generation']?.data,
        } satisfies DownstreamRunState,
      }
      downstreamRunsRef.current = next
      return next
    })
  }

  async function launchSkillGenerationFromApproval(): Promise<boolean> {
    const summary = latestSkillSummaryRef.current
    if (!summary) return false

    const signature = skillSummarySignatureRef.current || JSON.stringify(summary)
    if (signature && skillGenerationLaunchSignatureRef.current === signature) {
      setWorkflowNotice('技能实现生成已启动，请等待进度更新。')
      return true
    }

    if (signature) {
      skillGenerationLaunchSignatureRef.current = signature
    }

    setOptimisticSkillGenerationRun(
      'running',
      '技能实现已启动，正在等待下游进度。',
      'skill_generation_progress',
    )

    const submitted = await submitWorkflowMessage(
      buildDownstreamPrompt('skill-generation', summary),
      undefined,
      true,
      false,
    )

    if (submitted) {
      setWorkflowNotice('已开始生成技能实现，等待进度更新。')
      return true
    }

    skillGenerationLaunchSignatureRef.current = ''
    setOptimisticSkillGenerationRun(
      'waiting_confirm',
      '技能实现尚未启动，请重新确认是否开始生成。',
      'skill_generation_ready',
    )
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

        // 记录本次发送的用户消息和材料，供 WS typing_stop 事件中调用同步端点使用
        lastWsUserMessageRef.current = messageText
        lastWsMaterialsRef.current = toConversationMaterials(incoming)
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
        setStreamingContent(null)
        rawStreamingContentRef.current = ''
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
      if (assistantContent) {
        setMessages(prev => [...prev, {
          id: response.assistantMessage.messageId || mkId(),
          role: 'bot',
          content: assistantContent,
        }])
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
      setStreamingContent(null)
      messageSubmitRef.current = false
      setSubmittingMessage(false)
    }
  }

  async function handleSend() {
    if (isInteractionLocked || handleSendRef.current) return
    const text = input.trim()
    if (!text && pendingFiles.length === 0) return

    handleSendRef.current = true
    try {
      const incoming = pendingFiles.length ? [...pendingFiles] : []
      const shouldLaunchSkillGeneration =
        incoming.length === 0 &&
        skillGenerationState?.status === 'waiting_confirm' &&
        isSkillGenerationApprovalMessage(text) &&
        latestSkillSummaryRef.current !== null

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

      const fallbackText = text || `上传文件：${incoming.map(file => file.name).join('、')}`
      const submitted = shouldLaunchSkillGeneration
        ? await launchSkillGenerationFromApproval()
        : await submitWorkflowMessage(
            fallbackText,
            incoming.length > 0 ? incoming : undefined,
            true,
            false, // handleSend 已经主动 setMessages，避免重复推用户气泡
          )

      if (!submitted && incoming.length > 0) {
        setPendingFiles(prev => [...incoming, ...prev])
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
        status: i18n.t('hiring.file.parsed'),
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

  async function triggerCreate(packageArtifact?: { fileUrl: string; fileName: string }) {
    if (!canCreate || instanceCreated) return
    const hireId = await ensureWorkflowReady()
    if (!hireId) return

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
      // 前端从沙箱网关下载产物包后上传给后端 import-package，后端不再依赖 KingCrab finalize。
      // fileUrl 可能是绝对 URL（如 http://opensandbox-gateway.../media/xxx）或相对路径；
      // 绝对 URL 直接使用，相对路径则需拼接 gateway base。
      let fullUrl: string
      if (/^https?:\/\//i.test(artifact.fileUrl)) {
        fullUrl = artifact.fileUrl
      } else {
        // gatewayEndpointRef 可能只是 "host:port" 格式（无协议），需补全为合法绝对 URL，
        // 否则 fetch 会将其视为相对路径，导致请求打到 Vite 开发服务器而非沙箱网关。
        const rawGateway = gatewayEndpointRef.current.trim()
        const normalizedBase = /^https?:\/\//i.test(rawGateway)
          ? rawGateway.replace(/\/$/, '')
          : `http://${rawGateway.replace(/^\/+/, '').replace(/\/$/, '')}`
        const fileUrlPath = artifact.fileUrl.startsWith('/')
          ? artifact.fileUrl
          : `/${artifact.fileUrl}`
        fullUrl = `${normalizedBase}${fileUrlPath}`
      }
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
      setArtifactArchive(await api.hiringWorkflow.downloadArtifacts(hireId))
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

  /**
   * 从沙箱 Gateway 下载文件（需要附带 Bearer token）。
   * fileUrl 可以是绝对 URL 或相对于 gateway endpoint 的路径。
   */
  async function downloadGatewayFile(fileUrl: string, fileName: string) {
    try {
      let fullUrl: string
      if (/^https?:\/\//i.test(fileUrl)) {
        fullUrl = fileUrl
      } else {
        const rawGateway = (gatewayEndpointRef.current ?? '').trim()
        const normalizedBase = /^https?:\/\//i.test(rawGateway)
          ? rawGateway.replace(/\/$/, '')
          : `http://${rawGateway.replace(/^\/+/, '').replace(/\/$/, '')}`
        const urlPath = fileUrl.startsWith('/') ? fileUrl : `/${fileUrl}`
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
        setStreamingContent(null)
        setTyping(false)
        setJourneyGuideVisible(false)
        setFocusedStage(null)
        setWorkflowError('')
        setWorkflowNotice('')
        setWsStageOverrides(new Map())
        setDownstreamRuns({})
        setMaterialRequestedCategories([])
        setPendingPackageArtifact(null)
        setLinkedStoreSkillIds([])
        downstreamRunsRef.current = {}
        latestMaterialSummaryRef.current = null
        latestSkillSummaryRef.current = null
        latestExternalSummaryRef.current = null
        materialSummarySignatureRef.current = ''
        skillSummarySignatureRef.current = ''
        externalSummarySignatureRef.current = ''
        skillGenerationLaunchSignatureRef.current = ''
        ontologyExtractionDoneSignatureRef.current = ''
        pendingInternalPromptsRef.current = []

        // 清除后端对话缓存，确保重置后刷新页面不会恢复旧记录
        api.hiringWorkflow.saveConversationCache(hireId, {}).catch(() => {})

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

  // @ts-ignore
  return (
    <div className="hb-hiring-page">
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
          typing={typing}
          streamingContent={streamingContent}
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
          onArtifactFileDownload={(url, fileName) => { void downloadGatewayFile(url, fileName) }}
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
            hasArtifactArchive={Boolean(artifactArchive)}
            onContinue={handlePrototypeContinue}
            onFinalize={() => { void handleRequestPackaging() }}
            onEnterTraining={(employeeId) => navigate(`/department-employees/instances/${employeeId}/training`)}
            onDownloadArtifact={(artifactName) => { void downloadBackendArtifact(artifactName) }}
            onDownloadArchive={() => {
              if (artifactArchive) {
                downloadBlob(artifactArchive.blob, artifactArchive.fileName)
              }
            }}
          />

          {/* MCP TODO 交互面板：完全由 WS artifact 事件驱动阶段亮灯 */}
          <HiringTodoPanel
            hireId={workflowHireId}
            sessionId={sessionIdRef.current ?? ''}
            wsStageOverrides={uiStageOverrides}
            templatePackageSkills={template?.packageSkills ?? []}
            requestedMaterialCategories={materialRequestedCategories}
            uploadedConversationFiles={uploadedConversationFiles}
            skillDefinitionStageStatus={wsStageOverrides.get(HiringCollectionStage.Skill) ?? null}
            skillGenerationState={skillGenerationState}
            definedSkills={definedSkills}
            onAfterStageMessage={(_stage, summary) => { void submitWorkflowMessage(summary) }}
            onGenerate={() => { void handleRequestPackaging() }}
            generated={instanceCreated}
            onLinkedSkillIdsChange={setLinkedStoreSkillIds}
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
    <div className="hb-page">
      <div className="hb-card flex min-h-52 items-center justify-center p-8 text-sm text-[#737373]">
        {message}
      </div>
    </div>
  )
}

function SkillUploadModal({
  open,
  disabled,
  onClose,
  onSubmit,
}: {
  open: boolean
  disabled: boolean
  onClose: () => void
  onSubmit: (payload: SkillUploadPayload) => void
}) {
  const { t } = useTranslation()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [dragOver, setDragOver] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [form, setForm] = useState({ name: '', releaseNote: '', description: '' })

  if (!open) return null

  function handleFile(nextFile: File) {
    const lowerName = nextFile.name.toLowerCase()
    if (lowerName.endsWith('.zip') || lowerName.endsWith('.tar.gz') || lowerName.endsWith('.gz')) {
      setFile(nextFile)
    }
  }

  function handleDrop(event: React.DragEvent<HTMLDivElement>) {
    event.preventDefault()
    setDragOver(false)
    if (disabled) return
    const dropped = event.dataTransfer.files[0]
    if (dropped) handleFile(dropped)
  }

  const canSubmit = Boolean(file && form.name.trim() && form.description.trim() && !disabled)

  return (
    <div className="hb-modal-mask">
      <div className="hb-modal hb-hiring-modal">
        <div className="hb-modal-head flex items-center justify-between gap-4 border-b border-[#f5f5f5] pb-4">
          <div>
            <h2 className="hb-modal-title">{t('hiring.skillUpload.title')}</h2>
            <p className="hb-modal-sub">{t('hiring.skillUpload.desc')}</p>
          </div>
          <button onClick={onClose} disabled={disabled} className="hb-modal-close" aria-label={t('hiring.skillUpload.title')}>
            <X size={16} />
          </button>
        </div>

        <div className="hb-modal-body space-y-5">
          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.package')} <span className="text-red-500">*</span></label>
            <div
              className={`hb-hiring-dropzone ${dragOver ? 'is-active' : file ? 'is-filled' : ''}`}
              onClick={() => { if (!disabled) fileInputRef.current?.click() }}
              onDragOver={(event) => { event.preventDefault(); if (!disabled) setDragOver(true) }}
              onDragLeave={() => setDragOver(false)}
              onDrop={handleDrop}
            >
              <Upload size={22} className={`mx-auto mb-2 ${file ? 'text-violet-500' : 'text-slate-400'}`} />
              {file ? (
                <>
                  <p className="text-sm font-medium text-[#4a6cf7]">{file.name}</p>
                  <p className="mt-1 text-xs text-[#737373]">{t('hiring.skillUpload.selectAgain')}</p>
                </>
              ) : (
                <>
                  <p className="text-sm text-[#404040]">{t('hiring.skillUpload.dragHint')}</p>
                  <p className="mt-1 text-xs text-[#737373]">{t('hiring.skillUpload.supportedFormats')}</p>
                </>
              )}
              <input
                ref={fileInputRef}
                type="file"
                accept=".zip,.tar.gz,.gz"
                className="hidden"
                disabled={disabled}
                onChange={(event) => {
                  const selected = event.target.files?.[0]
                  if (selected) handleFile(selected)
                }}
              />
            </div>
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.name')} <span className="text-red-500">*</span></label>
            <input
              type="text"
              disabled={disabled}
              value={form.name}
              onChange={(event) => setForm(prev => ({ ...prev, name: event.target.value }))}
              placeholder={t('hiring.skillUpload.namePlaceholder')}
              className="hb-hiring-form-input"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">
              {t('hiring.skillUpload.releaseNote')}
              <span className="ml-1 font-normal text-[#9ca3af]">{t('hiring.skillUpload.releaseNoteOptional')}</span>
            </label>
            <textarea
              disabled={disabled}
              value={form.releaseNote}
              onChange={(event) => setForm(prev => ({ ...prev, releaseNote: event.target.value.slice(0, 500) }))}
              placeholder={t('hiring.skillUpload.releaseNotePlaceholder')}
              rows={3}
              className="hb-hiring-form-textarea"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.description')} <span className="text-red-500">*</span></label>
            <textarea
              disabled={disabled}
              value={form.description}
              onChange={(event) => setForm(prev => ({ ...prev, description: event.target.value.slice(0, 1000) }))}
              placeholder={t('hiring.skillUpload.descriptionPlaceholder')}
              rows={4}
              className="hb-hiring-form-textarea"
            />
          </div>
        </div>

        <div className="hb-modal-foot">
          <button onClick={onClose} disabled={disabled} className="hb-btn-ghost">{t('hiring.button.cancel')}</button>
          <button
            disabled={!canSubmit}
            onClick={() => {
              if (!file) return
              onSubmit({
                file,
                name: form.name.trim(),
                releaseNote: form.releaseNote.trim(),
                description: form.description.trim(),
              })
            }}
            className="hb-btn-primary"
          >
            {t('hiring.button.submit')}
          </button>
        </div>
      </div>
    </div>
  )
}
