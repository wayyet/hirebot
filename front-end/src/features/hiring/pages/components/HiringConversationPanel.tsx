import type { ReactNode, RefObject } from 'react'

import { FileText, Paperclip, Package, X } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

import type { ChatFile, ChatMessage } from '../hiringPageTypes'
import type { HiringGuideVm } from '../hiringWorkflowViewModel'

type HiringConversationPanelProps = {
  introName: string
  introAbilities: string
  journeyGuideVisible: boolean
  guideCard: HiringGuideVm
  messages: ChatMessage[]
  typing: boolean
  /** WS 流式内容，非 null 时显示逐字输出气泡 */
  streamingContent?: string | null
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
}

export function HiringConversationPanel({
  introName,
  introAbilities,
  journeyGuideVisible,
  guideCard,
  messages,
  typing,
  streamingContent,
  pendingFiles,
  input,
  promptPlaceholder,
  disabled,
  fileInputRef,
  composerRef,
  chatEndRef,
  onStartGuide,
  onInputChange,
  onSend,
  onFileChange,
  onOpenSkillUpload,
  onRemovePendingFile,
  formatFileSize,
}: HiringConversationPanelProps) {
  return (
    <div className="hb-hiring-chat">
      <div className="hb-hiring-panel-head">
        <div className="hb-hiring-employee-chip">
          <div className="hb-hiring-employee-avatar">{introName.slice(0, 1).toUpperCase()}</div>
          <div>
            <strong>{introName}</strong>
            <span>模板新雇佣</span>
          </div>
        </div>
      </div>

      <div className="hb-hiring-chat-body">
        <InfoCard
          title={`我是${introName}`}
          body={`我们会基于「${introName}」模板完成一条新的部门版雇佣流程。这次我会像一位即将上岗的新同事一样，主动告诉你我还缺什么。`}
          detail={
            <>你好，我是数字员工{introName}，本次会围绕 {introAbilities} 等能力完成资料发现、技能整理、外部系统确认和实例交付。</>
          }
        />
        <InfoCard
          title="这些是我要完成的入职事项"
          body="资料、技能、系统和实例包会在同一条会话里逐步闭环。右侧会同步记录每一项进度。"
          actions={!journeyGuideVisible ? (
            <button type="button" className="hb-hiring-card-action primary" onClick={onStartGuide}>
              开始第一步
            </button>
          ) : undefined}
        />
        {journeyGuideVisible && (
          <InfoCard
            title={guideCard.title}
            body={guideCard.description}
            detail={(
              <div className="hb-hiring-guide-list">
                <div className="hb-hiring-guide-item">
                  <strong>{guideCard.bulletTitle}</strong>
                  <span>{guideCard.bulletBody}</span>
                </div>
                <div className="hb-hiring-guide-item">
                  <strong>当前状态</strong>
                  <span>{guideCard.statusText}</span>
                </div>
                {guideCard.hints.map((hint) => (
                  <div key={hint} className="hb-hiring-guide-item is-muted">
                    <strong>提示</strong>
                    <span>{hint}</span>
                  </div>
                ))}
              </div>
            )}
          />
        )}

        {messages.map((message) => (
          <div key={message.id} className={`hb-hiring-msg ${message.role === 'user' ? 'is-user' : ''}`}>
            <div className={`hb-hiring-avatar ${message.role === 'user' ? 'is-user' : ''}`}>
              {message.role === 'user' ? '你' : introName.slice(0, 1).toUpperCase()}
            </div>
            <div className={`hb-hiring-msg-stack ${message.role === 'user' ? 'is-user' : ''}`}>
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
        ))}

        {/* WS 流式回复：有内容时展示逐字气泡，否则显示 typing 动画 */}
        {streamingContent !== null && streamingContent !== undefined ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
            <div className="hb-hiring-bubble is-bot">
              <ReactMarkdown remarkPlugins={[remarkGfm]}>
                {streamingContent.length > 0 ? streamingContent : '…'}
              </ReactMarkdown>
            </div>
          </div>
        ) : typing ? (
          <div className="hb-hiring-msg">
            <div className="hb-hiring-avatar">{introName.slice(0, 1).toUpperCase()}</div>
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
              rows={3}
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
