import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  ArrowLeft,
  BarChart2,
  CheckCircle2,
  ChevronDown,
  ExternalLink,
  FileText,
  Loader2,
  LogOut,
  MessageSquare,
  Package,
  PlayCircle,
  RefreshCw,
  SendHorizontal,
  Upload,
  Zap,
} from 'lucide-react'
import { useKeycloak } from '@react-keycloak/web'
import { useNavigate, useParams } from 'react-router-dom'
import {
  api,
  type EmployeeDetail,
  type EvaluationSandboxConversationState,
  type EvaluationScenario,
  type EvaluationState,
  type HiringConversationMessage,
} from '@/infra/api'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

type ArtifactTab = 'testcase' | 'trace' | 'report'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5280'

function toAbsoluteApiUrl(path?: string | null): string | null {
  if (!path) return null
  const trimmed = path.trim()
  if (!trimmed) return null
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  return new URL(trimmed.startsWith('/') ? trimmed : `/${trimmed}`, API_BASE_URL).toString()
}

function verdictLabel(verdict?: string | null) {
  if (verdict === 'passed') return '通过'
  if (verdict === 'failed') return '不通过'
  if (verdict === 'warning') return '待优化'
  return '待判定'
}

function verdictPillClass(verdict?: string | null) {
  if (verdict === 'passed') return 'hb-pill green'
  if (verdict === 'failed') return 'hb-pill pink'
  if (verdict === 'warning') return 'hb-pill orange'
  return 'hb-pill gray'
}

function scenarioScore(verdict?: string | null) {
  if (verdict === 'passed') return 90
  if (verdict === 'warning') return 70
  if (verdict === 'failed') return 45
  return 0
}

function calcDerivedScore(scenarios: EvaluationScenario[]) {
  const completed = scenarios.filter((item) => item.verdict === 'passed' || item.verdict === 'warning' || item.verdict === 'failed')
  if (completed.length === 0) return 0
  const total = completed.reduce((sum, item) => sum + scenarioScore(item.verdict), 0)
  return Math.round(total / completed.length)
}

function formatTime(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleTimeString('zh-CN', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
  })
}

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

function progressStepByState(params: {
  canStart: boolean
  needLoadSkill: boolean
  aiRunning: boolean
}) {
  if (params.canStart) return 0
  if (params.needLoadSkill) return 1
  if (params.aiRunning) return 2
  return 3
}

