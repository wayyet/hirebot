import { useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, ArrowLeft, Loader2, MessageCircle, Send, Trash2 } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeDetail, type InstanceChatMessage } from '@/infra/api'
import { firstCharacter, ownershipClass, ownershipLabel, statusClass, statusLabel, toEmployeeDetailSummary, withEmployeeView } from '@/features/hiring/pages/employeeView'

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

export default function InstanceChatPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [messages, setMessages] = useState<InstanceChatMessage[]>([])
  const [draft, setDraft] = useState<ChatDraft>({ content: '' })
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const [clearing, setClearing] = useState(false)
  const bottomRef = useRef<HTMLDivElement | null>(null)

  const employeeView = useMemo(() => {
    if (!employee) return null
    return withEmployeeView(toEmployeeDetailSummary(employee))
  }, [employee])

  const canChat = employeeView?.ownership === 'personal_clone' || employeeView?.ownership === 'private_branch'
  const isLive = employeeView?.mappedStatus === 'live'

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
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '无法加载分身对话')
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
  }, [messages, sending])

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

    try {
      const result = await api.employeeRuntime.sendInstanceChatMessage(id, { content })
      setMessages(prev => {
        const filtered = prev.filter(message => message.messageId !== optimistic.messageId)
        return [...filtered, optimistic, result.assistantMessage]
      })
    } catch (requestError: unknown) {
      setMessages(prev => prev.filter(message => message.messageId !== optimistic.messageId))
      setDraft({ content })
      setError(requestError instanceof Error ? requestError.message : '发送消息失败')
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
      await api.employeeRuntime.clearInstanceChatMessages(id)
      setMessages([])
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '清空对话失败')
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
            {sending && (
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
