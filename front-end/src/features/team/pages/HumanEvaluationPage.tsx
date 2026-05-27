import { useEffect, useRef, useState } from 'react'
import {
  AlertCircle,
  BarChart2,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Loader2,
  MessageCircle,
  SendHorizontal,
  ShieldAlert,
  ShieldCheck,
  Zap,
} from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { tokenService } from '@/infra/auth/token-service'
import { GatewayWs } from '@/infra/sandbox/gateway-ws'
import {
  fetchAdminSessions,
  fetchSandboxSessionMessages,
  type SandboxMessage,
} from '@/infra/sandbox/sandbox-api'
import {
  api,
  type EmployeeDetail,
  type EvaluationState,
  type EvaluationWorkspaceStatus,
  type HiringConversationMessage,
} from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import { HiringToolStepsBlock } from '@/features/hiring/pages/components/HiringToolStepsBlock'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import { instanceBasePath } from '@/shared/utils/instancePath'

type EvalChatMessage = HiringConversationMessage & { toolSteps?: ToolStep[] }

function formatDateTime(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function verdictLabel(verdict?: string | null) {
  if (verdict === 'passed') return '通过'
  if (verdict === 'failed') return '未通过'
  if (verdict === 'warning') return '待优化'
  return '待判定'
}

function verdictPillClass(verdict?: string | null) {
  if (verdict === 'passed') return 'eval-tone-completed'
  if (verdict === 'failed') return 'eval-tone-failed'
  if (verdict === 'warning') return 'eval-tone-warning'
  return 'eval-tone-pending'
}

function mapSandboxMessages(messages: SandboxMessage[]): EvalChatMessage[] {
  return messages
    .filter((m) => m.type === 'user_message' || m.type === 'assistant_message')
    .map((m, index) => {
      const toolSteps: ToolStep[] | undefined = (m.toolCalls?.length ?? 0) > 0
        ? m.toolCalls!.map((tc, tcIdx) => ({
            id: `${index}-${tcIdx}-${tc.toolName}`,
            name: tc.toolName.startsWith('streaming.') ? tc.toolName.slice('streaming.'.length) : tc.toolName,
            args: tc.arguments,
            result: tc.result,
            status: 'done' as const,
          }))
        : undefined
      return {
        messageId: `${m.type}-${index}-${String(m.createdAt ?? Date.now())}`,
        role: m.type === 'user_message' ? 'user' : 'assistant',
        content: String(m.content ?? m.text ?? '').trim(),
        createdAt: String(m.createdAt ?? new Date().toISOString()),
        toolSteps,
      }
    })
    .filter((m) => m.content.length > 0 || (m.toolSteps?.length ?? 0) > 0)
}

export default function HumanEvaluationPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const [workspaceStatus, setWorkspaceStatus] = useState<EvaluationWorkspaceStatus | null>(null)

  const [chatMessages, setChatMessages] = useState<EvalChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatSending, setChatSending] = useState(false)
  const [chatError, setChatError] = useState('')
  const [sandboxConnected, setSandboxConnected] = useState(false)
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([])
  const [chatTyping, setChatTyping] = useState(false)
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null)
  const [rightCollapsed, setRightCollapsed] = useState(false)
  const [showForceConfirm, setShowForceConfirm] = useState(false)

  const wsRef = useRef<GatewayWs | null>(null)
  const targetEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const streamingContentRef = useRef('')
  const streamingToolStepsRef = useRef<ToolStep[]>([])
  const chatEndRef = useRef<HTMLDivElement | null>(null)
  const chatReadyRef = useRef<Promise<boolean> | null>(null)
  const connectionStateRef = useRef<{ endpoint: string | null; sessionId: string | null }>({
    endpoint: null,
    sessionId: null,
  })

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatMessages, streamingContent])

  async function loadData() {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const [employeeData, evaluationData, workspaceData] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getEvaluationState(id),
        api.employeeRuntime.getEvaluationWorkspaceStatus(id),
      ])
      setEmployee(employeeData)
      setEvaluation(evaluationData)
      setWorkspaceStatus(workspaceData)

      // 安全检查：如果状态仍是 interning_ai（未经 EvaluationPage 按钮触发转换），在此补做转换
      if (employeeData.status === 'interning_ai') {
        try {
          const updated = await api.employeeRuntime.updateLifecycle(id, {
            status: 'interning_human',
            stageSummary: '人工评估进行中',
            primarySignal: '等待人工验证',
            signalLevel: 'ok',
          })
          setEmployee(updated)
        } catch {
          // 转换失败不影响后续操作
        }
      }
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载人工评估数据失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [id])

  // 工作区就绪后自动连接目标沙箱最新会话
  useEffect(() => {
    const endpoint = workspaceStatus?.targetGatewayEndpoint
    if (!endpoint || workspaceStatus?.overallStatus !== 'ready') return

    void initTargetChat(endpoint)

    return () => {
      wsRef.current?.disconnect()
      wsRef.current = null
      targetEndpointRef.current = null
      sessionIdRef.current = null
      chatReadyRef.current = null
      connectionStateRef.current = { endpoint: null, sessionId: null }
      setSandboxConnected(false)
    }
  }, [workspaceStatus?.targetGatewayEndpoint, workspaceStatus?.overallStatus])

  async function connectTargetWs(endpoint: string): Promise<void> {
    if (
      wsRef.current?.isOpen() &&
      connectionStateRef.current.endpoint === endpoint &&
      connectionStateRef.current.sessionId === sessionIdRef.current
    ) {
      setSandboxConnected(true)
      return
    }

    wsRef.current?.disconnect()
    wsRef.current = null
    setSandboxConnected(false)

    const token = await tokenService.ensureFresh()
    if (!token) throw new Error('沙箱鉴权失败，无法建立 WebSocket 连接')

    const ws = new GatewayWs(endpoint, token)
    let settled = false
    let timeoutId: number | null = null

    const waitForOpen = new Promise<void>((resolve, reject) => {
      timeoutId = window.setTimeout(() => {
        if (settled) return
        settled = true
        reject(new Error('目标沙箱连接超时，请稍后重试'))
      }, 8000)

      ws.onStateChange = (state) => {
        setSandboxConnected(state === 'open')
        if (state === 'open' && !settled) {
          settled = true
          if (timeoutId !== null) window.clearTimeout(timeoutId)
          resolve()
        }
        if ((state === 'closed' || state === 'error') && !settled) {
          settled = true
          if (timeoutId !== null) window.clearTimeout(timeoutId)
          reject(new Error('目标沙箱连接断开，无法建立会话'))
        }
      }
    })

    ws.onMessage = (msg) => {
      const msgType = String(msg.type ?? '')

      if (msgType === 'typing_start') {
        streamingContentRef.current = ''
        streamingToolStepsRef.current = []
        setStreamingContent('')
        setStreamingToolSteps([])
        setChatTyping(true)
        return
      }

      if (msgType === 'text_delta' || msgType === 'assistant_chunk') {
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        streamingContentRef.current += chunk
        setStreamingContent(streamingContentRef.current)
        setChatTyping(false)
        return
      }

      if (msgType === 'tool_start' || msgType === 'tool_use_start' || msgType === 'tool_call_start') {
        const rawName = msgType === 'tool_start'
          ? String((msg as unknown as Record<string, unknown>).text ?? '')
          : String(msg.name ?? msg.tool_name ?? msg.tool ?? 'tool')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const toolId = String(msg.id ?? msg.tool_use_id ?? `tool-${Date.now()}`)
        const rawArgs = (msg as unknown as Record<string, unknown>).arguments
        const newStep: ToolStep = {
          id: toolId,
          name: toolName || 'tool',
          args: rawArgs != null
            ? (typeof rawArgs === 'string' ? rawArgs : JSON.stringify(rawArgs))
            : msg.input ? JSON.stringify(msg.input) : undefined,
          status: 'running',
        }
        streamingToolStepsRef.current = [...streamingToolStepsRef.current, newStep]
        setStreamingToolSteps([...streamingToolStepsRef.current])
        return
      }

      if (msgType === 'tool_result' || msgType === 'tool_call_result') {
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.tool_name ?? rawMsg.name ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const result = msg.content
          ? typeof msg.content === 'string' ? msg.content : JSON.stringify(msg.content)
          : String(rawMsg.text ?? rawMsg.result ?? '')
        const isError = Boolean(rawMsg.is_error ?? rawMsg.isError)
        const list = streamingToolStepsRef.current
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
          next[targetIdx] = { ...next[targetIdx], status: isError ? 'error' : 'done', result }
          streamingToolStepsRef.current = next
          setStreamingToolSteps([...next])
        }
        return
      }

      if (msgType === 'typing_stop' || msgType === 'assistant_done') {
        const ep = targetEndpointRef.current
        const sid = sessionIdRef.current
        const completedToolSteps = [...streamingToolStepsRef.current]
        setStreamingContent(null)
        setStreamingToolSteps([])
        setChatTyping(false)
        streamingContentRef.current = ''
        streamingToolStepsRef.current = []
        if (ep && sid) {
          void fetchSandboxSessionMessages(ep, sid)
            .then((msgs) => {
              const mapped = mapSandboxMessages(msgs)
              setChatMessages(mapped)
              if (completedToolSteps.length > 0) {
                setChatMessages((prev) => {
                  const lastBotIdx = [...prev].reverse().findIndex((m) => m.role !== 'user')
                  if (lastBotIdx === -1) return prev
                  const idx = prev.length - 1 - lastBotIdx
                  const updated = [...prev]
                  updated[idx] = { ...updated[idx], toolSteps: completedToolSteps }
                  return updated
                })
              }
            })
            .catch(() => {})
        }
        return
      }

      if (msgType === 'error') {
        setChatError(String(msg.text ?? msg.content ?? '目标沙箱返回错误'))
      }
    }

    ws.onReconnected = () => {
      const ep = targetEndpointRef.current
      const sid = sessionIdRef.current
      if (ep && sid) {
        void fetchSandboxSessionMessages(ep, sid).then((msgs) => {
          setChatMessages(mapSandboxMessages(msgs))
        }).catch(() => {})
      }
    }

    // connect() 必须在 await waitForOpen 之前调用，否则状态回调永远不会触发
    ws.connect()
    wsRef.current = ws
    connectionStateRef.current = { endpoint, sessionId: sessionIdRef.current }
    await waitForOpen
  }

  async function initTargetChat(endpoint: string): Promise<boolean> {
    if (chatReadyRef.current) return chatReadyRef.current

    chatReadyRef.current = (async () => {
      setChatLoading(true)
      setChatError('')
      try {
        targetEndpointRef.current = endpoint

        // 从目标沙箱获取会话列表，只选最新一条
        const sessionsResp = await fetchAdminSessions(endpoint, { pageSize: 25 })
        const allSessions = [
          ...sessionsResp.active,
          ...sessionsResp.persisted.items,
        ].sort(
          (a, b) => new Date(b.lastActiveAt).getTime() - new Date(a.lastActiveAt).getTime()
        )

        const latestSession = allSessions[0]
        const sessionId = latestSession?.id ?? `human-eval:${id ?? ''}:${Date.now()}`

        sessionIdRef.current = sessionId
        setSelectedSessionId(sessionId)

        if (latestSession?.id) {
          const msgs = await fetchSandboxSessionMessages(endpoint, sessionId)
          setChatMessages(mapSandboxMessages(msgs))
        }

        await connectTargetWs(endpoint)
        return true
      } catch (err: unknown) {
        setChatError(err instanceof Error ? err.message : '初始化目标沙箱对话失败')
        return false
      } finally {
        setChatLoading(false)
        chatReadyRef.current = null
      }
    })()

    return chatReadyRef.current
  }

  async function sendMessage() {
    if (!id || chatSending) return
    const content = chatInput.trim()
    if (!content) return

    const optimistic: HiringConversationMessage = {
      messageId: `local_${Date.now()}`,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }

    setChatInput('')
    setChatSending(true)
    setChatError('')
    setChatMessages((prev) => [...prev, optimistic])

    try {
      if (!wsRef.current?.isOpen() || !sessionIdRef.current) {
        const ep = targetEndpointRef.current
        if (!ep) throw new Error('目标沙箱连接地址缺失')
        await connectTargetWs(ep)
      }

      const ws = wsRef.current
      const sessionId = sessionIdRef.current
      if (!ws?.isOpen() || !sessionId) throw new Error('目标沙箱连接未建立，请稍后重试')

      setStreamingContent('')
      streamingContentRef.current = ''

      const sent = ws.send({
        type: 'user_message',
        text: content,
        sessionId,
        messageId: `human-eval-${Date.now()}`,
      })

      if (!sent) throw new Error('目标沙箱连接尚未就绪，请稍后重试')
    } catch (err: unknown) {
      setChatError(err instanceof Error ? err.message : '发送消息失败')
      setChatMessages((prev) => prev.filter((m) => m.messageId !== optimistic.messageId))
    } finally {
      setChatSending(false)
    }
  }

  async function submitDecision(decision: 'ONBOARD' | 'REJECT' | 'FORCE') {
    if (!id) return
    setSubmitting(true)
    setError('')
    try {
      if (decision === 'ONBOARD') {
        // 确认上岗：提交决策 + 直接设为 live
        await api.employeeRuntime.submitOnboardingDecision(id, { decision: 'ONBOARD' })
        const updated = await api.employeeRuntime.updateLifecycle(id, {
          status: 'live',
          stageSummary: '人工评估通过，已上岗',
          primarySignal: '运行稳定',
          signalLevel: 'ok',
        })
        setEmployee(updated)
        navigate(instanceBasePath(location.pathname, id))
      } else if (decision === 'FORCE') {
        // 强制上岗：与 ONBOARD 相同最终结果，但 EvalPhase 标记为 force
        const updated = await api.employeeRuntime.submitOnboardingDecision(id, { decision: 'FORCE' })
        setEmployee(updated)
        navigate(`${instanceBasePath(location.pathname, id)}/onboarding`)
      } else {
        // 评估不通过：进入 Review 流程
        const updated = await api.employeeRuntime.submitOnboardingDecision(id, { decision: 'REJECT' })
        setEmployee(updated)
        navigate(`${instanceBasePath(location.pathname, id)}/review`)
      }
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '提交评估结论失败')
    } finally {
      setSubmitting(false)
      setShowForceConfirm(false)
    }
  }

  if (loading) {
    return (
      <div className="hb-page hb-workflow-page hb-eval-page">
        <div className="hb-card hb-detail-state">
          <Loader2 size={16} className="animate-spin" />
          {t('humanEvaluation.loading')}
        </div>
      </div>
    )
  }

  if (!employee || !evaluation) {
    return (
      <div className="hb-page hb-workflow-page hb-eval-page">
        <div className="hb-card hb-detail-state">{t('humanEvaluation.notFound')}</div>
      </div>
    )
  }

  const workspaceReady = workspaceStatus?.overallStatus === 'ready'
  const workspaceNotStarted = !workspaceStatus || workspaceStatus.overallStatus === 'not_started'
  const workspaceFailed = workspaceStatus?.overallStatus === 'failed'

  return (
    <div className="hb-page hb-workflow-page hb-eval-page">
      <Breadcrumb
        items={[
          { label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' },
          { label: t('humanEvaluation.breadcrumb') },
        ]}
      />
      <div className="flex h-[calc(100vh-116px)] min-h-[680px] flex-col gap-3">

        {/* 顶部状态栏 */}
        <section className="hb-card p-2.5">
          <div className="flex flex-wrap items-center gap-2">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-1.5">
                <h1 className="text-[16px] font-semibold eval-text-strong">{t('humanEvaluation.title')}</h1>
                <span className="rounded-full border eval-pill-neutral px-2 py-0.5 text-[10px]">
                  {employee.nickname} · {employee.roleName}
                </span>
              </div>
              <div className="mt-1.5 flex flex-wrap gap-1.5 text-[10px]">
                <span className="rounded-full border eval-pill-violet px-2 py-0.5">
                  {t('humanEvaluation.statusInProgress')}
                </span>
                <span className={`rounded-full border px-2 py-0.5 ${sandboxConnected ? 'eval-badge-connected' : 'eval-badge-disconnected'}`}>
                  {sandboxConnected ? t('humanEvaluation.sandboxConnected') : t('humanEvaluation.sandboxDisconnected')}
                </span>
                {selectedSessionId && (
                  <span className="rounded-full border eval-pill-neutral px-2 py-0.5">
                    当前会话：{selectedSessionId.length > 18 ? `${selectedSessionId.slice(0, 8)}...${selectedSessionId.slice(-6)}` : selectedSessionId}
                  </span>
                )}
              </div>
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-1.5">
              <button
                type="button"
                onClick={() => setRightCollapsed((c) => !c)}
                className="hb-btn-ghost !px-2.5 !py-1 !text-[11px]"
              >
                <BarChart2 size={12} />
                {rightCollapsed ? t('humanEvaluation.togglePanel') : t('humanEvaluation.collapsePanel')}
              </button>
            </div>
          </div>

          {error && (
            <div className="mt-3 rounded-xl border eval-bar-error px-3 py-2 text-xs">
              <span className="inline-flex items-center gap-1.5">
                <AlertCircle size={12} />
                {error}
              </span>
            </div>
          )}
        </section>

        <section className="flex min-h-0 flex-1 gap-4">
          {/* 左侧：与目标沙箱的对话主区域 */}
          <div className="hb-card flex min-w-0 flex-1 flex-col overflow-hidden">
            <div className="border-b eval-chat-footer px-5 py-4">
              <div className="flex items-center gap-2">
                <div className="flex h-9 w-9 items-center justify-center rounded-2xl eval-icon-indigo">
                  <MessageCircle size={18} />
                </div>
                <div>
                  <div className="text-base font-semibold eval-text-title">与数字员工对话</div>
                  <div className="text-[12px] leading-5 eval-text-secondary">
                    直接与目标员工沙箱交互，验证真实工作能力。自动接入最新会话。
                  </div>
                </div>
              </div>
            </div>

            <div className="flex flex-1 flex-col overflow-hidden eval-chat-bg px-5 py-4">
              {workspaceNotStarted && (
                <div className="mb-3 rounded-2xl border eval-bar-error px-4 py-3 text-sm">
                  <div className="flex items-center gap-2">
                    <AlertCircle size={14} />
                    <span>{t('humanEvaluation.sandboxNotStarted')}</span>
                  </div>
                </div>
              )}
              {workspaceFailed && (
                <div className="mb-3 rounded-2xl border eval-bar-error px-4 py-3 text-sm">
                  <div className="flex items-center gap-2">
                    <AlertCircle size={14} />
                    <span>{t('humanEvaluation.sandboxFailed')}</span>
                  </div>
                </div>
              )}

              <div className="flex-1 space-y-3 overflow-y-auto">
                {chatLoading ? (
                  <div className="flex items-center gap-2 text-sm eval-text-muted">
                    <Loader2 size={14} className="animate-spin" />
                    {t('humanEvaluation.connectingSession')}
                  </div>
                ) : !workspaceReady ? null : chatMessages.length === 0 ? (
                  <div className="rounded-2xl border border-dashed eval-empty-chat px-4 py-4 text-sm leading-6">
                    {t('humanEvaluation.noMessages')}
                  </div>
                ) : (
                  chatMessages.map((message) => {
                    const isUser = message.role.toLowerCase() === 'user'
                    return (
                      <div key={message.messageId} className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
                        {!isUser && (
                          <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">员</div>
                        )}
                        <div className={`flex min-w-0 max-w-[90%] flex-col gap-1.5 ${isUser ? 'items-end' : 'items-start'}`}>
                          {!isUser && message.toolSteps && message.toolSteps.length > 0 && (
                            <HiringToolStepsBlock steps={message.toolSteps} />
                          )}
                          <div className={`rounded-2xl px-3 py-2.5 text-sm leading-6 ${isUser ? 'eval-bubble-user' : 'border eval-bubble-bot'}`}>
                            <div className={`mb-1 text-[11px] ${isUser ? 'eval-bubble-meta-user' : 'eval-bubble-meta-bot'}`}>
                              {isUser ? t('humanEvaluation.messageYou') : t('humanEvaluation.messageEmployee')} · {formatDateTime(message.createdAt)}
                            </div>
                            {isUser ? (
                              <div className="whitespace-pre-wrap break-words">{message.content}</div>
                            ) : (
                              <div className="hb-md prose prose-sm max-w-none break-words">
                                <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                  {message.content}
                                </ReactMarkdown>
                              </div>
                            )}
                          </div>
                        </div>
                        {isUser && (
                          <div className="hb-hiring-avatar is-user ml-2 mt-0.5 shrink-0">你</div>
                        )}
                      </div>
                    )
                  })
                )}

                {(streamingContent !== null || chatTyping) && (
                  <div className="flex justify-start">
                    <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">员</div>
                    <div className="flex min-w-0 max-w-[90%] flex-col items-start gap-1.5">
                      {streamingToolSteps.length > 0 && (
                        <HiringToolStepsBlock steps={streamingToolSteps} />
                      )}
                      {chatTyping && streamingContent === '' ? (
                        <div className="hb-hiring-bubble is-bot hb-hiring-bubble-loading">
                          {[0, 1, 2].map((i) => (
                            <span key={i} className="hb-hiring-typing-dot" style={{ animationDelay: `${i * 0.15}s` }} />
                          ))}
                        </div>
                      ) : streamingContent ? (
                        <div className="border eval-bubble-bot rounded-2xl px-3 py-2.5 text-sm leading-6">
                          <div className="mb-1 text-[11px] eval-bubble-meta-bot">{t('humanEvaluation.messageEmployee')} · {t('humanEvaluation.streaming')}</div>
                          <div className="hb-md prose prose-sm max-w-none break-words">
                            <ReactMarkdown remarkPlugins={[remarkGfm]}>{streamingContent}</ReactMarkdown>
                          </div>
                        </div>
                      ) : null}
                    </div>
                  </div>
                )}
                <div ref={chatEndRef} />
              </div>

              {chatError && (
                <div className="mt-2 shrink-0 rounded-xl border eval-bar-error px-3 py-2 text-xs">
                  <span className="inline-flex items-center gap-1.5">
                    <AlertCircle size={12} />
                    {chatError}
                  </span>
                </div>
              )}
            </div>

            {/* 输入区 */}
            <div className="shrink-0 border-t eval-chat-footer px-4 py-4">
              <div className="flex items-end gap-2">
                <textarea
                  value={chatInput}
                  onChange={(e) => setChatInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault()
                      void sendMessage()
                    }
                  }}
                  rows={2}
                  disabled={chatSending || !workspaceReady}
                  placeholder={!workspaceReady ? t('humanEvaluation.inputPlaceholderDisabled') : t('humanEvaluation.inputPlaceholder')}
                  className="eval-textarea min-h-[72px] flex-1 resize-y rounded-2xl border px-4 py-3.5 text-sm leading-6 disabled:opacity-60"
                />
                <button
                  type="button"
                  disabled={chatSending || !chatInput.trim() || !workspaceReady}
                  onClick={() => void sendMessage()}
                  className="hb-workflow-send-btn"
                >
                  {chatSending ? <Loader2 size={16} className="animate-spin" /> : <SendHorizontal size={16} />}
                </button>
              </div>

              {/* 评估结论操作区 */}
              <div className="mt-4 rounded-2xl border eval-decision-panel px-4 py-3">
                <div className="mb-2.5 text-[11px] font-semibold eval-text-body">{t('humanEvaluation.decisionTitle')}</div>
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => void submitDecision('ONBOARD')}
                    className="hb-btn-primary !px-3 !py-1.5 !text-[12px]"
                  >
                    <ShieldCheck size={13} />
                    {t('humanEvaluation.confirmOnboard')}
                  </button>
                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => {
                      if (!showForceConfirm) {
                        setShowForceConfirm(true)
                        return
                      }
                      void submitDecision('FORCE')
                    }}
                    className="hb-btn-ghost !px-3 !py-1.5 !text-[12px]"
                  >
                    <Zap size={13} />
                    {showForceConfirm ? t('humanEvaluation.forceOnboardConfirm') : t('humanEvaluation.forceOnboard')}
                  </button>
                  <button
                    type="button"
                    disabled={submitting}
                    onClick={() => {
                      setShowForceConfirm(false)
                      void submitDecision('REJECT')
                    }}
                    className="hb-btn-primary hb-btn-danger !px-3 !py-1.5 !text-[12px]"
                  >
                    <ShieldAlert size={13} />
                    {t('humanEvaluation.rejectEvaluation')}
                  </button>
                  {showForceConfirm && (
                    <button
                      type="button"
                      onClick={() => setShowForceConfirm(false)}
                      className="hb-btn-ghost !px-3 !py-1.5 !text-[12px]"
                    >
                      {t('humanEvaluation.cancelAction')}
                    </button>
                  )}
                </div>
                <p className="mt-2 text-[10px] eval-text-caption">
                  {t('humanEvaluation.decisionNote')}
                </p>
              </div>
            </div>
          </div>

          {/* 右侧：AI 评估参考面板（可折叠） */}
          {!rightCollapsed && (
            <div className="hb-card flex w-[320px] xl:w-[360px] 2xl:w-[400px] shrink-0 flex-col overflow-hidden">
              <div className="border-b eval-chat-footer px-4 py-3">
                <div className="flex items-center justify-between">
                  <div className="text-[13px] font-semibold eval-text-title">{t('humanEvaluation.aiReferencePanel')}</div>
                  <button
                    type="button"
                    onClick={() => setRightCollapsed(true)}
                    className="eval-text-caption hover:eval-text-secondary"
                  >
                    <ChevronUp size={14} />
                  </button>
                </div>
                <div className="mt-0.5 text-[11px] eval-text-secondary">{t('humanEvaluation.aiReferencePanelDesc')}</div>
              </div>

              <div className="flex-1 space-y-2 overflow-y-auto p-4">
                {evaluation.scenarios.length === 0 ? (
                  <div className="text-sm eval-text-caption">{t('humanEvaluation.noAiScenarios')}</div>
                ) : (
                  evaluation.scenarios.map((scenario) => (
                    <div key={scenario.scenarioId} className="rounded-2xl border eval-question-card px-3 py-2.5">
                      <div className="flex items-center justify-between gap-2">
                        <div className="min-w-0 truncate text-[12px] font-medium eval-text-title">
                          {scenario.scenarioName}
                        </div>
                        <span className={`shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-medium ${verdictPillClass(scenario.verdict)}`}>
                          {verdictLabel(scenario.verdict)}
                        </span>
                      </div>
                      {scenario.verdictComment && (
                        <div className="mt-1.5 text-[11px] leading-relaxed eval-text-secondary">
                          {scenario.verdictComment}
                        </div>
                      )}
                    </div>
                  ))
                )}

                {evaluation.latestReport && (
                  <div className="mt-3 rounded-2xl border eval-stats-badge-ontology px-3 py-2.5">
                    <div className="text-[11px] font-semibold eval-text-indigo-label">{t('humanEvaluation.aiOverallScore')}</div>
                    <div className="mt-1 flex items-baseline gap-1">
                      <span className="text-2xl font-bold eval-text-indigo">
                        {evaluation.latestReport.overallScore}
                      </span>
                      <span className="text-[11px] eval-text-indigo">/ 100</span>
                    </div>
                    <div className="mt-1 text-[10px] eval-text-indigo">
                      {evaluation.latestReport.passed ? t('humanEvaluation.aiPassed') : t('humanEvaluation.aiFailed')}
                    </div>
                  </div>
                )}

                {evaluation.recommendation && (
                  <div className="rounded-2xl border eval-recommendation px-3 py-2.5">
                    <div className="mb-1 text-[11px] font-semibold eval-text-body">{t('humanEvaluation.aiSuggestion')}</div>
                    <div className="text-[11px] leading-relaxed eval-text-secondary">
                      {evaluation.recommendation}
                    </div>
                  </div>
                )}
              </div>

              <div className="shrink-0 border-t eval-chat-footer px-4 py-3">
                <div className="flex items-center gap-2">
                  <CheckCircle2 size={12} className="eval-text-caption" />
                  <span className="text-[10px] eval-text-caption">
                    {t('humanEvaluation.passRate', { passed: evaluation.scenarios.filter((s) => s.verdict === 'passed').length, total: evaluation.scenarios.length })}
                  </span>
                </div>
              </div>
            </div>
          )}

          {rightCollapsed && (
            <button
              type="button"
              onClick={() => setRightCollapsed(false)}
              className="hb-card flex w-8 shrink-0 items-center justify-center eval-text-caption hover:eval-text-secondary"
            >
              <ChevronDown size={14} />
            </button>
          )}
        </section>
      </div>
    </div>
  )
}

