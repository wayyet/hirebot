import type { ReactNode, RefObject } from 'react'
import { useState, useCallback } from 'react'

import { Check, Copy, FileText, Paperclip, Package, X } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

import type { ChatFile, ChatMessage, ToolStep } from '../hiringPageTypes'
import type { HiringGuideVm } from '../hiringWorkflowViewModel'
import { ArtifactMessageCard } from './ArtifactMessageCard'
import { HiringToolStepsBlock } from './HiringToolStepsBlock'
import { StageGateCard } from './StageGateCard'

/** 将对话消息列表转换为 Markdown 字符串，便于粘贴给其他 LLM 分析 */
function chatToMarkdown(messages: ChatMessage[], botName: string): string {
  const now = new Date().toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai' })
  const lines: string[] = [
    `# 雇佣对话记录`,
    ``,
    `**AI 角色**: ${botName}`,
    `**导出时间**: ${now}`,
    ``,
    `---`,
    ``,
  ]

  for (const msg of messages) {
    if (msg.role === 'user') {
      lines.push(`### 👤 用户`)
      lines.push(``)
      lines.push(msg.content)
      if (msg.files && msg.files.length > 0) {
        lines.push(``)
        lines.push(`*附件: ${msg.files.map((f) => f.name).join(', ')}*`)
      }
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    } else if (msg.role === 'bot') {
      lines.push(`### 🤖 ${botName}`)
      lines.push(``)
      lines.push(msg.content)
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
    } else if (msg.role === 'artifact' && msg.artifact) {
      const a = msg.artifact
      lines.push(`### 📦 产物 · ${a.label ?? a.artifactType}`)
      lines.push(``)
      if (a.kind === 'file') {
        lines.push(`- 文件名: ${a.fileName ?? '未知'}`)
        if (a.sizeLabel) lines.push(`- 大小: ${a.sizeLabel}`)
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
      lines.push(`### 🚦 阶段推进 · ${sg.completedStage} → ${sg.nextStage}`)
      lines.push(``)
      if (!sg.canProceed && sg.blockedReason) {
        lines.push(`> ⚠️ 阻塞原因: ${sg.blockedReason}`)
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
  journeyGuideVisible: boolean
  guideCard: HiringGuideVm
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
  onStartGuide: () => void
  onInputChange: (value: string) => void
  onSend: () => void
  onFileChange: (files: FileList) => void
  onOpenSkillUpload: () => void
  onRemovePendingFile: (fileId: string) => void
  formatFileSize: (bytes: number) => string
  /** 带 token 的 gateway 文件下载回调 */
  onArtifactFileDownload?: (url: string, fileName: string) => void
  /** 工作流连接状态徽标：放在聊天面板顶部 */
  workflowStatus?: {
    label: string
    tone: 'gray' | 'blue' | 'green' | 'pink'
    onRetry?: () => void
    retryDisabled?: boolean
  } | null
}

export function HiringConversationPanel({
  introName,
  introAbilities,
  journeyGuideVisible,
  guideCard,
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
  workflowStatus,
}: HiringConversationPanelProps) {
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
          <span className="hb-hiring-chat-status-dot" />
          <span className="hb-hiring-chat-status-label">{workflowStatus.label}</span>
          {workflowStatus.onRetry ? (
            <button
              type="button"
              className="hb-hiring-inline-btn"
              onClick={workflowStatus.onRetry}
              disabled={workflowStatus.retryDisabled}
            >
              重试初始化
            </button>
          ) : null}
        </div>
      ) : null}
      <div className="hb-hiring-chat-body">
        <InfoCard
          title={`我是${introName}`}
          body={`我们会基于「${introName}」模板完成一条新的部门版雇佣流程。这次我会像一位即将上岗的新同事一样，主动告诉你我还缺什么。`}
          detail={
            <>你好，我是数字员工{introName}，本次会围绕 {introAbilities} 等能力完成资料发现、技能整理、外部系统确认和实例交付。</>
          }
        />

        {messages.map((message) => {
          // artifact 产物卡片：不受 role 方向影响，居左展示
          if (message.role === 'artifact' && message.artifact) {
            return (
              <div key={message.id} className="hb-hiring-msg">
                <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
                <div className="hb-hiring-msg-stack">
                  <ArtifactMessageCard artifact={message.artifact} formatFileSize={formatFileSize} onFileDownload={onArtifactFileDownload} />
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

          return (
          <div key={message.id} className={`hb-hiring-msg ${message.role === 'user' ? 'is-user' : ''}`}>
            <div className={`hb-hiring-avatar ${message.role === 'user' ? 'is-user' : ''}`}>
              {message.role === 'user' ? '你' : introName.slice(0, 1).toUpperCase()}
            </div>
            <div className={`hb-hiring-msg-stack ${message.role === 'user' ? 'is-user' : ''}`}>
              {message.role === 'bot' && message.toolSteps && message.toolSteps.length > 0 ? (
                <HiringToolStepsBlock steps={message.toolSteps} />
              ) : null}
              {message.content ? (
                <div className={`hb-hiring-bubble ${message.role === 'user' ? 'is-user' : 'is-bot'}`}>
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>
                    {message.content}
                  </ReactMarkdown>
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

        {/* WS 流式回复：有内容时展示逐字气泡，否则显示 typing 动画 */}
        {streamingContent !== null && streamingContent !== undefined ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
            <div className="hb-hiring-msg-stack">
              {streamingToolSteps && streamingToolSteps.length > 0 ? (
                <HiringToolStepsBlock steps={streamingToolSteps} />
              ) : null}
              <div className="hb-hiring-bubble is-bot">
                <ReactMarkdown remarkPlugins={[remarkGfm]}>
                  {streamingContent.length > 0 ? streamingContent : '…'}
                </ReactMarkdown>
              </div>
            </div>
          </div>
        ) : typing ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
            <div className="hb-hiring-msg-stack">
              {streamingToolSteps && streamingToolSteps.length > 0 ? (
                <HiringToolStepsBlock steps={streamingToolSteps} />
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
                  文件上传
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
                  {copied ? '已复制' : '复制对话'}
                </button>
              </div>
              <button
                type="button"
                onClick={onSend}
                disabled={disabled || (!input.trim() && pendingFiles.length === 0)}
                className="hb-hiring-send"
              >
                发送
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
  return (
    <article className="hb-hiring-info-card">
      <div className="hb-hiring-bubble is-bot is-panel">
        <h3 className="hb-hiring-info-title">{title}</h3>
        <p className="hb-hiring-info-body">{body}</p>
        {detail ? <div className="hb-hiring-info-detail">{detail}</div> : null}
        {actions ? <div className="hb-hiring-info-actions">{actions}</div> : null}
      </div>
    </article>
  )
}
