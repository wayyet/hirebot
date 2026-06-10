import i18n from '@/i18n'

import type { ChatMessage, ToolStep } from '../hiringPageTypes'

export type ChatRenderItem =
  | { kind: 'artifact'; key: string; artifactMessage: ChatMessage }
  | { kind: 'stage_gate'; key: string; stageGateMessage: ChatMessage }
  | { kind: 'message'; key: string; message: ChatMessage; leadingArtifacts?: ChatMessage[] }

export function buildChatRenderItems(messages: ChatMessage[]): ChatRenderItem[] {
  const items: ChatRenderItem[] = []
  let pendingArtifacts: ChatMessage[] = []

  const flushPendingArtifacts = () => {
    for (const artifactMessage of pendingArtifacts) {
      items.push({
        kind: 'artifact',
        key: artifactMessage.id,
        artifactMessage,
      })
    }
    pendingArtifacts = []
  }

  for (const message of messages) {
    if (message.role === 'artifact' && message.artifact) {
      pendingArtifacts.push(message)
      continue
    }

    if (message.role === 'bot') {
      items.push({
        kind: 'message',
        key: message.id,
        message,
        leadingArtifacts: pendingArtifacts.length > 0 ? pendingArtifacts : undefined,
      })
      pendingArtifacts = []
      continue
    }

    // user 消息出现时不 flush 暂存的 artifact，而是继续向前携带，
    // 等到下一条 bot 消息时作为 leadingArtifacts 附在 bot 气泡上方。
    if (message.role === 'stage_gate' && message.stageGate) {
      items.push({
        kind: 'stage_gate',
        key: message.id,
        stageGateMessage: message,
      })
      continue
    }

    items.push({
      kind: 'message',
      key: message.id,
      message,
    })
  }

  flushPendingArtifacts()
  return items
}

type FormattedToolDetail = {
  content: string
  language: 'json' | 'text'
}

type ChatMarkdownOptions = {
  streamingContent?: string | null
  streamingToolSteps?: ToolStep[]
}

function appendArtifactMarkdown(lines: string[], artifactMessage: ChatMessage) {
  if (!artifactMessage.artifact) {
    return
  }

  const artifact = artifactMessage.artifact
  lines.push(i18n.t('hiring.export.artifactHeader', { label: artifact.label ?? artifact.artifactType }))
  lines.push(``)

  if (artifact.kind === 'file') {
    lines.push(i18n.t('hiring.export.fileName', { name: artifact.fileName ?? '未知' }))
    if (artifact.sizeLabel) lines.push(i18n.t('hiring.export.fileSize', { size: artifact.sizeLabel }))
  } else if (artifact.kind === 'data') {
    lines.push(`\`\`\`json`)
    lines.push(JSON.stringify(artifact.data, null, 2))
    lines.push(`\`\`\``)
  }

  lines.push(``)
  lines.push(`---`)
  lines.push(``)
}

function formatToolStepStatus(status: ToolStep['status']): string {
  if (status === 'running') {
    return '运行中'
  }

  if (status === 'error') {
    return '异常'
  }

  return '完成'
}

function formatToolStepDetail(value: string): FormattedToolDetail {
  const trimmed = value.trim()
  if (!trimmed) {
    return { content: '', language: 'text' }
  }

  try {
    return { content: JSON.stringify(JSON.parse(trimmed), null, 2), language: 'json' }
  } catch {
    return { content: value, language: 'text' }
  }
}

function markdownFenceFor(content: string): string {
  const matches = content.match(/`{3,}/g)
  const longestFence = matches?.reduce((max, fence) => Math.max(max, fence.length), 2) ?? 2
  return '`'.repeat(Math.max(3, longestFence + 1))
}

function appendFencedMarkdown(lines: string[], detail: FormattedToolDetail) {
  const fence = markdownFenceFor(detail.content)
  lines.push(`${fence}${detail.language === 'json' ? 'json' : ''}`)
  lines.push(detail.content)
  lines.push(fence)
}

function appendToolStepDetailMarkdown(lines: string[], label: string, value?: string) {
  if (!value || !value.trim()) {
    return
  }

  const detail = formatToolStepDetail(value)
  lines.push(``)
  lines.push(`**${label}**`)
  appendFencedMarkdown(lines, detail)
}

function appendToolStepsMarkdown(lines: string[], steps: ToolStep[] | undefined, title = '工具调用') {
  if (!steps || steps.length === 0) {
    return
  }

  lines.push(`#### ${title} (${steps.length})`)
  lines.push(``)

  steps.forEach((step, index) => {
    const toolName = (step.name || 'tool').replace(/`/g, '\\`')
    const hasDetail = Boolean(step.args?.trim() || step.result?.trim())

    lines.push(`##### ${index + 1}. \`${toolName}\``)
    lines.push(`- 状态: ${formatToolStepStatus(step.status)}`)

    appendToolStepDetailMarkdown(lines, '参数', step.args)
    appendToolStepDetailMarkdown(lines, '返回', step.result)

    if (!hasDetail) {
      lines.push(`- 详情: 无`)
    }

    lines.push(``)
  })
}

function appendActiveStreamingMarkdown(lines: string[], botName: string, options?: ChatMarkdownOptions) {
  const streamingContent = options?.streamingContent?.trim() ?? ''
  const streamingToolSteps = options?.streamingToolSteps ?? []
  if (!streamingContent && streamingToolSteps.length === 0) {
    return
  }

  lines.push(i18n.t('hiring.export.botHeader', { name: botName }))
  lines.push(``)

  if (streamingContent) {
    lines.push(streamingContent)
    lines.push(``)
  }

  appendToolStepsMarkdown(lines, streamingToolSteps, '当前轮次工具调用')

  lines.push(`---`)
  lines.push(``)
}

/** 将对话消息列表转换为 Markdown 字符串，便于粘贴给其他 LLM 分析。 */
export function chatToMarkdown(messages: ChatMessage[], botName: string, options?: ChatMarkdownOptions): string {
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

  for (const item of buildChatRenderItems(messages)) {
    if (item.kind === 'artifact') {
      appendArtifactMarkdown(lines, item.artifactMessage)
      continue
    }

    if (item.kind === 'stage_gate') {
      const sg = item.stageGateMessage.stageGate
      if (!sg) {
        continue
      }

      lines.push(i18n.t('hiring.export.stageGate', { from: sg.completedStage, to: sg.nextStage }))
      lines.push(``)
      if (!sg.canProceed && sg.blockedReason) {
        lines.push(i18n.t('hiring.export.blockedReason', { reason: sg.blockedReason }))
      }
      lines.push(``)
      lines.push(`---`)
      lines.push(``)
      continue
    }

    for (const artifactMessage of item.leadingArtifacts ?? []) {
      appendArtifactMarkdown(lines, artifactMessage)
    }

    const msg = item.message
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
      appendToolStepsMarkdown(lines, msg.toolSteps)
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

  appendActiveStreamingMarkdown(lines, botName, options)

  return lines.join('\n')
}
