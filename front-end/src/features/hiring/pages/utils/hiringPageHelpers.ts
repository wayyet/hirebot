import i18n from '@/i18n'

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
 * 规范化 AI 回复内容（移除 <think> 标签）
 */
export function normalizeAssistantReply(content: string): string {
  const cleaned = content.replace(/<think>[\s\S]*?<\/think>/gi, '').trim()
  return cleaned.length > 0 ? cleaned : content.trim()
}
