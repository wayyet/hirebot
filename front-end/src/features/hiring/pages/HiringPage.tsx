import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Upload, X } from 'lucide-react'

import { api, HiringAuditDecision, HiringCollectionStage } from '@/infra/api'
import type {
  EmployeeTemplateDetail,
  HandoffItem,
  HiringCollectionStageType,
  HiringConversationMaterial,
} from '@/infra/api'
import { GatewayWs, type GatewayMessage } from '@/infra/sandbox/gateway-ws'
import { resolveGatewayEndpoint } from '@/infra/sandbox/sandbox-config'
import { fetchLatestGatewaySession, fetchSandboxSessionMessages, uploadMediaToGateway } from '@/infra/sandbox/sandbox-api'
import { tokenService } from '@/infra/auth/token-service'

import { HiringConversationPanel } from './components/HiringConversationPanel'
import { HiringJourneyHeader } from './components/HiringJourneyHeader'
import { HiringProgressLedger } from './components/HiringProgressLedger'
import { HiringTodoPanel } from './components/HiringTodoPanel'
import { HiringStagePills } from './components/HiringStagePills'
import type { ArtifactDisplayData, ChatFile, ChatMessage, SkillUploadPayload, StageGateData } from './hiringPageTypes'
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

const SKILL_TO_HIRING_STAGE: Record<string, HiringUiStage> = {
  'ontology-extraction': HiringCollectionStage.Skill,
  'skill-generation': HiringCollectionStage.Skill,
  'external-config': HiringCollectionStage.External,
}

/**
 * 从 WS artifact/stage_gate 消息里推导对应的雇佣阶段。
 * employment-coach-conversation 的阶段名自带语义（stage1_material / stage2_skill / stage3_external），
 * 其余技能按 SKILL_TO_HIRING_STAGE 映射。
 */
