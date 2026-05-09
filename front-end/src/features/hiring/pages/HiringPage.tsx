import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Upload, X } from 'lucide-react'

import { api, HiringAuditDecision, HiringCollectionPhase, HiringCollectionStage, HiringTodoStatus } from '@/infra/api'
import type {
  CredentialSlot,
  EmployeeTemplateDetail,
  HiringCollectionPhaseType,
  HiringCollectionStageType,
  HiringConversationMaterial,
  HiringWorkflowState,
} from '@/infra/api'
import { GatewayWs } from '@/infra/sandbox/gateway-ws'
import { fetchSandboxSessionMessages, uploadMediaToGateway } from '@/infra/sandbox/sandbox-api'
import { tokenService } from '@/infra/auth/token-service'

import { HiringConversationPanel } from './components/HiringConversationPanel'
import { HiringJourneyHeader } from './components/HiringJourneyHeader'
import { HiringProgressLedger } from './components/HiringProgressLedger'
import { HiringStagePills } from './components/HiringStagePills'
import type { ChatFile, ChatMessage, CredentialDraft, SkillUploadPayload } from './hiringPageTypes'
import { type HiringUiStage, buildHiringWorkflowViewModel } from './hiringWorkflowViewModel'

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

  return '请求失败，请稍后重试'
}

function normalizeAssistantReply(content: string) {
  const cleaned = content.replace(/<think>[\s\S]*?<\/think>/gi, '').trim()
  return cleaned.length > 0 ? cleaned : content.trim()
}

function mapTimelineMessagesToChat(messages: { messageId: string; role: string; content: string; createdAt: string }[]): ChatMessage[] {
  return messages
    .filter(message => message.content?.trim().length > 0)
    .sort((a, b) => {
      const byTime = new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
      if (byTime !== 0) return byTime
      return a.role.toLowerCase() === 'user' ? -1 : 1
    })
    .map(message => ({
      id: message.messageId || mkId(),
      role: message.role.toLowerCase() === 'assistant' ? 'bot' : 'user',
      content: message.role.toLowerCase() === 'assistant'
        ? normalizeAssistantReply(message.content)
        : message.content,
    }))
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
    status: '已解析',
    type,
    mimeType: file.type || undefined,
    content,
    metadata,
    rawFile: file,
  }
}

