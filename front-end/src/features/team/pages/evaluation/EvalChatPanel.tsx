import { useEffect, useRef } from 'react'
import { AlertCircle, Check, CheckCircle2, Copy, Download, FileCode, FileText, Loader2, Package, Paperclip, PlayCircle, SendHorizontal } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { HiringToolStepsBlock } from '@/features/hiring/pages/components/HiringToolStepsBlock'
import { InstanceChatMessageBody } from '@/features/team/components/InstanceChatMessageBody'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import type { EvaluationTestcaseOutline, EvaluationWorkspaceStatus } from '@/infra/api'
import type { EvalChatMessage, EvaluationChatFile, ArtifactTab } from './evaluationTypes'
import { formatDateTime, shortSessionId } from './evaluationUtils'

const FILE_URL_INLINE_REGEX = /\[FILE_URL:([^\]|]+)(?:\|([^\]]+))?\](?:\s*\r?\nAttached file:\s*([^\r\n]+))?/g

type EvalFileMarker = {
  path: string
  fileName: string
}

function safeDecodeFileName(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

function normalizeAttachedFileName(value?: string): string {
  if (!value) return ''
  return value.replace(/\s*\([^)]*\)\s*$/, '').trim()
}

function parseEvalFileMarkers(content: string): { text: string; fileMarkers: EvalFileMarker[] } {
  const fileMarkers: EvalFileMarker[] = []
  const textWithoutMarkers = content
    .replace(FILE_URL_INLINE_REGEX, (_, rawPath: string, rawFileName?: string, rawAttachedFile?: string) => {
      const path = rawPath.trim()
      const fallbackName = path.split(/[\\/]/).pop() || 'file'
      const attachedFileName = normalizeAttachedFileName(rawAttachedFile)
      const fileName = safeDecodeFileName((rawFileName?.trim() || attachedFileName || fallbackName).trim()) || 'file'
      fileMarkers.push({ path, fileName })
      return ''
    })

  const text = (fileMarkers.length > 0
    ? textWithoutMarkers.replace(/(^|\r?\n)\s*上传文件[:：][^\r\n]*(?=\r?\n|$)/g, '\n')
    : textWithoutMarkers
  )
    .replace(/\n{3,}/g, '\n\n')
    .trim()

  return { text, fileMarkers }
}

function getEvalFileIcon(fileName: string) {
  const ext = fileName.split('.').pop()?.toLowerCase() ?? ''
  if (ext === 'zip') return <Package size={14} />
  if (ext === 'json') return <FileCode size={14} />
  return <FileText size={14} />
}

interface EvalChatPanelProps {
  aiRunning: boolean
  chatLoading: boolean
  chatSending: boolean
  chatMessages: EvalChatMessage[]
  streamingContent: string | null
  streamingToolSteps: ToolStep[]
  chatTyping: boolean
  chatInput: string
  chatError: string
  pendingFiles: EvaluationChatFile[]
  sessionSwitching: boolean
  sandboxConnected: boolean
  environmentStatus: { label: string; dotClassName: string }
  workspaceStatus: EvaluationWorkspaceStatus | null
  sessionCopied: boolean
  errorMessage: string
  onCopySessionId: () => void
  testcaseItems: EvaluationTestcaseOutline[]
  canNavigateToHumanEval: boolean
  humanEvalPath: string | null
  humanEvalBannerTone: string
  humanEvalBannerTextTone: string
  humanEvalBannerTitle: string
  humanEvalBannerDescription: string
  enteringHumanEval: boolean
  onSendMessage: (content?: string) => void
  onEnterHumanEval: () => void
  onSetChatInput: (value: string) => void
  onAddPendingFiles: (files: FileList | File[]) => void
  onRemovePendingFile: (fileId: string) => void
  onSetArtifactTab: (tab: ArtifactTab) => void
  onFileDownload: (url: string, fileName: string) => void
}

