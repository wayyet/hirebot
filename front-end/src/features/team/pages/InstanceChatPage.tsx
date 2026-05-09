import { useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, ArrowLeft, Loader2, MessageCircle, Send, Trash2 } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'

import { api, type EmployeeDetail, type InstanceChatMessage } from '@/infra/api'
import { tokenService } from '@/infra/auth/token-service'
import { GatewayWs } from '@/infra/sandbox/gateway-ws'
import { fetchSandboxSessionMessages } from '@/infra/sandbox/sandbox-api'
import {
  firstCharacter,
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from '@/features/hiring/pages/employeeView'

type ChatDraft = {
  content: string
}

function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString('zh-CN', {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function normalizeMessageContent(content: string) {
  return content.replace(/<think>[\s\S]*?<\/think>/gi, '').trim()
}

function mapMessages(messages: InstanceChatMessage[]) {
  return messages
    .filter(message => message.content.trim().length > 0)
    .map(message => ({
      ...message,
      content: normalizeMessageContent(message.content),
    }))
}

function mapSandboxMessages(messages: { type: string; content?: string; text?: string }[]) {
  return messages
    .filter(message => message.type === 'user_message' || message.type === 'assistant_message')
    .map<InstanceChatMessage>((message, index) => ({
      messageId: `sandbox-${index}-${Date.now()}`,
      role: message.type === 'user_message' ? 'user' : 'assistant',
      content: normalizeMessageContent(String(message.content ?? message.text ?? '')),
      createdAt: new Date().toISOString(),
    }))
    .filter(message => message.content.trim().length > 0)
}

function normalizeErrorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message
  }

  return '请求失败，请稍后重试'
}

function resolveSandboxGatewayEndpoint() {
  const runtimeEndpoint = typeof window !== 'undefined'
    ? window.__AUTH_CONFIG__?.SandboxGatewayEndpoint?.trim()
    : ''
  const envEndpoint = (import.meta.env.VITE_SANDBOX_GATEWAY_ENDPOINT as string | undefined)?.trim() ?? ''
  return runtimeEndpoint || envEndpoint || null
}

export default function InstanceChatPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [messages, setMessages] = useState<InstanceChatMessage[]>([])
  const [draft, setDraft] = useState<ChatDraft>({ content: '' })
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [typing, setTyping] = useState(false)
  const [error, setError] = useState('')
  const [clearing, setClearing] = useState(false)
  const [streamingContent, setStreamingContent] = useState<string | null>(null)

  const bottomRef = useRef<HTMLDivElement | null>(null)
  const wsRef = useRef<GatewayWs | null>(null)
  const gatewayEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)

  const employeeView = useMemo(() => {
    if (!employee) return null
    return withEmployeeView(toEmployeeDetailSummary(employee))
  }, [employee])

  const canChat = employeeView?.ownership === 'personal_clone' || employeeView?.ownership === 'private_branch'
  const isLive = employeeView?.mappedStatus === 'live'
  const directSandboxEndpoint = resolveSandboxGatewayEndpoint()

  async function syncSandboxHistory(endpoint: string, sessionId: string) {
    const sandboxMessages = await fetchSandboxSessionMessages(endpoint, sessionId)
    const mapped = mapSandboxMessages(sandboxMessages)
    setMessages(prev => (mapped.length >= prev.length ? mapped : prev))
  }

  async function connectSandboxWs(endpoint: string) {
    wsRef.current?.disconnect()

    const token = await tokenService.ensureFresh()
    if (!token) return

    const ws = new GatewayWs(endpoint, token)

    ws.onMessage = (msg) => {
      const type = String(msg.type ?? '')
      if (type === 'typing_start') {
        setStreamingContent('')
        setTyping(true)
        return
      }

      if (type === 'text_delta' || type === 'assistant_chunk') {
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        setStreamingContent(prev => (prev === null ? chunk : prev + chunk))
        return
      }

      if (type === 'typing_stop' || type === 'assistant_done') {
        setStreamingContent(prev => {
          if (prev && prev.trim().length > 0) {
            const cleaned = normalizeMessageContent(prev)
            if (cleaned.length > 0) {
              setMessages(current => [
                ...current,
                {
                  messageId: `local-${Date.now()}`,
                  role: 'assistant',
                  content: cleaned,
                  createdAt: new Date().toISOString(),
                },
              ])
            }
          }
          return null
        })
        setTyping(false)

        const sandboxSessionId = sessionIdRef.current
        const sandboxGatewayEndpoint = gatewayEndpointRef.current
        if (sandboxSessionId && sandboxGatewayEndpoint) {
          void syncSandboxHistory(sandboxGatewayEndpoint, sandboxSessionId).catch(() => {
            // 历史同步失败时保留当前已渲染内容
          })
        }
      }
    }

    ws.onStateChange = (state) => {
      if (state === 'closed' || state === 'error') {
        setTyping(false)
      }
    }

    ws.connect()
    wsRef.current = ws
  }

  async function loadChat(instanceId: string) {
    setLoading(true)
    setError('')

    try {
      const [detail, timeline] = await Promise.all([
        api.employeeRuntime.getEmployee(instanceId),
        api.employeeRuntime.getInstanceChatMessages(instanceId),
      ])

      setEmployee(detail)
      setMessages(mapMessages(timeline.messages))
      sessionIdRef.current = timeline.conversationId

      if (directSandboxEndpoint) {
        gatewayEndpointRef.current = directSandboxEndpoint
        try {
          await syncSandboxHistory(directSandboxEndpoint, timeline.conversationId)
          await connectSandboxWs(directSandboxEndpoint)
        } catch {
          // 直连沙箱不可用时，保持后端兜底路径
          gatewayEndpointRef.current = null
          wsRef.current?.disconnect()
          wsRef.current = null
        }
      }
    } catch (requestError: unknown) {
      setError(normalizeErrorMessage(requestError))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!id) {
      setError('实例 ID 缺失')
      setLoading(false)
      return
    }

    void loadChat(id)
  }, [id])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, sending, typing])

  useEffect(() => {
    return () => {
      wsRef.current?.disconnect()
      wsRef.current = null
    }
  }, [])

  async function handleSend() {
    if (!id || !canChat || !isLive) {
      return
    }

    const content = draft.content.trim()
    if (!content || sending) {
      return
    }

    setSending(true)
    setError('')
    const optimistic: InstanceChatMessage = {
      messageId: `local-${Date.now()}`,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }
    setMessages(prev => [...prev, optimistic])
    setDraft({ content: '' })

    const sandboxGatewayEndpoint = gatewayEndpointRef.current
    const sandboxSessionId = sessionIdRef.current
    const sandboxWs = wsRef.current

    if (sandboxGatewayEndpoint && sandboxSessionId && sandboxWs?.isOpen()) {
      try {
        sandboxWs.send({
          type: 'user_message',
          text: content,
          sessionId: sandboxSessionId,
        })
        setTyping(true)
        return
      } catch (requestError: unknown) {
        setError(normalizeErrorMessage(requestError))
      } finally {
        setSending(false)
      }
    }

    try {
      const result = await api.employeeRuntime.sendInstanceChatMessage(id, { content })
      setMessages(prev => {
        const filtered = prev.filter(message => message.messageId !== optimistic.messageId)
        return [...filtered, optimistic, result.assistantMessage]
      })
    } catch (requestError: unknown) {
      setMessages(prev => prev.filter(message => message.messageId !== optimistic.messageId))
      setDraft({ content })
      setError(normalizeErrorMessage(requestError))
    } finally {
      setSending(false)
    }
  }

  async function handleClear() {
    if (!id || clearing) {
      return
    }

    setClearing(true)
    setError('')
    try {
      wsRef.current?.disconnect()
      wsRef.current = null
      setStreamingContent(null)
      setTyping(false)

      await api.employeeRuntime.clearInstanceChatMessages(id)
      setMessages([])

      if (directSandboxEndpoint && sessionIdRef.current) {
        gatewayEndpointRef.current = directSandboxEndpoint
        void connectSandboxWs(directSandboxEndpoint)
      }
    } catch (requestError: unknown) {
      setError(normalizeErrorMessage(requestError))
    } finally {
      setClearing(false)
    }
  }

  const backTarget = employeeView?.ownership === 'department' ? '/department-employees' : '/my-employees'

  return (
    <div className="hb-page hb-page-wide">
      <button type="button" onClick={() => navigate(backTarget)} className="hb-detail-crumb">
        <ArrowLeft size={14} />
        返回{employeeView?.ownership === 'department' ? '部门数字员工' : '我的数字员工'}
      </button>

      {error && (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      )}

      {loading ? (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载分身对话...
        </div>
      ) : !employee || !employeeView ? (
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      ) : !canChat ? (
        <div className="hb-card p-8 text-sm text-[#737373]">
          当前实例不是分身类型，不能进入站内对话。
        </div>
      ) : (
        <div className="hb-chat-shell hb-card">
          <div className="hb-chat-head">
            <div className="flex items-start gap-4">
              <div className={`hb-user-avatar hb-chat-avatar ${ownershipClass(employeeView.ownership)}`}>
                {firstCharacter(employee.nickname)}
              </div>
              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h1 className="text-[22px] font-semibold tracking-[-0.02em] text-[#0a0a0a]">
                    {employee.nickname}
                  </h1>
                  <span className={`hb-pill ${statusClass(employeeView.mappedStatus, employee.lifecycleStatus)}`}>
                    {statusLabel(employeeView.mappedStatus, employee.lifecycleStatus)}
                  </span>
                  <span className={`hb-pill ${ownershipClass(employeeView.ownership)}`}>
                    {ownershipLabel(employeeView.ownership)}
                  </span>
                </div>
                <p className="max-w-[720px] text-sm leading-6 text-[#737373]">
                  这里是你的实例站内对话。消息会直接发送到当前分身，不会进入雇佣流程。
                </p>
                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-[#9ca3af]">
                  <span>实例 ID {employee.employeeId}</span>
                  <span>Owner {employee.ownerUserId}</span>
                  <span>部门 {employee.departmentId || employee.owningTeam}</span>
                </div>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button type="button" className="hb-btn-ghost" onClick={handleClear} disabled={clearing || messages.length === 0}>
                <Trash2 size={14} />
                {clearing ? '清空中' : '清空对话'}
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => navigate(`/instances/${employee.employeeId}`)}
              >
                <MessageCircle size={14} />
                查看详情
              </button>
            </div>
          </div>

          <div className="hb-chat-history">
            {messages.length === 0 ? (
              <div className="hb-chat-empty">
                <MessageCircle size={16} />
                还没有消息，先给分身发一句话吧
              </div>
            ) : (
              messages.map((message) => (
                <div key={message.messageId} className={`hb-chat-message ${message.role === 'assistant' ? 'is-assistant' : 'is-user'}`}>
                  <div className="hb-chat-meta">
                    {message.role === 'assistant' ? employee.nickname : '我'} · {formatTime(message.createdAt)}
                  </div>
                  <div className={`hb-chat-bubble ${message.role === 'assistant' ? 'is-assistant' : 'is-user'}`}>
                    {message.content}
                  </div>
                </div>
              ))
            )}

            {typing && streamingContent && (
              <div className="hb-chat-message is-assistant">
                <div className="hb-chat-meta">{employee.nickname} · 正在回复</div>
                <div className="hb-chat-bubble is-assistant">
                  {normalizeMessageContent(streamingContent)}
                </div>
              </div>
            )}

            {typing && !streamingContent && (
              <div className="hb-chat-message is-assistant">
                <div className="hb-chat-meta">{employee.nickname} · 正在回复</div>
                <div className="hb-chat-bubble is-assistant hb-chat-typing">正在思考中...</div>
              </div>
            )}

            <div ref={bottomRef} />
          </div>

          <div className="hb-chat-compose">
            <div className="flex items-end gap-3">
              <textarea
                value={draft.content}
                onChange={(event) => setDraft({ content: event.target.value })}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                    event.preventDefault()
                    void handleSend()
                  }
                }}
                placeholder={isLive ? '输入消息，Ctrl+Enter 发送' : '当前实例未上岗，不能对话'}
                disabled={sending || !isLive}
              />
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => void handleSend()}
                disabled={sending || !isLive || draft.content.trim().length === 0}
              >
                <Send size={14} />
                发送
              </button>
            </div>
            {!isLive && (
              <p className="mt-3 text-xs text-[#9ca3af]">
                只有 `live` 状态的分身和私有分支才能进入站内对话。
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