function readFileText(file: File): Promise<string | undefined> {
  if (file.size > MAX_MATERIAL_CHARS * 4) {
    return Promise.resolve(`[文件过大，仅作为资料登记：${file.name}，${file.size} bytes]`)
  }

  return new Promise(resolve => {
    const reader = new FileReader()
    reader.onload = () => {
      const value = typeof reader.result === 'string' ? reader.result : undefined
      resolve(value && value.length > MAX_MATERIAL_CHARS ? `${value.slice(0, MAX_MATERIAL_CHARS)}\n...[truncated]` : value)
    }
    reader.onerror = () => resolve(`[文件内容读取失败，仅作为资料登记：${file.name}]`)
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

function normalizeCollectionPhase(value: string): HiringCollectionPhaseType {
  if (value === HiringCollectionPhase.NotStarted) return HiringCollectionPhase.NotStarted
  if (value === HiringCollectionPhase.InProgress) return HiringCollectionPhase.InProgress
  if (value === HiringCollectionPhase.ReadyForFinalize) return HiringCollectionPhase.ReadyForFinalize
  if (value === HiringCollectionPhase.Finalized) return HiringCollectionPhase.Finalized
  return HiringCollectionPhase.InProgress
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

function hasPendingDispatch(workflowState: HiringWorkflowState | null) {
  return workflowState?.latestDispatches?.some(dispatch => !dispatch.completedAtUtc) ?? false
}

function looksLikeSensitiveSecret(value: string) {
  const normalized = value.trim()
  if (normalized.length < 12) {
    return false
  }

  return /\b(token|password|secret|connection[_ -]?string|client[_ -]?secret|api[_ -]?key)\b/i.test(normalized)
    || /(=|:)\s*["']?[A-Za-z0-9_\-:/+=]{10,}/.test(normalized)
    || /\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/.test(normalized)
}

function buildSummaryItems(workflowState: HiringWorkflowState | null, uploadedFileCount: number) {
  if (!workflowState) {
    return []
  }

  const workflowTodos = workflowState.workflowTodos ?? []
  const handoffItems = workflowState.handoffItems ?? []
  const allTodos = [...workflowTodos, ...handoffItems.map(item => ({
    status: item.status === 'confirmed' ? HiringTodoStatus.Done
      : item.status === 'dismissed' ? HiringTodoStatus.Dismissed
      : item.status === 'needs_review' ? HiringTodoStatus.NeedsReview
      : item.status === 'dispatched' || item.status === 'dirty' ? HiringTodoStatus.InProgress
      : HiringTodoStatus.Open,
  }))]
  const completedTodos = allTodos.filter(todo => todo.status === HiringTodoStatus.Done || todo.status === HiringTodoStatus.Resolved)
  return [
    { label: '待办总数', value: String(allTodos.length) },
    { label: '已完成', value: String(completedTodos.length) },
    { label: '诊断项', value: String(workflowState.latestDiagnosticReport?.diagnosticTodos.length ?? 0) },
    { label: '待复核', value: String(workflowState.configGovernance?.pendingReviewTodoIds.length ?? 0) },
    { label: '已上传文件', value: String(uploadedFileCount) },
  ]
}

export default function HiringPage() {
  const { templateId } = useParams()
  const navigate = useNavigate()

  const [template, setTemplate] = useState<EmployeeTemplateDetail | null>(null)
  const [templateLoading, setTemplateLoading] = useState(true)
  const [templateError, setTemplateError] = useState('')
  const [messages, setMessages] = useState<ChatMessage[]>([])
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
  const [workflowState, setWorkflowState] = useState<HiringWorkflowState | null>(null)
  const [workflowBooting, setWorkflowBooting] = useState(false)
  const [workflowError, setWorkflowError] = useState('')
  const [workflowNotice, setWorkflowNotice] = useState('')
  const [workflowInitAttempted, setWorkflowInitAttempted] = useState(false)
  const [artifactArchive, setArtifactArchive] = useState<{ fileName: string; blob: Blob } | null>(null)
  const [artifactFileNames, setArtifactFileNames] = useState<string[]>([])
  const [submittingMessage, setSubmittingMessage] = useState(false)
  // WS 流式内容：非 null 时表示 AI 正在逐字输出
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  const [credentialDrafts, setCredentialDrafts] = useState<Record<string, CredentialDraft>>({})
  const [configDrafts, setConfigDrafts] = useState<Record<string, string>>({})
  const [credentialSubmittingSlot, setCredentialSubmittingSlot] = useState<string | null>(null)
  const [configSavingKey, setConfigSavingKey] = useState<string | null>(null)

  const fileRef = useRef<HTMLInputElement>(null)
  const composerRef = useRef<HTMLTextAreaElement>(null)
  const chatEndRef = useRef<HTMLDivElement>(null)
  const workflowInitRef = useRef<Promise<string | null> | null>(null)
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

  const workflowReady = Boolean(workflowHireId && workflowState)
  const workflowCollectionPhase = normalizeCollectionPhase(workflowState?.collectionPhase ?? HiringCollectionPhase.NotStarted)
  const workflowCurrentStage = normalizeCollectionStage(workflowState?.currentStage ?? HiringCollectionStage.Material)
  const workflowConversationPaused = Boolean(workflowState?.isConversationPaused)
  const workflowConversationResponding = Boolean(workflowState?.isConversationResponding)
  const viewModel = buildHiringWorkflowViewModel(workflowState, focusedStage)
  const summaryItems = buildSummaryItems(workflowState, allFiles.length)
  const canCreate = viewModel.actionState.canFinalize && workflowCollectionPhase !== HiringCollectionPhase.Finalized
  const isInteractionLocked = typing || workflowBooting || workflowConversationPaused || workflowConversationResponding || submittingMessage

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
    if (workflowState?.configGovernance?.files) {
      setConfigDrafts(prev => {
        const next = { ...prev }
        for (const file of workflowState.configGovernance?.files ?? []) {
          if (!(file.configKey in next)) {
            next[file.configKey] = file.content
          }
        }
        return next
      })
    }
  }, [workflowState?.configGovernance?.files])

  useEffect(() => {
    if (workflowState?.credentialSlots) {
      setCredentialDrafts(prev => {
        const next = { ...prev }
        for (const slot of workflowState.credentialSlots ?? []) {
          if (!(slot.credentialSlot in next)) {
            next[slot.credentialSlot] = { secretValue: '', secretRef: slot.secretRef ?? '' }
          }
        }
        return next
      })
    }
  }, [workflowState?.credentialSlots])

  useEffect(() => {
    if (!workflowReady || !workflowHireId) {
      return
    }

    if (!workflowConversationResponding && !hasPendingDispatch(workflowState)) {
      return
    }

    const timer = window.setInterval(() => {
      void syncWorkflowState(workflowHireId)
    }, 2000)

    return () => window.clearInterval(timer)
  }, [workflowConversationResponding, workflowHireId, workflowReady, workflowState])

  useEffect(() => {
    if (templateLoading || templateError || !templateId) {
      return
    }
    if (workflowReady || workflowBooting || workflowInitAttempted || messages.length > 0) {
      return
    }
    void ensureWorkflowReady()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateLoading, templateError, templateId, workflowReady, workflowBooting, workflowInitAttempted, messages.length])

  useEffect(() => {
    if (journeyGuideVisible && !focusedStage) {
      setFocusedStage(workflowCurrentStage)
    }
  }, [focusedStage, journeyGuideVisible, workflowCurrentStage])

  useEffect(() => {
    if (!templateId) {
      setTemplate(null)
      setTemplateLoading(false)
      setTemplateError('模板参数缺失')
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

  const introName = template?.name ?? '数字员工'
  const introAbilities = template?.coreAbilities.slice(0, 3).join('、') || '业务理解、技能配置、外部系统连接'
  const journeySummary = `当前模板为「${introName}」，完成资料、技能、系统与实例包四段闭环后即可进入后续培训与评估。`

  async function syncWorkflowState(hireId: string) {
    const nextWorkflowState = await api.hiringWorkflow.getWorkflowState(hireId)
    // 优先直连沙箱拉取历史消息，减少后端中转
    const endpoint = gatewayEndpointRef.current
    const sid = sessionIdRef.current
    if (endpoint && sid) {
      const sandboxMessages = await fetchSandboxSessionMessages(endpoint, sid)
      const mapped = sandboxMessages
        .filter(m => m.type === 'user_message' || m.type === 'assistant_message')
        .map<ChatMessage>(m => ({
          id: mkId(),
          role: m.type === 'user_message' ? 'user' : 'bot',
          content: m.type === 'assistant_message'
            ? normalizeAssistantReply(String(m.content ?? ''))
            : String(m.text ?? ''),
        }))
        .filter(m => m.content.trim().length > 0)
      setMessages(prev => mapped.length >= prev.length ? mapped : prev)
    } else {
      const timeline = await api.hiringWorkflow.getConversationTimeline(hireId)
      setMessages(prev => timeline.messages.length >= prev.length ? mapTimelineMessagesToChat(timeline.messages) : prev)
    }
    setWorkflowState(nextWorkflowState)
    if (nextWorkflowState.collectionPhase === HiringCollectionPhase.Finalized) {
      setInstanceCreated(true)
    }
    return nextWorkflowState
  }

  async function ensureWorkflowReady(): Promise<string | null> {
    if (!templateId) {
      setWorkflowError('模板参数缺失，请从模板详情页重新进入')
      return null
    }
    if (workflowReady && workflowHireId) {
      return workflowHireId
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

        let latestStatus = hired.status
        let latestGatewayEndpoint: string | null = null
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

        // 如果 hired.status 初始就是 READY（沙箱已在运行），循环体从未执行，
        // gatewayEndpoint 未被填充，需要补一次查询
        if (!latestGatewayEndpoint) {
          const statusResult = await api.employeeTemplate.getHiringStatus(hired.hireId)
          latestGatewayEndpoint = statusResult.gatewayEndpoint ?? null
        }

        // 保存网关端点，后续直连沙箱使用
        if (latestGatewayEndpoint) {
          gatewayEndpointRef.current = latestGatewayEndpoint
        }

        const conversation = await api.hiringWorkflow.startConversation(hired.hireId)
        // 保存会话 ID，用于直连沙箱拉取历史和建立 WebSocket
        sessionIdRef.current = conversation.sessionId

        // 建立到沙箱的 WebSocket 直连，用于流式展示 AI 回复
        if (latestGatewayEndpoint) {
          await connectSandboxWs(latestGatewayEndpoint)
        }

        await syncWorkflowState(hired.hireId)
        setWorkflowNotice('')
        return hired.hireId
      } catch (error: unknown) {
        setWorkflowState(null)
        setWorkflowError(normalizeErrorMessage(error))
        return null
      } finally {
        setWorkflowBooting(false)
        workflowInitRef.current = null
      }
    })()

    return workflowInitRef.current
  }

  /**
   * 建立到沙箱 Gateway 的 WebSocket 直连。
   * 消息发送直接经由 WebSocket，沙箱流式推送 AI 回复。
   */
  async function connectSandboxWs(endpoint: string) {
    wsRef.current?.disconnect()

    const token = await tokenService.ensureFresh()
    if (!token) return

    const ws = new GatewayWs(endpoint, token)

    ws.onMessage = (msg) => {
      const type = msg.type as string
      if (type === 'typing_start') {
        // AI 开始思考，切换到流式展示
        rawStreamingContentRef.current = ''
        setStreamingContent('')
        setTyping(true)
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
            setMessages(msgs => [...msgs, { id: mkId(), role: 'bot', content: cleaned }])
          }
        }
        setStreamingContent(null)
        setTyping(false)

        // 将对话轮次同步到后端，使工作流引擎处理 AI 结构化标签、推进阶段等
        const hireId = workflowHireId
        if (hireId && rawReply) {
          api.hiringWorkflow.syncConversationTurn(hireId, {
            userMessage: userMessage || '',
            assistantReply: rawReply,
            materials: materials ?? undefined,
          }).then(() => {
            syncWorkflowState(hireId).catch(() => { /* 忽略 */ })
          }).catch(() => {
            // 同步失败时仍然刷新工作流状态（后端可从沙箱拉取 handoff 元数据）
            syncWorkflowState(hireId).catch(() => { /* 忽略 */ })
          })
        }
      }
    }

    // 重连后拉取断线期间的会话历史
    ws.onReconnected = () => {
      const sid = sessionIdRef.current
      if (endpoint && sid) {
        fetchSandboxSessionMessages(endpoint, sid).then(sandboxMessages => {
          const mapped = sandboxMessages
            .filter(m => m.type === 'user_message' || m.type === 'assistant_message')
            .map<ChatMessage>(m => ({
              id: mkId(),
              role: m.type === 'user_message' ? 'user' : 'bot',
              content: m.type === 'assistant_message'
                ? normalizeAssistantReply(String(m.content ?? ''))
                : String(m.text ?? ''),
            }))
            .filter(m => m.content.trim().length > 0)
          setMessages(prev => mapped.length >= prev.length ? mapped : prev)
        }).catch(() => { /* 忽略拉取失败 */ })
      }
    }

    ws.connect()
    wsRef.current = ws
  }

  function retryWorkflowInitialization() {
    setWorkflowError('')
    setWorkflowNotice('')
    setWorkflowInitAttempted(false)
    void ensureWorkflowReady()
  }

  async function submitWorkflowMessage(text: string, incoming?: ChatFile[], autoApprove = true): Promise<boolean> {
    if (workflowConversationPaused) {
      setWorkflowError('对话已暂停，请先恢复后再继续发送消息')
      return false
    }
    if (messageSubmitRef.current || workflowConversationResponding) {
      setWorkflowError('上一轮回复仍在生成中，请稍候')
      return false
    }

    const hireId = await ensureWorkflowReady()
    if (!hireId) return false

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
        ws.send({ type: 'user_message', text: messageText, sessionId })
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

      if (response.currentStage) {
        setWorkflowState(prev => prev ? { ...prev, currentStage: response.currentStage } : prev)
      }

      await syncWorkflowState(hireId)
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

    if (workflowCurrentStage === HiringCollectionStage.External && text && looksLikeSensitiveSecret(text)) {
      setWorkflowError('检测到疑似敏感凭据，请改用右侧“凭据绑定”区填写，不要直接发送到聊天框。')
      setJourneyGuideVisible(true)
      setFocusedStage(HiringCollectionStage.External)
      return
    }

    handleSendRef.current = true
    try {
      const incoming = pendingFiles.length ? [...pendingFiles] : []
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

      const submitted = await submitWorkflowMessage(
        text || `上传文件：${incoming.map(file => file.name).join('、')}`,
        incoming.length > 0 ? incoming : undefined,
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
      setWorkflowError('当前对话处理中，请稍候后再上传 Skill')
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
        status: '已解析',
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

      if (await submitWorkflowMessage(`已上传 Skill 包并提交信息\n${details.join('\n')}`, [skillFile], false)) {
        setShowSkillUploadModal(false)
      }
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    }
  }

  async function triggerCreate() {
    if (!canCreate || instanceCreated) return
    const hireId = await ensureWorkflowReady()
    if (!hireId) return

    try {
      const finalizeResult = await api.hiringWorkflow.finalize(hireId)
      setArtifactFileNames(finalizeResult.generatedFiles)
      if (finalizeResult.employeeId) {
        setCreatedId(finalizeResult.employeeId)
      }
      setArtifactArchive(await api.hiringWorkflow.downloadArtifacts(hireId))
      setInstanceCreated(true)
      await syncWorkflowState(hireId)
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

  function handlePrototypeContinue() {
    setJourneyGuideVisible(true)
    setFocusedStage(workflowCurrentStage)
    setWorkflowNotice('')
    composerRef.current?.focus()
  }

  function handlePrototypeResetView() {
    setJourneyGuideVisible(false)
    setFocusedStage(null)
    setWorkflowNotice('')
    composerRef.current?.blur()
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

  async function handleCredentialSubmit(slot: CredentialSlot) {
    if (!workflowHireId) return
    const draft = credentialDrafts[slot.credentialSlot]
    if (!draft?.secretValue.trim()) {
      setWorkflowError('请先填写凭据值')
      return
    }

    setCredentialSubmittingSlot(slot.credentialSlot)
    try {
      await api.hiringWorkflow.upsertCredentialBinding(workflowHireId, {
        credentialSlot: slot.credentialSlot,
        secretValue: draft.secretValue.trim(),
        secretRef: draft.secretRef.trim() || undefined,
        authKind: slot.authKind ?? undefined,
        targetSystem: slot.targetSystem ?? undefined,
        todoId: slot.todoId ?? undefined,
      })
      await syncWorkflowState(workflowHireId)
      setCredentialDrafts(prev => ({ ...prev, [slot.credentialSlot]: { secretValue: '', secretRef: draft.secretRef } }))
      setWorkflowNotice(`已保存 ${slot.credentialSlot} 的凭据绑定。`)
      setWorkflowError('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    } finally {
      setCredentialSubmittingSlot(null)
    }
  }

  async function handleConfigSave(configKey: string) {
    if (!workflowHireId) return
    const content = configDrafts[configKey]
    if (typeof content !== 'string') return

    setConfigSavingKey(configKey)
    try {
      await api.hiringWorkflow.updateConfigFile(workflowHireId, configKey, {
        content,
        summary: '前端调整配置治理内容',
      })
      await syncWorkflowState(workflowHireId)
      setWorkflowNotice(`已保存 ${configKey} 配置，受影响工单会重新进入复核。`)
      setWorkflowError('')
    } catch (error: unknown) {
      setWorkflowError(normalizeErrorMessage(error))
    } finally {
      setConfigSavingKey(null)
    }
  }

  if (!templateId) {
    return <CenterState message="模板参数缺失" />
  }
  if (templateLoading) {
    return <CenterState message="模板加载中..." />
  }
  if (!template) {
    return <CenterState message={templateError || '模板不存在'} />
  }

  const workflowStatusTone = workflowError
    ? 'pink'
    : workflowBooting || workflowConversationResponding || workflowNotice
      ? 'blue'
      : workflowConversationPaused
        ? 'orange'
        : workflowReady
          ? 'green'
          : 'gray'
  const workflowStatusLabel = workflowError
    ? workflowError
    : workflowNotice
      ? workflowNotice
      : workflowBooting
        ? '正在初始化后端工作流，请稍候...'
        : viewModel.workflowMeta.join(' · ')

  return (
    <div className="hb-hiring-page">
      <HiringJourneyHeader
        templateName={template.name}
        onBack={() => navigate(`/templates/${template.templateId}`)}
        onReset={handlePrototypeResetView}
        onContinue={handlePrototypeContinue}
      />

      <div className="hb-hiring-shell">
        <HiringStagePills
          journeySummary={journeySummary}
          steps={viewModel.stepPills}
          onSelectStage={handleSelectStage}
        />

        <div className={`hb-hiring-proto-note is-${workflowStatusTone}`}>
          <span>{workflowStatusLabel}</span>
          {workflowError ? (
            <button type="button" onClick={retryWorkflowInitialization} disabled={workflowBooting} className="hb-hiring-inline-btn">
              重试初始化
            </button>
          ) : null}
        </div>

        <div className="hb-hiring-workspace">
          <HiringConversationPanel
            introName={introName}
            introAbilities={introAbilities}
            journeyGuideVisible={journeyGuideVisible}
            guideCard={viewModel.guideCard}
            messages={messages}
            typing={typing}
            streamingContent={streamingContent}
            pendingFiles={pendingFiles}
            input={input}
            promptPlaceholder={viewModel.promptPlaceholder}
            disabled={isInteractionLocked}
            fileInputRef={fileRef}
            composerRef={composerRef}
            chatEndRef={chatEndRef}
            onStartGuide={handlePrototypeContinue}
            onInputChange={setInput}
            onSend={() => { void handleSend() }}
            onFileChange={addPendingFiles}
            onOpenSkillUpload={() => setShowSkillUploadModal(true)}
            onRemovePendingFile={(fileId) => setPendingFiles(prev => prev.filter(file => file.id !== fileId))}
            formatFileSize={formatFileSize}
          />

          <HiringProgressLedger
            stageCards={viewModel.stageCards}
            overallProgress={viewModel.overallProgress}
            currentStage={workflowCurrentStage}
            collectionPhase={workflowCollectionPhase}
            actionState={viewModel.actionState}
            instanceCreated={instanceCreated}
            createdId={createdId}
            summaryItems={summaryItems}
            artifactFileNames={artifactFileNames}
            hasArtifactArchive={Boolean(artifactArchive)}
            credentialSlots={workflowState?.credentialSlots ?? []}
            credentialDrafts={credentialDrafts}
            credentialSubmittingSlot={credentialSubmittingSlot}
            configGovernance={workflowState?.configGovernance ?? null}
            configDrafts={configDrafts}
            configSavingKey={configSavingKey}
            onContinue={handlePrototypeContinue}
            onFinalize={() => { void triggerCreate() }}
            onEnterTraining={(employeeId) => navigate(`/instances/${employeeId}/training`)}
            onDownloadArtifact={(artifactName) => { void downloadBackendArtifact(artifactName) }}
            onDownloadArchive={() => {
              if (artifactArchive) {
                downloadBlob(artifactArchive.blob, artifactArchive.fileName)
              }
            }}
            onCredentialChange={(credentialSlot, field, value) => {
              setCredentialDrafts(prev => ({
                ...prev,
                [credentialSlot]: {
                  secretValue: prev[credentialSlot]?.secretValue ?? '',
                  secretRef: prev[credentialSlot]?.secretRef ?? '',
                  [field]: value,
                },
              }))
            }}
            onCredentialSubmit={(slot) => { void handleCredentialSubmit(slot) }}
            onConfigChange={(configKey, value) => setConfigDrafts(prev => ({ ...prev, [configKey]: value }))}
            onConfigSave={(configKey) => { void handleConfigSave(configKey) }}
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
            <h2 className="hb-modal-title">上传 Skill</h2>
            <p className="hb-modal-sub">上传技能包并补充技能元信息</p>
          </div>
          <button onClick={onClose} disabled={disabled} className="hb-modal-close" aria-label="关闭">
            <X size={16} />
          </button>
        </div>

        <div className="hb-modal-body space-y-5">
          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">技能包 <span className="text-red-500">*</span></label>
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
                  <p className="mt-1 text-xs text-[#737373]">点击重新选择文件</p>
                </>
              ) : (
                <>
                  <p className="text-sm text-[#404040]">拖拽技能包到此处，或点击上传</p>
                  <p className="mt-1 text-xs text-[#737373]">支持 .zip、.tar.gz、.gz</p>
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
            <label className="hb-hiring-form-label">Skill 名称 <span className="text-red-500">*</span></label>
            <input
              type="text"
              disabled={disabled}
              value={form.name}
              onChange={(event) => setForm(prev => ({ ...prev, name: event.target.value }))}
              placeholder="请输入 Skill 名称"
              className="hb-hiring-form-input"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">
              版本说明
              <span className="ml-1 font-normal text-[#9ca3af]">(可选，最多 500 字)</span>
            </label>
            <textarea
              disabled={disabled}
              value={form.releaseNote}
              onChange={(event) => setForm(prev => ({ ...prev, releaseNote: event.target.value.slice(0, 500) }))}
              placeholder="描述本次版本更新内容"
              rows={3}
              className="hb-hiring-form-textarea"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">技能描述 <span className="text-red-500">*</span></label>
            <textarea
              disabled={disabled}
              value={form.description}
              onChange={(event) => setForm(prev => ({ ...prev, description: event.target.value.slice(0, 1000) }))}
              placeholder="请输入技能适用场景、输入输出与注意事项"
              rows={4}
              className="hb-hiring-form-textarea"
            />
          </div>
        </div>

        <div className="hb-modal-foot">
          <button onClick={onClose} disabled={disabled} className="hb-btn-ghost">取消</button>
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
            提交
          </button>
        </div>
      </div>
    </div>
  )
}
