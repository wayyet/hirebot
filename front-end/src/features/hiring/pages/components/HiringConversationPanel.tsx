import type { ReactNode, RefObject } from 'react'
import { useState, useCallback } from 'react'

import { Check, ChevronDown, ChevronUp, Copy, Download, FileCode, FileText, Paperclip, Package, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'

import { InstanceChatMessageBody } from '@/features/team/components/InstanceChatMessageBody'
import type { ChatFile, ChatMessage, ToolStep } from '../hiringPageTypes'
import { ArtifactMessageCard } from './ArtifactMessageCard'
import { HiringToolStepsBlock } from './HiringToolStepsBlock'
import { StageGateCard } from './StageGateCard'

/** 从 bot 消息文本中解析出内联 [FILE_URL:path|filename] 标记，返回干净文本与文件列表 */
const FILE_URL_INLINE_REGEX = /\[FILE_URL:([^\]|]+)(?:\|([^\]]+))?\]/g

function parseInlineFileMarkers(content: string): { text: string; fileMarkers: { path: string; filename: string }[] } {
  const fileMarkers: { path: string; filename: string }[] = []
  const text = content
    .replace(FILE_URL_INLINE_REGEX, (_, path: string, filename?: string) => {
      const cleanPath = path.trim()
      const cleanFilename = filename?.trim() || cleanPath.split('/').pop() || 'file'
      fileMarkers.push({ path: cleanPath, filename: cleanFilename })
      return ''
    })
    .trim()
  return { text, fileMarkers }
}

function getFileIcon(filename: string): ReactNode {
  const ext = filename.split('.').pop()?.toLowerCase() ?? ''
  if (ext === 'zip') return <Package size={14} />
  if (ext === 'json') return <FileCode size={14} />
  return <FileText size={14} />
}

/** 将对话消息列表转换为 Markdown 字符串，便于粘贴给其他 LLM 分析 */
function chatToMarkdown(messages: ChatMessage[], botName: string): string {
  const now = new Date().toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
  const lines: string[] = [
    `# ${i18n.t('hiring.export.title')}`,
    ``,
    `**AI 角色**: ${botName}`,
    `**导出时间**: ${now}`,
    ``,
    `---`,
    ``,
  ]

  for (const msg of messages) {
    if (msg.role === 'user') {
      lines.push(i18n.t('hiring.export.userHeader'))
      lines.push(``)
      lines.push(msg.content)
      if (msg.files && msg.files.length > 0) {
        lines.push(``)
        lines.push(i18n.t('hiring.export.attachments', { files: msg.files.map((f) => f.name).join(', ') }))
      }
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    } else if (msg.role === 'bot') {
      lines.push(i18n.t('hiring.export.botHeader', { name: botName }))
      lines.push(``)
      lines.push(msg.content)
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    } else if (msg.role === 'artifact' && msg.artifact) {
      const a = msg.artifact
      lines.push(i18n.t('hiring.export.artifactHeader', { label: a.label ?? a.artifactType }))
      lines.push(``)
      if (a.kind === 'file') {
        lines.push(i18n.t('hiring.export.fileName', { name: a.fileName ?? '未知' }))
        if (a.sizeLabel) lines.push(i18n.t('hiring.export.fileSize', { size: a.sizeLabel }))
      } else if (a.kind === 'data') {
        lines.push(`\`\`\`json`)
        lines.push(JSON.stringify(a.data, null, 2))
        lines.push(`\`\`\``)
      }
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    } else if (msg.role === 'stage_gate' && msg.stageGate) {
      const sg = msg.stageGate
      lines.push(i18n.t('hiring.export.stageGate', { from: sg.completedStage, to: sg.nextStage }))
      lines.push(``)
      if (!sg.canProceed && sg.blockedReason) {
        lines.push(i18n.t('hiring.export.blockedReason', { reason: sg.blockedReason }))
      }
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    }
  }

  return lines.join('\n')
}

type HiringConversationPanelProps = {
  introName: string
  introAbilities: string
  messages: ChatMessage[]
  typing: boolean
  /** WS 流式内容，非 null 时显示逐字输出气泡 */
  streamingContent?: string | null
  /** 当前轮次正在累积/已完成的 MCP 工具调用步骤，伴随流式气泡展示 */
  streamingToolSteps?: ToolStep[]
  pendingFiles: ChatFile[]
  input: string
  promptPlaceholder: string
  disabled: boolean
  fileInputRef: RefObject<HTMLInputElement | null>
  composerRef: RefObject<HTMLTextAreaElement | null>
  chatEndRef: RefObject<HTMLDivElement | null>
  onInputChange: (value: string) => void
  onSend: () => void
  onFileChange: (files: FileList) => void
  onOpenSkillUpload: () => void
  onRemovePendingFile: (fileId: string) => void
  formatFileSize: (bytes: number) => string
  /** 带 token 的 gateway 文件下载回调 */
  onArtifactFileDownload?: (url: string, fileName: string) => void
  /** 手动触发产物包上传到系统（template_package 卡片展示） */
  onArtifactManualUpload?: (url: string, fileName: string) => void
  /** 工作流连接状态徽标：放在聊天面板顶部 */
  workflowStatus?: {
    title: string
    detail?: string
    tone: 'gray' | 'blue' | 'green' | 'pink'
    onRetry?: () => void
    retryDisabled?: boolean
  } | null
}