export function EvalChatPanel({
  aiRunning,
  chatLoading,
  chatSending,
  chatMessages,
  streamingContent,
  streamingToolSteps,
  chatTyping,
  chatInput,
  chatError,
  pendingFiles,
  sessionSwitching,
  sandboxConnected,
  environmentStatus,
  workspaceStatus,
  sessionCopied,
  errorMessage,
  onCopySessionId,
  testcaseItems,
  canNavigateToHumanEval,
  humanEvalPath,
  humanEvalBannerTone,
  humanEvalBannerTextTone,
  humanEvalBannerTitle,
  humanEvalBannerDescription,
  enteringHumanEval,
  onSendMessage,
  onEnterHumanEval,
  onSetChatInput,
  onAddPendingFiles,
  onRemovePendingFile,
  onSetArtifactTab,
  onFileDownload,
}: EvalChatPanelProps) {
  const chatEndRef = useRef<HTMLDivElement>(null)
  const chatInputRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const hasChatTimelineContent = chatLoading || chatMessages.length > 0 || streamingContent !== null || chatTyping

  // 消息更新时自动滚动到底部
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      chatEndRef.current?.scrollIntoView({ behavior: streamingContent !== null ? 'auto' : 'smooth' })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [chatLoading, chatMessages, streamingContent, streamingToolSteps])

  function handleKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      onSendMessage()
    }
  }

  function handleFileInputChange(event: React.ChangeEvent<HTMLInputElement>) {
    const files = event.target.files
    if (files && files.length > 0) {
      onAddPendingFiles(files)
    }
    event.target.value = ''
  }

  return (
    <div className="hb-card eval-chat-wrapper flex min-w-0 flex-1 flex-col overflow-hidden">
      {/* 头部：紧凑单行 + 状态条 */}
      <div className="border-b eval-chat-footer px-4 py-2">
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <span className="text-sm font-semibold eval-text-title">评估对话</span>
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px]">
            {/* 环境状态 */}
            <span className="eval-flow-status-item">
              <span className={environmentStatus.dotClassName} />
              {environmentStatus.label}
            </span>
            <span className="eval-flow-status-divider" aria-hidden="true" />
            {/* WS 连接状态 */}
            <span className={`eval-flow-status-item ${sandboxConnected ? 'eval-flow-status-connected' : 'eval-flow-status-muted'}`}>
              {workspaceStatus?.sessionId ? (sandboxConnected ? '会话已连接' : '会话未连接') : '暂无会话'}
            </span>
            {/* Session ID */}
            {workspaceStatus?.sessionId && (
              <>
                <span className="eval-flow-status-divider" aria-hidden="true" />
                <span className="eval-flow-status-item eval-flow-status-session">
                  <span className="eval-flow-status-label">Session</span>
                  <span className="font-mono eval-flow-status-session-value">{shortSessionId(workspaceStatus.sessionId)}</span>
                  <button
                    type="button"
                    className="eval-flow-copy-btn"
                    onClick={onCopySessionId}
                    title={sessionCopied ? '已复制' : '复制 Session'}
                  >
                    {sessionCopied ? <Check size={12} /> : <Copy size={12} />}
                  </button>
                </span>
              </>
            )}
            {/* 错误信息 */}
            {errorMessage && (
              <>
                <span className="eval-flow-status-divider" aria-hidden="true" />
                <span className="eval-flow-status-item eval-flow-status-error">
                  <AlertCircle size={12} className="shrink-0" />
                  {errorMessage}
                </span>
              </>
            )}
          </div>
        </div>
      </div>

      {/* 消息区 */}
      <div className="flex flex-1 flex-col overflow-hidden eval-chat-bg px-5 pb-4 pt-2">
        {/* 人工评估横幅：评估完成后提示进入人工评估 */}
        {canNavigateToHumanEval && humanEvalPath && (
          <div className={`eval-human-banner mb-3 flex shrink-0 items-center justify-between gap-3 rounded-2xl border px-4 py-3 shadow-sm ${humanEvalBannerTone}`}>
            <div className={`flex items-center gap-2.5 text-sm font-medium ${humanEvalBannerTextTone}`}>
              <CheckCircle2 size={16} className="shrink-0 eval-text-green-mid" />
              <span>
                {humanEvalBannerTitle}
                {humanEvalBannerDescription}
              </span>
            </div>
            <button
              type="button"
              disabled={enteringHumanEval}
              className="hb-btn-primary eval-human-banner-btn shrink-0 !px-3 !py-1.5 !text-[12px] disabled:opacity-60"
              onClick={onEnterHumanEval}
            >
              {enteringHumanEval ? <Loader2 size={12} className="animate-spin" /> : null}
              进入人工评估 →
            </button>
          </div>
        )}

        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          {!aiRunning ? (
            <div className="m-4 rounded-2xl border eval-inactive-tip px-4 py-3 text-sm leading-6">
              请先点击"准备评估环境"。环境就绪后，这里会成为主聊天入口，你可以直接和评估沙箱对话，再结合右侧题卡、轨迹和报告辅助判断。
            </div>
          ) : (
            <>
              {/* 测试用例快捷栏 */}
              {testcaseItems.length > 0 && (
                <div className="shrink-0 border-b eval-chat-footer px-5 py-2.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-[12px] font-medium eval-text-green-mid">✓ 测试用例已就绪</span>
                    <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[11px]">
                      {testcaseItems.length} 个场景
                    </span>
                    {testcaseItems.slice(0, 3).map((outline) => (
                      <span key={outline.testcaseId} className="max-w-[160px] truncate rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px]">
                        {outline.title || outline.testcaseId}
                      </span>
                    ))}
                    {testcaseItems.length > 3 && (
                      <button
                        type="button"
                        className="rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px] eval-text-indigo transition-colors hover:bg-[var(--hb-blue)]/10"
                        onClick={() => onSetArtifactTab('testcase')}
                      >
                        +{testcaseItems.length - 3} 查看全部 →
                      </button>
                    )}
                  </div>
                </div>
              )}

              {/* 消息时间线 */}
              <div className={`eval-chat-timeline flex-1 px-5 py-4 ${hasChatTimelineContent ? 'space-y-3 overflow-y-auto' : 'overflow-y-hidden'}`}>
                {chatLoading ? (
                  <div className="flex items-center gap-2 text-sm text-[var(--hb-soft)]">
                    <Loader2 size={14} className="animate-spin" />
                    正在加载评估沙箱对话...
                  </div>
                ) : chatMessages.length === 0 ? (
                  <div className="flex min-h-full flex-col items-center justify-center gap-3 py-12 text-center">
                    <div className="eval-empty-stage-icon">
                      <PlayCircle size={20} />
                    </div>
                    <div className="mt-2">
                      <p className="text-[14px] font-semibold eval-text-title">
                        {aiRunning ? '评估正在进行中…' : '暂无对话'}
                      </p>
                      <p className="mx-auto mt-1.5 max-w-[320px] text-[12px] leading-6 eval-text-secondary">
                        {aiRunning
                          ? '评估 Agent 正在运行，对话记录将在完成后同步到此处。'
                          : '点击左侧「执行评估」按钮启动评估流程，所有对话和评分结论将同步回此面板。'}
                      </p>
                    </div>
                  </div>
                ) : (
                  // 消息列表
                  chatMessages.map((message) => {
                    const isUser = message.role.toLowerCase() === 'user'
                    const { text: messageText, fileMarkers } = parseEvalFileMarkers(message.content)
                    const hasMessageText = messageText.trim().length > 0
                    return (
                      <div key={message.messageId} className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
                        {!isUser && (
                          <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">评</div>
                        )}
                        <div className={`flex min-w-0 max-w-[90%] flex-col gap-1.5 ${isUser ? 'items-end' : 'items-start'}`}>
                          {!isUser && message.toolSteps && message.toolSteps.length > 0 && (
                            <HiringToolStepsBlock steps={message.toolSteps} />
                          )}
                          {hasMessageText ? (
                            <div
                              className={`hb-chat-bubble rounded-2xl px-3 py-2.5 text-sm leading-6 ${
                                isUser ? 'is-user eval-bubble-user' : 'is-assistant border eval-bubble-bot'
                              }`}
                            >
                              <div className={`mb-1 text-[11px] ${isUser ? 'eval-bubble-meta-user' : 'eval-bubble-meta-bot'}`}>
                                {isUser ? '你' : '评估沙箱'} · {formatDateTime(message.createdAt)}
                              </div>
                              {isUser ? (
                                <div className="whitespace-pre-wrap break-words">{messageText}</div>
                              ) : (
                                <div className="hb-md prose prose-sm max-w-none break-words">
                                  <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                    {messageText}
                                  </ReactMarkdown>
                                </div>
                              )}
                            </div>
                          ) : null}
                          {fileMarkers.length > 0 ? (
                            <div className="hb-hiring-inline-file-list">
                              {fileMarkers.map((marker, index) => (
                                <button
                                  key={`${marker.path}-${index}`}
                                  type="button"
                                  className="hb-hiring-inline-file-chip"
                                  onClick={() => onFileDownload(marker.path, marker.fileName)}
                                  title={marker.fileName}
                                >
                                  {getEvalFileIcon(marker.fileName)}
                                  <span className="hb-hiring-file-name">{marker.fileName}</span>
                                  <Download size={12} className="hb-hiring-file-icon" />
                                </button>
                              ))}
                            </div>
                          ) : null}
                        </div>
                        {isUser && (
                          <div className="hb-hiring-avatar is-user ml-2 mt-0.5 shrink-0">你</div>
                        )}
                      </div>
                    )
                  })
                )}

                {/* 流式回复气泡 */}
                {(streamingContent !== null || chatTyping) && (
                  <div className="flex justify-start">
                    <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">评</div>
                    <div className="flex min-w-0 max-w-[90%] flex-col items-start gap-1.5">
                      {streamingToolSteps.length > 0 && (
                        <HiringToolStepsBlock steps={streamingToolSteps} />
                      )}
                      {chatTyping && streamingContent === '' ? (
                        <div className="hb-chat-bubble hb-hiring-bubble is-assistant is-bot hb-hiring-bubble-loading">
                          {[0, 1, 2].map((i) => (
                            <span
                              key={i}
                              className="hb-hiring-typing-dot"
                              style={{ animationDelay: `${i * 0.15}s` }}
                            />
                          ))}
                        </div>
                      ) : streamingContent ? (
                        <div className="hb-chat-bubble is-assistant rounded-2xl border eval-bubble-bot px-3 py-2.5 text-sm leading-6">
                          <div className="mb-1 text-[11px] eval-bubble-meta-bot">评估沙箱 · 正在回复</div>
                          <InstanceChatMessageBody content={streamingContent} role="assistant" streaming />
                        </div>
                      ) : null}
                    </div>
                  </div>
                )}

                {hasChatTimelineContent ? <div ref={chatEndRef} /> : null}
              </div>

              {/* 输入框 */}
              <div className="border-t eval-chat-footer px-4 py-4">
                {pendingFiles.length > 0 && (
                  <div className="mb-3 flex flex-wrap gap-2">
                    {pendingFiles.map((file) => (
                      <div
                        key={file.id}
                        className={`hb-chat-file-chip is-${
                          file.status === '上传失败'
                            ? 'error'
                            : file.status === '上传中'
                              ? 'loading'
                              : 'ready'
                        }`}
                      >
                        {file.status === '上传中' ? (
                          <Loader2 size={12} className="hb-chat-file-chip-spin" />
                        ) : file.status === '上传失败' ? (
                          <AlertCircle size={12} className="text-[#dc2626]" />
                        ) : (
                          <FileText size={12} className="text-[#9ca3af]" />
                        )}
                        <span className="max-w-[200px] truncate">{file.name}</span>
                        <span className="hb-chat-file-chip-meta">
                          {file.status === '上传失败' ? file.uploadError || file.status : file.status}
                        </span>
                        <button
                          type="button"
                          onClick={() => onRemovePendingFile(file.id)}
                          className="ml-1 text-[#9ca3af] hover:text-[#525252]"
                          aria-label={`移除附件 ${file.name}`}
                          title="移除附件"
                        >
                          ×
                        </button>
                      </div>
                    ))}
                  </div>
                )}
                <div className="hb-chat-composer-box eval-composer-shell flex items-end gap-3 rounded-[24px] border px-4 py-3">
                  <input
                    ref={fileInputRef}
                    type="file"
                    multiple
                    onChange={handleFileInputChange}
                    className="hidden"
                    disabled={chatSending}
                  />
                  <button
                    type="button"
                    className="hb-chat-attach-btn mb-1 !h-11 !w-11"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={chatSending}
                    title="上传文件"
                    aria-label="上传文件"
                  >
                    <Paperclip size={16} />
                  </button>
                  <textarea
                    ref={chatInputRef}
                    value={chatInput}
                    onChange={(event) => onSetChatInput(event.target.value)}
                    onKeyDown={handleKeyDown}
                    rows={2}
                    disabled={chatSending}
                    placeholder="向评估沙箱发送消息（Enter 发送，Shift+Enter 换行）"
                    className="eval-composer-input min-h-[88px] flex-1 resize-none bg-transparent px-1 py-2 text-sm leading-6 outline-none disabled:opacity-60"
                  />
                  <button
                    type="button"
                    disabled={chatSending || (!chatInput.trim() && pendingFiles.length === 0)}
                    className="hb-chat-send-action hb-btn-primary mb-1"
                    aria-label="发送"
                    title="发送"
                    onClick={() => onSendMessage()}
                  >
                    {chatSending ? <Loader2 size={12} className="animate-spin" /> : <SendHorizontal size={12} />}
                  </button>
                </div>
              </div>
            </>
          )}
        </div>

        {chatError && (
          <div className="mt-2 rounded-xl border eval-bar-error px-2.5 py-1.5 text-[11px]">
            {chatError}
          </div>
        )}
        {sessionSwitching && (
          <div className="mt-2 rounded-xl border eval-bar-info px-2.5 py-1.5 text-[11px]">
            正在切换评估会话...
          </div>
        )}
      </div>
    </div>
  )
}