export default function EvaluationPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const keycloak = useOptionalKeycloak()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const [artifactTab, setArtifactTab] = useState<ArtifactTab>('report')
  const [rightCollapsed, setRightCollapsed] = useState(false)
  const [selectedRound, setSelectedRound] = useState(1)

  const [chatMessages, setChatMessages] = useState<HiringConversationMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatSending, setChatSending] = useState(false)
  const [chatError, setChatError] = useState('')
  const [sandboxConversation, setSandboxConversation] = useState<EvaluationSandboxConversationState | null>(null)
  const [logoutLoading, setLogoutLoading] = useState(false)
  const chatEndRef = useRef<HTMLDivElement | null>(null)
  const pollingCancelledRef = useRef(false)
  const lastMessageCountRef = useRef(0)
  const stablePollCountRef = useRef(0)

  async function loadData() {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const [employeeData, evaluationData] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getEvaluationState(id),
      ])
      setEmployee(employeeData)
      setEvaluation(evaluationData)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载 AI 评估数据失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [id])

  const overview = useMemo(() => {
    if (!evaluation) {
      return { total: 0, passed: 0, failed: 0, pending: 0, score: 0 }
    }

    const total = evaluation.scenarios.length
    const passed = evaluation.scenarios.filter((scenario) => scenario.verdict === 'passed').length
    const failed = evaluation.scenarios.filter((scenario) => scenario.verdict === 'failed').length
    const pending = total - passed - failed
    const score = evaluation.latestReport?.overallScore ?? calcDerivedScore(evaluation.scenarios)
    return { total, passed, failed, pending, score }
  }, [evaluation])

  const canStart = employee?.status === 'hired' || employee?.status === 'failed'
  const isAiStage = employee?.status === 'interning_ai'
  const aiRunning = isAiStage && employee?.evalPhase === 'ai_running'
  const needLoadSkill = isAiStage && !aiRunning

  const currentRound = Math.max(1, employee?.evalIteration ?? 1)
  const maxRounds = Math.max(currentRound, employee?.evalMaxIterations ?? 30)
  const roundOptions = Array.from({ length: currentRound }, (_, index) => index + 1)

  const progressStep = progressStepByState({ canStart, needLoadSkill, aiRunning })

  const evaluatorHireId = sandboxConversation?.evaluatorHireId ?? null
  const evaluatorSandboxId = sandboxConversation?.evaluatorSandboxId ?? null

  useEffect(() => {
    setSelectedRound(currentRound)
  }, [currentRound])

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatLoading, chatMessages])

  async function submitAiDecision(decision: 'START' | 'LOAD_SKILL' | 'RUN') {
    if (!id) return
    setSubmitting(true)
    setError('')

    try {
      const updated = await api.employeeRuntime.submitAiEvaluationDecision(id, { decision })
      setEmployee(updated)

      if (decision === 'RUN') {
        if (updated.status === 'interning_human') {
          navigate(`/instances/${id}/human-evaluation`)
          return
        }
        if (updated.status === 'failed') {
          navigate(`/instances/${id}/review`)
          return
        }
      }

      if (decision === 'RUN' && updated.evalPhase === 'pending_human_review') {
        navigate(`/instances/${id}/human-evaluation`)
        return
      }

      const evaluationState = await api.employeeRuntime.getEvaluationState(id)
      setEvaluation(evaluationState)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '提交 AI 评估动作失败')
    } finally {
      setSubmitting(false)
    }
  }

  async function loadEvaluatorConversation() {
    if (!id) {
      setChatMessages([])
      setSandboxConversation(null)
      return
    }

    setChatLoading(true)
    setChatError('')
    try {
      const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id)
      setSandboxConversation(conversation)
      setChatMessages(conversation.messages ?? [])
    } catch (conversationError: unknown) {
      setChatError(conversationError instanceof Error ? conversationError.message : '读取评估沙箱对话失败')
    } finally {
      setChatLoading(false)
    }
  }

  async function sendEvaluatorMessage() {
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
      const conversation = await api.employeeRuntime.sendEvaluationSandboxMessage(id, { content })
      setSandboxConversation(conversation)
      setChatMessages(conversation.messages ?? [])
    } catch (sendError: unknown) {
      setChatError(sendError instanceof Error ? sendError.message : '发送消息到评估沙箱失败')
      setChatMessages((prev) => prev.filter((item) => item.messageId !== optimistic.messageId))
    } finally {
      setChatSending(false)
    }
  }

  async function handleLogout() {
    if (!keycloak) {
      navigate('/department-employees')
      return
    }
    setLogoutLoading(true)
    try {
      await keycloak.logout({
        redirectUri: `${window.location.origin}/login`,
      })
    } catch (logoutError: unknown) {
      setError(logoutError instanceof Error ? logoutError.message : '退出登录失败，请稍后重试')
      setLogoutLoading(false)
    }
  }

  useEffect(() => {
    if (!aiRunning || !id) {
      pollingCancelledRef.current = true
      setChatMessages([])
      setChatError('')
      setSandboxConversation(null)
      return
    }

    pollingCancelledRef.current = false
    lastMessageCountRef.current = 0
    stablePollCountRef.current = 0

    let timer: number
    let currentInterval = 6000
    let lastMessageId: string | null = null
    let noChangeCount = 0
    const maxNoChangeBeforeStop = 3  // ~30s total after slowdown kicks in

    function isConversationDone(messages: HiringConversationMessage[]): boolean {
      if (messages.length === 0) return false
      const last = messages[messages.length - 1]
      // AI is done when last message is assistant, non-empty, and not a tool_use
      return last.role === 'assistant'
        && last.content.length > 0
        && !last.content.includes('[tool_use]')
    }

    function scheduleNext() {
      timer = window.setTimeout(() => {
        void pollConversation()
      }, currentInterval)
    }

    let shouldStop = false

    async function pollConversation() {
      if (pollingCancelledRef.current) return
      setChatLoading(true)
      setChatError('')
      try {
        const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id!, lastMessageId)
        if (pollingCancelledRef.current) return
        // null means 304 Not Modified — no new messages, keep current state
        if (conversation === null) {
          noChangeCount++
          if (noChangeCount >= maxNoChangeBeforeStop) {
            shouldStop = true
            return
          }
          return
        }
        noChangeCount = 0
        setSandboxConversation(conversation)
        const newMessages = conversation.messages ?? []
        setChatMessages(newMessages)
        const newCount = newMessages.length
        if (newCount > 0) {
          lastMessageId = newMessages[newCount - 1].messageId
        }
        if (newCount === lastMessageCountRef.current && newCount > 0) {
          stablePollCountRef.current++
        } else {
          stablePollCountRef.current = 0
        }
        lastMessageCountRef.current = newCount
        // Slow down: 6s -> 12s -> 20s
        if (stablePollCountRef.current >= 10) {
          currentInterval = 20000
        } else if (stablePollCountRef.current >= 4) {
          currentInterval = 12000
        }
        if (isConversationDone(newMessages)) {
          shouldStop = true
          return
        }
      } catch (conversationError: unknown) {
        if (!pollingCancelledRef.current) {
          setChatError(conversationError instanceof Error ? conversationError.message : '读取评估沙箱对话失败')
        }
      } finally {
        if (!pollingCancelledRef.current) {
          setChatLoading(false)
          if (!shouldStop) {
            scheduleNext()
          }
        }
      }
    }

    void pollConversation()
    return () => {
      pollingCancelledRef.current = true
      window.clearTimeout(timer)
    }
  }, [aiRunning, id])

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-[220px] items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载 AI 评估...
        </div>
      </div>
    )
  }

  if (!employee || !evaluation) {
    return (
      <div className="hb-page">
        <div className="hb-card p-8 text-sm text-[#737373]">评估数据不存在</div>
      </div>
    )
  }

  const traceAssets = (evaluation.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)

  return (
    <div className="hb-page">
      <div className="flex h-[calc(100vh-132px)] min-h-[680px] flex-col gap-4">
        <section className="hb-card p-4">
          <div className="flex flex-wrap items-start gap-3">
            <button
              type="button"
              onClick={() => navigate(id ? `/instances/${id}` : '/department-employees')}
              className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
            >
              <ArrowLeft size={12} />
              返回
            </button>
            <div className="h-6 w-px bg-[#ececec]" />
            <div className="min-w-0 flex-1">
              <h1 className="text-[18px] font-semibold text-[#0a0a0a]">AI 评估训练</h1>
              <p className="mt-0.5 truncate text-xs text-[#737373]">
                {employee.nickname} · {employee.roleName}
              </p>
              <p className="mt-1 text-xs text-[#737373]">当前阶段：{employee.stageSummary || '待发起 AI 评估'}</p>
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => void handleLogout()}
                className="hb-btn-ghost !px-3 !py-1.5 !text-xs text-[#b91c1c] hover:!bg-[#fef2f2]"
                disabled={logoutLoading}
              >
                {logoutLoading ? <Loader2 size={12} className="animate-spin" /> : <LogOut size={12} />}
                退出登录
              </button>
              <button
                type="button"
                disabled={submitting || !canStart}
                className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                onClick={() => void submitAiDecision('START')}
              >
                <PlayCircle size={12} />
                发起 AI 评估
              </button>
              <button
                type="button"
                disabled={submitting || !needLoadSkill}
                className="hb-btn-primary !px-3 !py-1.5 !text-xs"
                onClick={() => void submitAiDecision('LOAD_SKILL')}
              >
                <Upload size={12} />
                上传并加载评估 Skill
              </button>
              <button
                type="button"
                disabled={submitting || !aiRunning}
                className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                onClick={() => void submitAiDecision('RUN')}
              >
                <CheckCircle2 size={12} />
                执行 AI 评估
              </button>
            </div>
          </div>

          {error && (
            <div className="mt-3 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
              <span className="inline-flex items-center gap-1.5">
                <AlertCircle size={12} />
                {error}
              </span>
            </div>
          )}

          <div className="mt-3 rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-medium text-[#404040]">
                第 {currentRound}/{maxRounds} 评训轮次
              </div>
              <div className="text-xs text-[#737373]">evalPhase: {employee.evalPhase || '未设置'}</div>
            </div>
            <div className="mt-2 h-1.5 w-full rounded-full bg-[#efefef]">
              <div className="h-1.5 rounded-full bg-[#4a6cf7]" style={{ width: `${((progressStep + 1) / 4) * 100}%` }} />
            </div>
            <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
              {['本体检查', '确认考题', '执行测试', '评估判分'].map((step, index) => (
                <span
                  key={step}
                  className={`rounded-full px-2 py-1 font-medium ${
                    index < progressStep
                      ? 'bg-[#e6f5ec] text-[#15803d]'
                      : index === progressStep
                      ? 'bg-[#e8edff] text-[#4a6cf7]'
                      : 'bg-[#efefef] text-[#737373]'
                  }`}
                >
                  {step}
                </span>
              ))}
            </div>
            <div className="mt-2 text-[11px] text-[#737373]">
              评估 Skill 加载策略：调用雇佣侧已有上传 Skill 接口，后端将评估 Skill 注入评估沙箱后执行并回传判定结果。
            </div>
          </div>
        </section>

        <section className="flex min-h-0 flex-1 gap-4">
          <div className="hb-card flex min-w-0 flex-1 flex-col overflow-hidden">
            <div className="px-4 pt-4">
              <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
                <div className="mb-2 flex items-center gap-1.5 text-xs font-semibold text-[#404040]">
                  <BarChart2 size={12} />
                  评估概览
                </div>
                <div className="grid gap-2 text-xs sm:grid-cols-5">
                  <div className="rounded-xl border border-[#ececec] bg-white p-2">
                    <div className="text-[#737373]">场景总数</div>
                    <div className="mt-1 text-sm font-semibold text-[#0a0a0a] tabular-nums">{overview.total}</div>
                  </div>
                  <div className="rounded-xl border border-[#ececec] bg-white p-2">
                    <div className="text-[#737373]">通过</div>
                    <div className="mt-1 text-sm font-semibold text-[#15803d] tabular-nums">{overview.passed}</div>
                  </div>
                  <div className="rounded-xl border border-[#ececec] bg-white p-2">
                    <div className="text-[#737373]">未通过</div>
                    <div className="mt-1 text-sm font-semibold text-[#be185d] tabular-nums">{overview.failed}</div>
                  </div>
                  <div className="rounded-xl border border-[#ececec] bg-white p-2">
                    <div className="text-[#737373]">待判定</div>
                    <div className="mt-1 text-sm font-semibold text-[#c47a26] tabular-nums">{overview.pending}</div>
                  </div>
                  <div className="rounded-xl border border-[#ececec] bg-white p-2">
                    <div className="text-[#737373]">综合评分</div>
                    <div className="mt-1 text-sm font-semibold text-[#0a0a0a] tabular-nums">{overview.score}</div>
                  </div>
                </div>
              </div>
            </div>
            <div className="flex-1 overflow-y-auto px-4 pb-3 pt-3">
              <div className="my-2 flex justify-center">
                <span className="rounded-full bg-[#fafafa] px-3 py-1 text-xs text-[#9ca3af]">
                  {formatTime(employee.createdAt)} · {employee.stageSummary || '评估专家已就绪，等待评估动作。'}
                </span>
              </div>

              {evaluation.scenarios.map((scenario) => (
                <div key={scenario.scenarioId} className="my-2 rounded-2xl border border-[#ececec] bg-white p-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-sm font-semibold text-[#0a0a0a]">{scenario.scenarioName}</div>
                    <span className={verdictPillClass(scenario.verdict)}>{verdictLabel(scenario.verdict)}</span>
                  </div>
                  <div className="mt-1 text-xs text-[#737373]">
                    状态：{scenario.status} · 消息数：{scenario.messageCount}
                  </div>
                  {scenario.verdictComment && (
                    <div className="mt-2 rounded-xl border border-[#f3f4f6] bg-[#fafafa] px-2.5 py-2 text-xs text-[#404040]">
                      备注：{scenario.verdictComment}
                    </div>
                  )}
                </div>
              ))}

            </div>

            <div className="border-t border-[#ececec] bg-[#fafafa] px-4 py-3">
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-1.5 text-xs font-semibold text-[#404040]">
                  <MessageSquare size={12} />
                  评估沙箱对话窗口
                </div>
                <div className="flex items-center gap-2">
                  <span className="hb-pill blue">{evaluatorHireId ? `evalHireId=${evaluatorHireId}` : '评估沙箱初始化中'}</span>
                  <button
                    type="button"
                    disabled={!aiRunning || chatLoading || chatSending}
                    className="hb-btn-ghost !px-2.5 !py-1 !text-[11px]"
                    onClick={() => void loadEvaluatorConversation()}
                  >
                    <RefreshCw size={11} />
                    刷新
                  </button>
                </div>
              </div>

              {!aiRunning ? (
                <div className="rounded-xl border border-[#ececec] bg-white px-3 py-2 text-xs text-[#737373]">
                  请先执行“上传并加载评估 Skill”，进入 ai_running 后可连接评估沙箱。
                </div>
              ) : (!evaluatorHireId && chatLoading) ? (
                <div className="rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
                  评估沙箱初始化中，请稍候...
                </div>
              ) : (
                <div className="overflow-hidden rounded-xl border border-[#ececec] bg-white">
                  <div className="max-h-44 space-y-2 overflow-y-auto px-3 py-2">
                    {chatLoading ? (
                      <div className="flex items-center gap-1.5 text-xs text-[#737373]">
                        <Loader2 size={12} className="animate-spin" />
                        正在加载评估沙箱对话...
                      </div>
                    ) : chatMessages.length === 0 ? (
                      <div className="text-xs text-[#737373]">暂无对话，发送消息后将同步评估沙箱回复。</div>
                    ) : (
                      chatMessages.map((message) => {
                        const isUser = message.role.toLowerCase() === 'user'
                        return (
                          <div key={message.messageId} className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
                            <div
                              className={`max-w-[90%] rounded-xl px-2.5 py-2 text-xs leading-relaxed ${
                                isUser
                                  ? 'bg-[#000000] text-white'
                                  : 'border border-[#ececec] bg-[#fafafa] text-[#404040]'
                              }`}
                            >
                              <div className={`mb-1 text-[10px] ${isUser ? 'text-[#e5e5e5]' : 'text-[#9ca3af]'}`}>
                                {isUser ? '你' : '评估沙箱'} · {formatDateTime(message.createdAt)}
                              </div>
                              <div className="whitespace-pre-wrap break-words">{message.content}</div>
                            </div>
                          </div>
                        )
                      })
                    )}
                    <div ref={chatEndRef} />
                  </div>
                  <div className="border-t border-[#ececec] px-2.5 py-2">
                    <div className="flex items-end gap-2">
                      <textarea
                        value={chatInput}
                        onChange={(event) => setChatInput(event.target.value)}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter' && !event.shiftKey) {
                            event.preventDefault()
                            void sendEvaluatorMessage()
                          }
                        }}
                        rows={2}
                        disabled={chatSending}
                        placeholder="向评估沙箱发送消息（Enter 发送，Shift+Enter 换行）"
                        className="min-h-[44px] flex-1 resize-y rounded-xl border border-[#e5e5e5] bg-[#fafafa] px-3 py-2 text-xs outline-none focus:border-[#4a6cf7] disabled:opacity-60"
                      />
                      <button
                        type="button"
                        disabled={chatSending || !chatInput.trim()}
                        className="hb-btn-primary !px-2.5 !py-2 disabled:!bg-[#d4d4d8]"
                        onClick={() => void sendEvaluatorMessage()}
                      >
                        {chatSending ? <Loader2 size={12} className="animate-spin" /> : <SendHorizontal size={12} />}
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {evaluatorSandboxId && <div className="mt-2 text-[11px] text-[#9ca3af]">evalSandboxId: {evaluatorSandboxId}</div>}

              {chatError && (
                <div className="mt-2 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-2.5 py-1.5 text-[11px] text-[#b3263c]">
                  {chatError}
                </div>
              )}
            </div>
          </div>
          <div
            className={`${
              rightCollapsed ? 'w-10' : 'w-[360px]'
            } hb-card flex shrink-0 flex-col overflow-hidden transition-all duration-200`}
          >
            {rightCollapsed ? (
              <button
                type="button"
                onClick={() => setRightCollapsed(false)}
                className="flex h-full w-full items-center justify-center transition-colors hover:bg-[#fafafa]"
              >
                <ChevronDown size={16} className="-rotate-90 text-[#9ca3af]" />
              </button>
            ) : (
              <>
                <div className="flex items-center justify-between border-b border-[#ececec] px-3 py-2.5">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-semibold text-[#404040]">轮次</span>
                    <select
                      value={selectedRound}
                      onChange={(event) => setSelectedRound(Number(event.target.value))}
                      className="rounded border border-[#e5e5e5] bg-[#fafafa] px-2 py-1 text-xs outline-none focus:border-[#4a6cf7]"
                    >
                      {roundOptions.map((round) => (
                        <option key={round} value={round}>
                          第 {round} 轮 {round === currentRound ? `(评分 ${overview.score})` : ''}
                        </option>
                      ))}
                    </select>
                  </div>
                  <button
                    type="button"
                    onClick={() => setRightCollapsed(true)}
                    className="rounded p-1 text-[#9ca3af] transition-colors hover:bg-[#fafafa] hover:text-[#404040]"
                  >
                    <ChevronDown size={14} className="rotate-90" />
                  </button>
                </div>

                <div className="flex border-b border-[#ececec]">
                  {[
                    { key: 'testcase' as ArtifactTab, label: '测试用例', icon: FileText },
                    { key: 'trace' as ArtifactTab, label: '执行轨迹', icon: Zap },
                    { key: 'report' as ArtifactTab, label: '评估报告', icon: BarChart2 },
                  ].map((tab) => (
                    <button
                      key={tab.key}
                      type="button"
                      onClick={() => setArtifactTab(tab.key)}
                      className={`flex flex-1 items-center justify-center gap-1 border-b-2 py-2 text-xs font-medium ${
                        artifactTab === tab.key ? 'border-[#0a0a0a] text-[#0a0a0a]' : 'border-transparent text-[#737373]'
                      }`}
                    >
                      <tab.icon size={11} />
                      {tab.label}
                    </button>
                  ))}
                </div>

                <div className="flex-1 overflow-y-auto p-3 text-xs">
                  {artifactTab === 'testcase' && (
                    <div className="space-y-3">
                      <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-2.5">
                        <div className="mb-1.5 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <Package size={11} />
                          测试场景（{evaluation.scenarios.length}）
                        </div>
                        <div className="space-y-1.5">
                          {evaluation.scenarios.map((scenario) => (
                            <div key={scenario.scenarioId} className="rounded-xl border border-[#ececec] bg-white px-2.5 py-2">
                              <div className="font-medium text-[#0a0a0a]">{scenario.scenarioName}</div>
                              <div className="mt-1 text-[11px] text-[#737373]">
                                状态：{scenario.status} · 消息数：{scenario.messageCount}
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    </div>
                  )}

                  {artifactTab === 'trace' && (
                    <div className="space-y-2">
                      {traceAssets.map((asset, index) => {
                        const traceLink = toAbsoluteApiUrl(asset.publicUrl)
                        const relatedScenario = evaluation.scenarios.find(
                          (item) => item.scenarioId === asset.relatedKey || item.scenarioName === asset.relatedKey,
                        )
                        return (
                          <div key={asset.relativePath} className="rounded-xl border border-[#ececec] bg-[#fafafa] p-2.5">
                            <div className="mb-1 flex items-center gap-1.5">
                              <Zap size={10} className="text-[#4a6cf7]" />
                              <span className="font-semibold text-[#404040]">
                                轨迹 #{index + 1} {asset.relatedKey ? `· ${asset.relatedKey}` : ''}
                              </span>
                            </div>
                            <div className="text-[11px] text-[#737373]">创建时间：{formatDateTime(asset.createdAtUtc)}</div>
                            {relatedScenario && (
                              <div className="mt-1 text-[11px] text-[#737373]">
                                场景：{relatedScenario.scenarioName} · 判定：{verdictLabel(relatedScenario.verdict)} · 消息数：
                                {relatedScenario.messageCount}
                              </div>
                            )}
                            <div className="mt-1 break-all text-[11px] text-[#9ca3af]">{asset.relativePath}</div>
                            {traceLink && (
                              <a
                                href={traceLink}
                                target="_blank"
                                rel="noreferrer"
                                className="mt-2 inline-flex items-center gap-1 text-[11px] text-[#2563eb]"
                              >
                                <ExternalLink size={10} />
                                查看原始 Trace JSON
                              </a>
                            )}
                          </div>
                        )
                      })}
                      {(traceAssets.length === 0 ? evaluation.scenarios : []).map((scenario, index) => (
                        <div key={scenario.scenarioId} className="rounded-xl border border-[#ececec] bg-[#fafafa] p-2.5">
                          <div className="mb-1 flex items-center gap-1.5">
                            <Zap size={10} className="text-[#4a6cf7]" />
                            <span className="font-semibold text-[#404040]">
                              轨迹 #{index + 1} · {scenario.scenarioName}
                            </span>
                          </div>
                          <div className="text-[11px] text-[#737373]">开始：{formatDateTime(scenario.startedAt)}</div>
                          <div className="text-[11px] text-[#737373]">结束：{formatDateTime(scenario.completedAt)}</div>
                          <div className="mt-1 text-[11px] text-[#737373]">
                            消息数：{scenario.messageCount} · 判定：{verdictLabel(scenario.verdict)}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}

                  {artifactTab === 'report' && (
                    <div className="space-y-3">
                      <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-3 text-center">
                        <div className="text-4xl font-bold text-[#0a0a0a] tabular-nums">{overview.score}</div>
                        <div className="mt-1 text-xs text-[#737373]">综合评分（估算）</div>
                        <div className="mt-2 inline-flex">
                          <span className={overview.failed === 0 ? 'hb-pill green' : 'hb-pill pink'}>
                            {overview.failed === 0 ? 'AI 判定：通过' : 'AI 判定：待修复'}
                          </span>
                        </div>
                      </div>

                      <div className="rounded-xl border border-[#ececec] bg-white p-2.5">
                        <div className="mb-2 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <BarChart2 size={11} />
                          场景评分分布
                        </div>
                        <div className="space-y-2">
                          {evaluation.scenarios.map((scenario) => {
                            const score = scenarioScore(scenario.verdict)
                            return (
                              <div key={scenario.scenarioId} className="rounded-lg border border-[#f5f5f5] bg-[#fafafa] p-2">
                                <div className="mb-1 flex items-center justify-between gap-2">
                                  <span className="text-[11px] text-[#404040]">{scenario.scenarioName}</span>
                                  <span className="tabular-nums text-[11px] font-semibold text-[#0a0a0a]">{score}</span>
                                </div>
                                <div className="h-1.5 rounded-full bg-[#efefef]">
                                  <div
                                    className={`h-1.5 rounded-full ${
                                      scenario.verdict === 'passed'
                                        ? 'bg-[#10b981]'
                                        : scenario.verdict === 'failed'
                                          ? 'bg-[#be185d]'
                                          : 'bg-[#c47a26]'
                                    }`}
                                    style={{ width: `${score}%` }}
                                  />
                                </div>
                              </div>
                            )
                          })}
                        </div>
                      </div>

                      <div className="rounded-xl border border-[#ececec] bg-white p-2.5">
                        <div className="mb-1 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <FileText size={11} />
                          评估建议
                        </div>
                        <p className="text-[11px] leading-relaxed text-[#737373]">
                          {evaluation.recommendation || '建议优先修复未通过场景，完成后继续执行下一轮 AI 评估。'}
                        </p>
                      </div>
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        </section>
      </div>
    </div>
  )
}

function useOptionalKeycloak() {
  if (isAuthBypassed) {
    return null
  }

  const { keycloak } = useKeycloak()
  return keycloak
}
