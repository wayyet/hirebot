import { useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, ArrowLeft, Loader2, SendHorizontal, Trash2 } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { useUxOverlay } from '@/app/context/UxOverlayContext'
import { api, type EmployeeDetail, type InstanceChatMessage } from '@/infra/api'
import { firstCharacter, toEmployeeDetailSummary, withEmployeeView } from './employeeView'

function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '刚刚'
  return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

function isUserMessage(message: InstanceChatMessage) {
  const role = message.role.toLowerCase()
  return role === 'user' || role === 'human'
}

export default function InstanceChatPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showToast } = useUxOverlay()
  const endRef = useRef<HTMLDivElement | null>(null)

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [messages, setMessages] = useState<InstanceChatMessage[]>([])
  const [draft, setDraft] = useState('')
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')

  const employeeView = useMemo(() => {
    return employee ? withEmployeeView(toEmployeeDetailSummary(employee)) : null
  }, [employee])

  useEffect(() => {
    if (!id) {
      setError('实例 ID 缺失')
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)
    setError('')

    Promise.all([
      api.employeeRuntime.getEmployee(id),
      api.employeeRuntime.getInAppChatMessages(id),
    ])
      .then(([detail, timeline]) => {
        if (cancelled) return
        setEmployee(detail)
        setMessages(timeline.messages ?? [])
      })
      .catch((requestError: unknown) => {
        if (cancelled) return
        setError(requestError instanceof Error ? requestError.message : '加载站内对话失败')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [id])

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, sending])

  const canChat = employeeView?.ownership !== 'department' && employeeView?.mappedStatus === 'live'

  async function sendMessage() {
    if (!id || sending || !draft.trim() || !canChat) return
    const content = draft.trim()
    const optimistic: InstanceChatMessage = {
      messageId: `local_${Date.now()}`,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }

    setDraft('')
    setSending(true)
    setError('')
    setMessages((previous) => [...previous, optimistic])

    try {
      const result = await api.employeeRuntime.sendInAppChatMessage(id, { content })
      setMessages((previous) => [
        ...previous.filter((item) => item.messageId !== optimistic.messageId),
        optimistic,
        result.assistantMessage,
      ])
    } catch (requestError: unknown) {
      setMessages((previous) => previous.filter((item) => item.messageId !== optimistic.messageId))
      setError(requestError instanceof Error ? requestError.message : '消息发送失败')
    } finally {
      setSending(false)
    }
  }

  async function clearMessages() {
    if (!id || sending) return
    setError('')
    try {
      const timeline = await api.employeeRuntime.clearInAppChatMessages(id)
      setMessages(timeline.messages ?? [])
      showToast('站内对话历史已清空', 'success')
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '清空对话失败')
    }
  }

  return (
    <div className="hb-page space-y-5">
      <button type="button" onClick={() => navigate('/my-employees')} className="hb-btn-ghost">
        <ArrowLeft size={14} />
        返回我的数字员工
      </button>

      {loading ? (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载站内对话...
        </div>
      ) : !employee || !employeeView ? (
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      ) : (
        <section className="hb-card overflow-hidden p-0">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[#ececec] p-5">
            <div className="flex items-center gap-3">
              <span className="hb-squircle h-12 w-12 bg-[#ece7fb] text-[#6a5acd]">
                {firstCharacter(employee.nickname)}
              </span>
              <div>
                <h1 className="text-xl font-semibold text-[#0a0a0a]">{employee.nickname}</h1>
                <p className="mt-1 text-xs text-[#737373]">
                  站内会话与外部 IM 独立上下文，历史按实例隔离。
                </p>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" className="hb-btn-ghost" onClick={() => navigate(`/instances/${employee.employeeId}/onboarding`)}>
                配置 IM
              </button>
              <button type="button" className="hb-btn-ghost" onClick={() => void clearMessages()} disabled={sending}>
                <Trash2 size={14} />
                清空历史
              </button>
            </div>
          </div>

          {error && (
            <div className="mx-5 mt-4 rounded-2xl border border-[#ffd5da] bg-[#fff1f2] p-3 text-sm text-[#b3263c]">
              <span className="inline-flex items-center gap-2">
                <AlertCircle size={14} />
                {error}
              </span>
            </div>
          )}

          {!canChat ? (
            <div className="p-10 text-center">
              <h2 className="text-base font-semibold text-[#0a0a0a]">该实例当前不可进入站内对话</h2>
              <p className="mt-2 text-sm text-[#737373]">
                只有已上岗的我的分身或私有分支可以直接对话；部门员工请先复制为个人分身。
              </p>
            </div>
          ) : (
            <>
              <div className="max-h-[58vh] min-h-[380px] space-y-4 overflow-y-auto bg-[#fafafa] p-5">
                {messages.length === 0 && (
                  <div className="rounded-2xl border border-[#ececec] bg-white p-4 text-sm text-[#737373]">
                    你好，我是 {employee.nickname}。我们可以先从当前最重要的任务开始。
                  </div>
                )}
                {messages.map((message) => {
                  const mine = isUserMessage(message)
                  return (
                    <div key={message.messageId} className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
                      <div className={`max-w-[78%] ${mine ? 'text-right' : 'text-left'}`}>
                        <div className="mb-1 text-xs text-[#737373]">
                          {mine ? '你' : employee.nickname} · {formatTime(message.createdAt)}
                        </div>
                        <div className={`rounded-2xl px-4 py-3 text-sm leading-relaxed ${
                          mine ? 'bg-[#0a0a0a] text-white' : 'border border-[#ececec] bg-white text-[#404040]'
                        }`}>
                          {message.content}
                        </div>
                      </div>
                    </div>
                  )
                })}
                {sending && (
                  <div className="flex justify-start">
                    <div className="rounded-2xl border border-[#ececec] bg-white px-4 py-3 text-sm text-[#737373]">
                      正在整理回答...
                    </div>
                  </div>
                )}
                <div ref={endRef} />
              </div>

              <div className="border-t border-[#ececec] bg-white p-5">
                <textarea
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  placeholder="输入你的问题，Enter 发送，Shift+Enter 换行"
                  rows={3}
                  className="w-full resize-none rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' && !event.shiftKey) {
                      event.preventDefault()
                      void sendMessage()
                    }
                  }}
                />
                <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                  <span className="text-xs text-[#737373]">默认保留后端返回的站内消息历史。</span>
                  <button type="button" className="hb-btn-primary" onClick={() => void sendMessage()} disabled={sending || !draft.trim()}>
                    {sending ? <Loader2 size={14} className="animate-spin" /> : <SendHorizontal size={14} />}
                    发送
                  </button>
                </div>
              </div>
            </>
          )}
        </section>
      )}
    </div>
  )
}