function resolveHiringStageFromWs(
  skillName: string | undefined,
  stageName: string | undefined,
): HiringUiStage | null {
  if (skillName === 'employment-coach-conversation' && stageName) {
    if (stageName.includes('material')) return HiringCollectionStage.Material
    if (stageName.includes('skill')) return HiringCollectionStage.Skill
    if (stageName.includes('external')) return HiringCollectionStage.External
  }
  return SKILL_TO_HIRING_STAGE[skillName ?? ''] ?? null
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
  const [workflowBooting, setWorkflowBooting] = useState(false)
  const [workflowError, setWorkflowError] = useState('')
  const [workflowNotice, setWorkflowNotice] = useState('')
  const [workflowInitAttempted, setWorkflowInitAttempted] = useState(false)
  const [artifactArchive, setArtifactArchive] = useState<{ fileName: string; blob: Blob } | null>(null)
  const [artifactFileNames, setArtifactFileNames] = useState<string[]>([])
  // template_package artifact 到达时暂存，触发 triggerCreate() 后消费
  const [pendingPackageArtifact, setPendingPackageArtifact] = useState<{ fileUrl: string; fileName: string } | null>(null)
  const [submittingMessage, setSubmittingMessage] = useState(false)
  // WS 流式内容：非 null 时表示 AI 正在逐字输出
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  const [resetting, setResetting] = useState(false)
  const resettingRef = useRef(false)
  /** WS 实时推送的阶段状态覆盖，优先级高于 REST 轮询的 dispatchStatus */
  const [wsStageOverrides, setWsStageOverrides] = useState<Map<HiringUiStage, 'running' | 'completed' | 'failed'>>(new Map())
  /** MCP todo 面板：AI 通过 MCP 工具创建的 handoff 待办事项 */
  const [handoffItems, setHandoffItems] = useState<HandoffItem[]>([])
  /** 新到达的 handoffId，用于 flash 入场动画，约 800ms 后清除 */
  const [newHandoffIds, setNewHandoffIds] = useState<Set<string>>(new Set())

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
  // 避免同一会话重复触发“自动上传模板并引导”
  const autoTemplateBootstrapSessionRef = useRef<string | null>(null)

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
        if (wsStageOverrides.get(stage) !== 'completed') return stage
      }
      return HiringCollectionStage.ReadyForPackaging
    })(),
  )
  const viewModel = buildHiringWorkflowViewModel(null, focusedStage)
  // 将 WS 实时推送的阶段状态合并到阶段胶囊
  const mergedStepPills = viewModel.stepPills.map(pill => {
    const wsStatus = wsStageOverrides.get(pill.stage)
    if (!wsStatus) return pill
    return { ...pill, dispatchStatus: wsStatus }
  })
  // 三个收集阶段全部通过 WS 标记为 completed 时，允许触发打包（不依赖后端 workflowState 轮询）
  // 仅当沙箱已推送 template_package artifact（pendingPackageArtifact 不为 null）才能点击生成实例，
  // 否则后端无可导入的产物包。
  const wsStagesAllCompleted = (
    wsStageOverrides.get(HiringCollectionStage.Material) === 'completed' &&
    wsStageOverrides.get(HiringCollectionStage.Skill) === 'completed' &&
    wsStageOverrides.get(HiringCollectionStage.External) === 'completed'
  )
  const wsCanFinalize = wsStagesAllCompleted && pendingPackageArtifact !== null
  const mergedActionState = wsCanFinalize
    ? { ...viewModel.actionState, canFinalize: true }
    : viewModel.actionState
  const canCreate = Boolean(workflowHireId) && !instanceCreated
  const isInteractionLocked = typing || workflowBooting || submittingMessage || resetting

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
    void triggerCreate(pendingPackageArtifact)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingPackageArtifact, workflowHireId, instanceCreated])

  // 对话状态变化时防抖保存到后端（messages 或 wsStageOverrides 变化时触发）
  useEffect(() => {
    if (!workflowHireId || messages.length === 0) return
    const timer = setTimeout(() => {
      const cache = {
        messages,
        stageOverrides: Array.from(wsStageOverrides.entries()),
      }
      api.hiringWorkflow.saveConversationCache(workflowHireId, cache).catch(() => {})
    }, 2000)
    return () => clearTimeout(timer)
  }, [messages, wsStageOverrides, workflowHireId])

  // hireId 就绪后拉取一次 todo 列表，后续由 WS tool_result / typing_stop 驱动增量刷新
  useEffect(() => {
    if (!workflowHireId) return
    void refreshTodos()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflowHireId])

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

  // ── MCP TODO 面板操作 ───────────────────────────────────────────────────────

  /** 拉取当前 hireId 对应的所有 handoff todo 并更新 state，检测新增 id 触发 flash */
  async function refreshTodos() {
    if (!workflowHireId) return
    try {
      const items = await api.hiringWorkflow.getTodos(workflowHireId)
      setHandoffItems(prev => {
        const prevIds = new Set(prev.map(i => i.handoff_id))
        const freshIds = items.filter(i => !prevIds.has(i.handoff_id)).map(i => i.handoff_id)
        if (freshIds.length > 0) {
          setNewHandoffIds(new Set(freshIds))
          setTimeout(() => setNewHandoffIds(new Set()), 800)
        }
        return items
      })
    } catch {
      // 静默忽略：todo 面板加载失败不影响主聊天流程
    }
  }

  async function handleConfirmTodo(handoffId: string) {
    if (!workflowHireId) return
    const item = handoffItems.find(i => i.handoff_id === handoffId)
    await api.hiringWorkflow.updateTodoStatus(workflowHireId, handoffId, 'confirmed')
    setHandoffItems(prev => prev.map(i => i.handoff_id === handoffId ? { ...i, status: 'confirmed' } : i))
    if (item) void submitWorkflowMessage(`已确认：${item.title}，请继续下一步`)
  }

  async function handleDismissTodo(handoffId: string) {
    if (!workflowHireId) return
    await api.hiringWorkflow.updateTodoStatus(workflowHireId, handoffId, 'dismissed')
    setHandoffItems(prev => prev.map(i => i.handoff_id === handoffId ? { ...i, status: 'dismissed' } : i))
  }

  async function handleUploadTodoFile(handoffId: string, file: File) {
    if (!workflowHireId) return
    const item = handoffItems.find(i => i.handoff_id === handoffId)
    await api.hiringWorkflow.uploadMaterialFile(workflowHireId, file, { handoffId })
    await api.hiringWorkflow.updateTodoStatus(workflowHireId, handoffId, 'confirmed')
    setHandoffItems(prev => prev.map(i => i.handoff_id === handoffId ? { ...i, status: 'confirmed' } : i))
    if (item) void submitWorkflowMessage(`已上传文件 ${file.name}（${item.title}），请继续`)
  }

  async function handleSaveExternalConfig(handoffId: string, _config: Record<string, string>) {
    if (!workflowHireId) return
    const item = handoffItems.find(i => i.handoff_id === handoffId)
    await api.hiringWorkflow.updateTodoStatus(workflowHireId, handoffId, 'confirmed')
    setHandoffItems(prev => prev.map(i => i.handoff_id === handoffId ? { ...i, status: 'confirmed' } : i))
    if (item) void submitWorkflowMessage(`外部系统 ${item.title} 配置已完成，请继续`)
  }

  async function handleUploadSkillTodo(
    handoffId: string,
    file: File,
    meta: { name: string; releaseNote: string; description: string },
  ) {
    if (!workflowHireId) return
    const item = handoffItems.find(i => i.handoff_id === handoffId)
    await api.hiringWorkflow.uploadMaterialFile(workflowHireId, file, {
      type: 'skill',
      skillName: meta.name,
      releaseNote: meta.releaseNote,
      description: meta.description,
      archiveFormat: 'zip',
      handoffId,
    })
    await api.hiringWorkflow.updateTodoStatus(workflowHireId, handoffId, 'confirmed')
    setHandoffItems(prev => prev.map(i => i.handoff_id === handoffId ? { ...i, status: 'confirmed' } : i))
    if (item) void submitWorkflowMessage(`技能包 ${meta.name} 已上传（${item.title}），请继续`)
  }

  // ─────────────────────────────────────────────────────────────────────────────

  async function ensureWorkflowReady(): Promise<string | null> {
    if (!templateId) {
      setWorkflowError('模板参数缺失，请从模板详情页重新进入')
      return null
    }
    if (workflowHireId) {
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
          setWorkflowNotice(`模板自动导入失败：${bootstrapError}，请手动上传模板包后继续。`)
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
      `Attached file: ${uploadedFileName}`,
      '',
      `请先解压并完整分析上面的模板包（模板名：${templateName}）。`,
      useCaseSection,
      '然后严格按以下顺序引导我完成雇佣配置：',
      '1. 先给出材料收集清单（缺什么、为什么、如何提供）。',
      '2. 再给出技能与知识结构抽取结果，并指出待确认项。',
      '3. 再给出外部系统对接与凭据绑定清单（不要让我在聊天里直接贴敏感密钥）。',
      '4. 每一步都输出可执行的下一步操作，不要一次性抛出过多任务。',
      '5. 如果你发现信息不足，请先提问，不要自行假设关键业务参数。',
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
      // 优先从后端缓存恢复完整对话历史（含 artifact / stage_gate 消息和阶段状态）
      const hireIdForCache = currentHireId || workflowHireId
      if (hireIdForCache) {
        try {
          const cached = await api.hiringWorkflow.getConversationCache(hireIdForCache) as {
            messages?: ChatMessage[]
            stageOverrides?: [string, string][]
          } | null
          if (cached?.messages && cached.messages.length > 0) {
            setMessages(cached.messages)
            if (cached.stageOverrides && cached.stageOverrides.length > 0) {
              setWsStageOverrides(new Map(cached.stageOverrides as [HiringUiStage, 'running' | 'completed' | 'failed'][]))
            }
            autoTemplateBootstrapSessionRef.current = sessionId
            return
          }
        } catch {
          // 缓存读取失败时静默回退到沙箱消息恢复
        }
      }

      // 后端无缓存时，回退到仅恢复文字消息（沙箱历史）
      const mapped = existingMessages
        .filter(m => m.type === 'user_message' || m.type === 'assistant_message')
        .map<ChatMessage>(m => ({
          id: mkId(),
          role: m.type === 'user_message' ? 'user' : 'bot',
          content: m.type === 'assistant_message'
            ? normalizeAssistantReply(String(m.content ?? ''))
            : String(m.text ?? ''),
        }))
        .filter(m => m.content.trim().length > 0)
      if (mapped.length > 0) {
        setMessages(mapped)
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

    const uploadResult = await uploadMediaToGateway(endpoint, token, packageFile)
    const prompt = buildTemplateBootstrapPrompt(
      storeDetail.name || template?.name || '数字员工模板',
      Array.isArray(storeDetail.useCases) ? storeDetail.useCases : [],
      uploadResult.marker,
      uploadResult.fileName,
    )

    lastWsUserMessageRef.current = prompt
    lastWsMaterialsRef.current = [
      {
        type: 'file',
        name: uploadResult.fileName,
        size: uploadResult.sizeBytes,
        mimeType: uploadResult.mimeType,
        metadata: {
          source: 'template_auto_bootstrap',
          templateId: currentTemplateId,
          templateVersionId: versionId,
          mediaId: uploadResult.mediaId,
        },
      },
    ]

    const sent = ws.send({ type: 'user_message', text: prompt, sessionId })
    if (!sent) {
      return
    }

    autoTemplateBootstrapSessionRef.current = sessionId
    setTyping(true)
    setWorkflowNotice('已自动导入模板包并发送分析指令，正在由沙箱助手解析并引导下一步。')
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
          }).catch(() => { /* 忽略 */ })
        }

        // AI 回复结束后保底刷新 todo 面板（MCP 工具可能在本轮中创建了新 todo）
        void refreshTodos()
      } else if (type === 'tool_start') {
        // MCP 工具开始调用：从 text 中提取工具名（去除 streaming. 前缀），若为 hiring. 工具立即乐观刷新
        const rawName = String((msg as unknown as Record<string, unknown>).text ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        console.log('[WS tool_start] rawName=%s toolName=%s isHiring=%s', rawName, toolName, toolName.startsWith('hiring.'))
        if (toolName.startsWith('hiring.')) {
          void refreshTodos()
        }
      } else if (type === 'tool_result') {
        // MCP 工具调用完成：优先从顶层字段取工具名（部分 Gateway 版本携带），
        // 取不到时尝试解析 text JSON——若结果中含 data.handoff_id 则判定为 hiring todo 结果
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.tool_name ?? rawMsg.name ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const textStr = String(rawMsg.text ?? '')
        console.log('[WS tool_result] rawName=%s toolName=%s textPreview=%s', rawName, toolName, textStr.slice(0, 120))
        if (toolName.startsWith('hiring.')) {
          void refreshTodos()
        } else {
          // 兜底：解析 text 字段，检测是否为 HandoffItem 结构
          try {
            const parsed = JSON.parse(textStr) as Record<string, unknown>
            const data = parsed?.data as Record<string, unknown> | null | undefined
            console.log('[WS tool_result fallback] data.handoff_id=%s', data?.handoff_id)
            if (typeof data?.handoff_id === 'string') {
              void refreshTodos()
            }
          } catch { /* text 不是 JSON，忽略 */ }
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
            artifactData.data = raw.data
          }
          setMessages(msgs => [...msgs, {
            id: mkId(),
            role: 'artifact',
            content: label ?? artifactType,
            artifact: artifactData,
          }])
          // 同步更新阶段胶囊状态（实时，不等 REST 轮询）
          const hiringStage = resolveHiringStageFromWs(skillName, stage)
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
            setPendingPackageArtifact({ fileUrl: artifactData.fileUrl, fileName: artifactData.fileName ?? 'artifacts.zip' })
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

    // 开发调试钩子：window.__injectWsMsg({ type, ... }) 可在浏览器 Console 模拟 WS 消息
    if (import.meta.env.DEV) {
      ;((window as unknown) as Record<string, unknown>).__injectWsMsg = (msg: GatewayMessage) => {
        ws.onMessage?.(msg)
      }
    }
  }

  function retryWorkflowInitialization() {
    setWorkflowError('')
    setWorkflowNotice('')
    setWorkflowInitAttempted(false)
    void ensureWorkflowReady()
  }

  async function submitWorkflowMessage(text: string, incoming?: ChatFile[], autoApprove = true): Promise<boolean> {
    if (messageSubmitRef.current) {
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

  async function triggerCreate(packageArtifact?: { fileUrl: string; fileName: string }) {
    if (!canCreate || instanceCreated) return
    const hireId = await ensureWorkflowReady()
    if (!hireId) return

    // 方案 A：只走 import-package，不再回退调 KingCrab finalize。
    // 不明确传入 packageArtifact 时，尝试从状态中拿上次推送的产物事件。
    const effectiveArtifact = packageArtifact ?? pendingPackageArtifact
    if (!effectiveArtifact) {
      setWorkflowError('请在聊天中告知助手“已完成全部确认，请生成产物包”，等沙箱推送打包完成后再点击生成实例。')
      return
    }
    if (!gatewayEndpointRef.current) {
      setWorkflowError('未获取到沙箱网关地址，无法下载产物包，请刷新页面重试。')
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
      const finalizeResult = await api.hiringWorkflow.importPackage(hireId, packageBlob, artifact.fileName)

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
    setWorkflowNotice('正在重置会话...')

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

        setWorkflowNotice('会话已重置，可以开始新的雇佣流程。')
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
    : workflowBooting || workflowNotice
      ? 'blue'
      : workflowReady
        ? 'green'
        : 'gray'
  const workflowStatusLabel = workflowError
    ? workflowError
    : workflowNotice
      ? workflowNotice
      : workflowBooting
        ? '正在初始化后端工作流，请稍候...'
        : workflowReady
          ? '已连接'
          : ''

  return (
    <div className="hb-hiring-page">
      <HiringJourneyHeader
        templateName={introName}
        templateId={template.templateId}
        onReset={handleResetConversation}
        onContinue={handlePrototypeContinue}
        resetting={resetting}
      />

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
          onArtifactFileDownload={(url, fileName) => { void downloadGatewayFile(url, fileName) }}
        />

        <div className="hb-hiring-right-col">
          {workflowStatusLabel ? (
            <div className={`hb-hiring-proto-note is-${workflowStatusTone}`}>
              <span>{workflowStatusLabel}</span>
              {workflowError ? (
                <button type="button" onClick={retryWorkflowInitialization} disabled={workflowBooting} className="hb-hiring-inline-btn">
                  重试初始化
                </button>
              ) : null}
            </div>
          ) : null}

          <div className="hb-hiring-steps-card">
            <HiringStagePills
              steps={mergedStepPills}
              onSelectStage={handleSelectStage}
            />
          </div>

          <HiringProgressLedger
            stageCards={viewModel.stageCards}
            overallProgress={viewModel.overallProgress}
            actionState={mergedActionState}
            instanceCreated={instanceCreated}
            createdId={createdId}
            summaryItems={[{ label: '已上传文件', value: String(allFiles.length) }]}
            artifactFileNames={artifactFileNames}
            hasArtifactArchive={Boolean(artifactArchive)}
            onContinue={handlePrototypeContinue}
            onFinalize={() => { void triggerCreate() }}
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
            sessionId={sessionIdRef.current}
            wsStageOverrides={wsStageOverrides}
            onAfterStageMessage={(_stage, summary) => { void submitWorkflowMessage(summary) }}
            onGenerate={() => { void triggerCreate() }}
            generated={instanceCreated}
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
