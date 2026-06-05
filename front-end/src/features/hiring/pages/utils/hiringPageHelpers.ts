import i18n from '@/i18n'

const HIDDEN_ASSISTANT_TAG_REGEX = /<(think|dispatch|dispatch_callback|diagnostic_report|config_governance_patch)\b[^>]*>[\s\S]*?<\/\1>/gi

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
  return content
    .replace(HIDDEN_ASSISTANT_TAG_REGEX, '')
    .trim()
}
