import i18n from '@/i18n'

const HIDDEN_ASSISTANT_TAG_PATTERN = 'think|dispatch|dispatch_callback|diagnostic_report|config_governance_patch'
const HIDDEN_ASSISTANT_TAG_REGEX = new RegExp(
  `<(${HIDDEN_ASSISTANT_TAG_PATTERN})\\b[^>]*>[\\s\\S]*?<\\/\\1>`,
  'gi',
)
const HIDDEN_ASSISTANT_OPEN_TAG_REGEX = new RegExp(
  `<(${HIDDEN_ASSISTANT_TAG_PATTERN})\\b[^>]*>`,
  'gi',
)
const INTERNAL_INSTRUCTION_LINE_REGEX =
  /^.*(?:\[Internal (?:stage resume|downstream trigger|packaging trigger|skill definition confirmation|external config commit)[^\]]*\]|Internal stage resume|Internal downstream trigger|Internal packaging trigger|Internal skill definition confirmation).*$(?:\r?\n)?/gim

function removeHiddenAssistantProtocol(content: string): string {
  return content
    .replace(HIDDEN_ASSISTANT_TAG_REGEX, '')
    .replace(INTERNAL_INSTRUCTION_LINE_REGEX, '')
    .trim()
}

/**
 * 生成唯一 ID（时间戳 + 随机字符串）
 */
export function mkId(): string {
  return `${Date.now()}_${Math.random().toString(36).slice(2)}`
}

/**
 * 延迟指定毫秒
 */
export function sleep(ms: number): Promise<void> {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, ms)
  })
}

/**
 * 规范化错误消息
 */
export function normalizeErrorMessage(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message
  }

  return i18n.t('hiring.error.networkFailure')
}

/**
 * 规范化 AI 回复内容，移除仅供协议同步使用的内部标签，避免技术 JSON 直接展示给用户
 */
export function normalizeAssistantReply(content: string): string {
  return removeHiddenAssistantProtocol(content)
}

/**
 * 规范化流式预览内容。
 * 流式阶段可能只收到内部标签的开头，此时需要从标签起始处立刻截断，
 * 避免 technical artifact 的 JSON 在打字机过程中闪到界面上。
 */
export function normalizeAssistantStreamingPreview(content: string): string {
  const withoutClosedTags = content.replace(HIDDEN_ASSISTANT_TAG_REGEX, '')
  const lowerContent = withoutClosedTags.toLowerCase()
  HIDDEN_ASSISTANT_OPEN_TAG_REGEX.lastIndex = 0

  let match: RegExpExecArray | null = null
  while ((match = HIDDEN_ASSISTANT_OPEN_TAG_REGEX.exec(withoutClosedTags)) !== null) {
    const tagName = match[1].toLowerCase()
    const closingTag = `</${tagName}>`
    const closingIndex = lowerContent.indexOf(closingTag, match.index + match[0].length)
    if (closingIndex >= 0) {
      continue
    }

    return withoutClosedTags.slice(0, match.index).trim()
  }

  return withoutClosedTags.replace(INTERNAL_INSTRUCTION_LINE_REGEX, '').trim()
}