export function HiringConversationPanel({
  introName,
  introAbilities,
  messages,
  typing,
  streamingContent,
  streamingToolSteps,
  pendingFiles,
  input,
  promptPlaceholder,
  disabled,
  fileInputRef,
  composerRef,
  chatEndRef,
  onInputChange,
  onSend,
  onFileChange,
  onOpenSkillUpload,
  onRemovePendingFile,
  formatFileSize,
  onArtifactFileDownload,
  onArtifactManualUpload,
  workflowStatus,
}: HiringConversationPanelProps) {
  const { t } = useTranslation()
  const [copied, setCopied] = useState(false)

  const handleCopyAsMarkdown = useCallback(() => {
    if (messages.length === 0) return
    const md = chatToMarkdown(messages, introName)
    void navigator.clipboard.writeText(md).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    })
  }, [messages, introName])

  return (
    <div className="hb-hiring-chat">
      {workflowStatus ? (
        <div className={`hb-hiring-chat-status is-${workflowStatus.tone}`}>
          <span className="hb-hiring-chat-status-dot" aria-hidden="true" />
          <span className="hb-hiring-chat-status-copy">
            <strong>{workflowStatus.title}</strong>
            {workflowStatus.detail ? <span>{workflowStatus.detail}</span> : null}
          </span>
          {workflowStatus.onRetry ? (
            <button
              type="button"
              className="hb-hiring-inline-btn"
              onClick={workflowStatus.onRetry}
              disabled={workflowStatus.retryDisabled}
            >
              {t('hiring.button.retryInit')}
            </button>
          ) : null}
        </div>
      ) : null}
      <div className="hb-hiring-chat-body">
        <InfoCard
          title={t('hiring.intro.title', { name: introName })}
          body={t('hiring.intro.subtitle', { name: introName })}
          detail={
            <>{t('hiring.intro.detail', { name: introName, abilities: introAbilities })}</>
          }
        />

        {messages.map((message) => {
          // artifact 产物卡片：不受 role 方向影响，居左展示
          if (message.role === 'artifact' && message.artifact) {
            return (
              <div key={message.id} className="hb-hiring-msg">
                <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
                <div className="hb-hiring-msg-stack">
                  <ArtifactMessageCard artifact={message.artifact} onFileDownload={onArtifactFileDownload} onManualUpload={onArtifactManualUpload} />
                </div>
              </div>
            )
          }

          // stage_gate 阶段推进卡片：居左展示
          if (message.role === 'stage_gate' && message.stageGate) {
            return (
              <div key={message.id} className="hb-hiring-msg">
                <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
                <div className="hb-hiring-msg-stack">
                  <StageGateCard stageGate={message.stageGate} />
                </div>
              </div>
            )
          }

          // 对 bot 消息解析内联 FILE_URL 标记，从正文中剔除并单独渲染为可下载文件卡片
          const { text: messageText, fileMarkers } = message.role === 'bot'
            ? parseInlineFileMarkers(message.content)
            : { text: message.content, fileMarkers: [] as { path: string; filename: string }[] }

          return (
          <div key={message.id} className={`hb-hiring-msg ${message.role === 'user' ? 'is-user' : ''}`}>
            <div className={`hb-hiring-avatar ${message.role === 'user' ? 'is-user' : ''}`}>
              {message.role === 'user' ? t('hiring.intro.userAvatar') : introName.slice(0, 1).toUpperCase()}
            </div>
            <div className={`hb-hiring-msg-stack ${message.role === 'user' ? 'is-user' : ''}`}>
              {message.role === 'bot' && message.toolSteps && message.toolSteps.length > 0 ? (
                <div className="hb-chat-toolsteps">
                  <HiringToolStepsBlock steps={message.toolSteps} />
                </div>
              ) : null}
              {messageText ? (
                <div className={`hb-hiring-bubble ${message.role === 'user' ? 'is-user' : 'is-bot'}`}>
                  <InstanceChatMessageBody
                    content={messageText}
                    role={message.role === 'user' ? 'user' : 'assistant'}
                  />
                </div>
              ) : null}
              {fileMarkers.length > 0 ? (
                <div className="hb-hiring-inline-file-list">
                  {fileMarkers.map((marker, idx) => (
                    <button
                      key={idx}
                      type="button"
                      className="hb-hiring-inline-file-chip"
                      onClick={() => onArtifactFileDownload?.(marker.path, marker.filename)}
                      title={marker.filename}
                    >
                      {getFileIcon(marker.filename)}
                      <span className="hb-hiring-file-name">{marker.filename}</span>
                      <Download size={12} className="hb-hiring-file-icon" />
                    </button>
                  ))}
                </div>
              ) : null}
              {message.files?.map((file) => (
                <div key={file.id} className="hb-hiring-file-chip">
                  <FileText size={12} className="hb-hiring-file-icon" />
                  <span className="hb-hiring-file-name">{file.name}</span>
                  <span className="hb-hiring-file-size">{formatFileSize(file.size)}</span>
                </div>
              ))}
            </div>
          </div>
          )
        })}

        {/* WS 流式回复：有内容时展示逐字气泡（使用 streaming 模式避免不完整 Markdown 解析卡顿），否则显示 typing 动画 */}
        {streamingContent !== null && streamingContent !== undefined && streamingContent.length > 0 ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
            <div className="hb-hiring-msg-stack">
              {streamingToolSteps && streamingToolSteps.length > 0 ? (
                <div className="hb-chat-toolsteps">
                  <HiringToolStepsBlock steps={streamingToolSteps} />
                </div>
              ) : null}
              <div className="hb-hiring-bubble is-bot">
                <InstanceChatMessageBody
                  content={streamingContent}
                  role="assistant"
                  streaming
                />
              </div>
            </div>
          </div>
        ) : typing ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
            <div className="hb-hiring-msg-stack">
              {streamingToolSteps && streamingToolSteps.length > 0 ? (
                <div className="hb-chat-toolsteps">
                  <HiringToolStepsBlock steps={streamingToolSteps} />
                </div>
              ) : null}
              <div className="hb-hiring-bubble is-bot hb-hiring-bubble-loading">
                {[0, 1, 2].map((index) => (
                  <span
                    key={index}
                    className="hb-hiring-typing-dot"
                    style={{ animationDelay: `${index * 0.15}s` }}
                  />
                ))}
              </div>
            </div>
          </div>
        ) : null}
        <div ref={chatEndRef} />
      </div>

      {pendingFiles.length > 0 ? (
        <div className="hb-hiring-pending">
          {pendingFiles.map((file) => (
            <div key={file.id} className="hb-hiring-file-chip">
              <FileText size={11} className="text-[#9ca3af]" />
              <span className="max-w-[120px] truncate">{file.name}</span>
              <button type="button" onClick={() => onRemovePendingFile(file.id)}>
                <X size={11} className="text-[#9ca3af] hover:text-[#525252]" />
              </button>
            </div>
          ))}
        </div>
      ) : null}

      <div className="hb-hiring-composer">
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="hidden"
          onChange={(event) => {
            if (event.target.files?.length) {
              onFileChange(event.target.files)
              event.target.value = ''
            }
          }}
        />
        <div className="hb-hiring-composer-box">
          <div className="hb-hiring-input-wrap">
            <textarea
              ref={composerRef}
              value={input}
              onChange={(event) => onInputChange(event.target.value)}
              disabled={disabled}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey && !disabled) {
                  event.preventDefault()
                  onSend()
                }
              }}
              rows={2}
              placeholder={promptPlaceholder}
              className="hb-hiring-textarea"
            />
            <div className="hb-hiring-input-toolbar">
              <div className="hb-hiring-composer-tools">
                <button
                  type="button"
                  onClick={() => fileInputRef.current?.click()}
                  disabled={disabled}
                  className="hb-hiring-tool-btn"
                >
                  <Paperclip size={15} />
                  {t('hiring.button.fileUpload')}
                </button>
                <button
                  type="button"
                  onClick={onOpenSkillUpload}
                  disabled={disabled}
                  className="hb-hiring-tool-btn"
                >
                  <Package size={15} />
                  skill
                </button>
                <button
                  type="button"
                  onClick={handleCopyAsMarkdown}
                  disabled={messages.length === 0}
                  className="hb-hiring-tool-btn"
                  title="将对话记录复制为 Markdown，便于粘贴给其他 AI 分析"
                >
                  {copied ? <Check size={15} /> : <Copy size={15} />}
                  {copied ? t('hiring.button.copied') : t('hiring.button.copyChat')}
                </button>
              </div>
              <button
                type="button"
                onClick={onSend}
                disabled={disabled || (!input.trim() && pendingFiles.length === 0)}
                className="hb-hiring-send"
              >
                {t('hiring.button.send')}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

type InfoCardProps = {
  title: string
  body: string
  detail?: ReactNode
  actions?: ReactNode
}

function InfoCard({ title, body, detail, actions }: InfoCardProps) {
  const { t } = useTranslation()
  const [collapsed, setCollapsed] = useState(false)
  return (
    <article className="hb-hiring-info-card">
      <div className="hb-hiring-bubble is-bot is-panel">
        <div className="hb-hiring-info-header">
          <h3 className="hb-hiring-info-title">{title}</h3>
          <button
            type="button"
            className="hb-hiring-info-collapse-btn"
            onClick={() => setCollapsed((v) => !v)}
            title={collapsed ? t('hiring.button.expand') : t('hiring.button.collapse')}
          >
            {collapsed ? <ChevronDown size={16} /> : <ChevronUp size={16} />}
          </button>
        </div>
        {collapsed ? null : (
          <>
            <p className="hb-hiring-info-body">{body}</p>
            {detail ? <div className="hb-hiring-info-detail">{detail}</div> : null}
            {actions ? <div className="hb-hiring-info-actions">{actions}</div> : null}
          </>
        )}
      </div>
    </article>
  )
}
